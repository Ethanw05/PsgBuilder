using System.Buffers.Binary;

namespace PsgBuilder.Core.Psg;

/// <summary>
/// Data-driven Arena writer. Writes any <see cref="PsgArenaSpec"/> to a stream
/// (header, sections, objects, dictionary, optional subrefs).
/// All object TypeIds must exist in the spec's TypeRegistry; validation fails fast.
/// </summary>
public static class GenericArenaWriter
{
    private const int HeaderSize = 0xC0;
    private const int SectionsSize = 0x180;
    private const int ObjectsStart = 0x240;
    private const int DictEntrySize = 24;
    private const int SubrefRecordSize = 8;
    private const int Alignment = 16;

    /// <summary>
    /// Validates the spec and writes the Arena file to <paramref name="output"/>.
    /// </summary>
    public static void Write(PsgArenaSpec spec, Stream output)
    {
        if (spec == null) throw new ArgumentNullException(nameof(spec));
        if (output == null) throw new ArgumentNullException(nameof(output));

        var objects = spec.Objects ?? Array.Empty<PsgObjectSpec>();
        var typeRegistry = spec.TypeRegistry ?? Array.Empty<uint>();

        // Validation: every object TypeId must be in TypeRegistry
        for (int i = 0; i < objects.Count; i++)
        {
            uint typeId = objects[i].TypeId;
            if (Array.IndexOf(typeRegistry, typeId) < 0)
                throw new InvalidOperationException(
                    $"Object[{i}] has TypeId 0x{typeId:X8} which is not in the TypeRegistry.");
        }

        var blob = new List<byte>();
        blob.AddRange(new byte[HeaderSize]);

        // Sections (0x180): Manifest, Types, ExternalArenas, Subreferences, Atoms
        blob.AddRange(BuildSections(spec.ArenaId, typeRegistry, spec.Subrefs));

        while (blob.Count < ObjectsStart)
            blob.Add(0);

        // Object layout: compute offset and typeIndex per object.
        // When DeferBaseResourceLayout: write non-BaseResource first, then BaseResource,
        // so metadata gets low ptrs (0x2BD8...) and BaseResource block at main_base.
        int firstBaseResourceOffset = -1;
        var dictEntries = new List<(uint Ptr, int Size, int TypeIndex, uint TypeId)>();

        if (spec.DeferBaseResourceLayout)
        {
            // Mesh layout per real PSG: metadata at low ptrs (0x2BD8...), dict at 0x462C, BaseResource block at main_base (0x563C).
            // Phase 1: Write non-BaseResource objects only (metadata gets absolute ptrs).
            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (obj.IsBaseResource)
                {
                    dictEntries.Add((0, obj.Data.Length, Array.IndexOf(typeRegistry, obj.TypeId), obj.TypeId)); // ptr filled in phase 3
                    continue;
                }
                int align = (int)(obj.Alignment > 0 ? obj.Alignment : Alignment);
                while (blob.Count % align != 0) blob.Add(0);
                int offset = blob.Count;
                blob.AddRange(obj.Data);
                dictEntries.Add(((uint)offset, obj.Data.Length, Array.IndexOf(typeRegistry, obj.TypeId), obj.TypeId));
            }
            // Phase 2: Write dict, then subrefs (BaseResource block goes after these).
        }
        else
        {
            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                int align = (int)(obj.Alignment > 0 ? obj.Alignment : Alignment);
                while (blob.Count % align != 0) blob.Add(0);
                int offset = blob.Count;
                blob.AddRange(obj.Data);

                int typeIndex = Array.IndexOf(typeRegistry, obj.TypeId);
                uint ptr;
                if (obj.IsBaseResource)
                {
                    if (firstBaseResourceOffset < 0)
                        firstBaseResourceOffset = offset;
                    ptr = (uint)(offset - firstBaseResourceOffset);
                }
                else
                {
                    ptr = (uint)offset;
                }

                dictEntries.Add((ptr, obj.Data.Length, typeIndex, obj.TypeId));
            }
        }

        int dictStart = Align(blob.Count, Alignment);
        while (blob.Count < dictStart) blob.Add(0);

        int subrefRecordsStart = 0;
        int subrefDictStart = 0;

        if (spec.DeferBaseResourceLayout)
        {
            // Mesh layout: metadata → dict → subrefs → BaseResource block. Dict must contain BaseResource ptrs,
            // so we reserve dict space, write subrefs, write BaseResource block, then backfill dict.
            int dictSize = objects.Count * DictEntrySize;
            for (int i = 0; i < dictSize; i++) blob.Add(0);

            if (spec.Subrefs != null && spec.Subrefs.Records.Count > 0)
            {
                // Per real mesh dumps (F72E84DE): records first (lower offset), then dict (higher).
                subrefRecordsStart = blob.Count;
                foreach (var rec in spec.Subrefs.Records)
                {
                    blob.AddRange(BeU32(rec.ObjectDictIndex));
                    blob.AddRange(BeU32(rec.OffsetInObject));
                }
                subrefDictStart = blob.Count;
                WriteSubrefDictionary(blob, spec.Subrefs.Records.Count);
            }

            while (blob.Count % 4 != 0) blob.Add(0);
            for (int i = 0; i < 16; i++) blob.Add(0);

            // Phase 3: BaseResource block at main_base
            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (!obj.IsBaseResource) continue;
                int align = (int)(obj.Alignment > 0 ? obj.Alignment : Alignment);
                while (blob.Count % align != 0) blob.Add(0);
                int offset = blob.Count;
                if (firstBaseResourceOffset < 0)
                    firstBaseResourceOffset = offset;
                blob.AddRange(obj.Data);
                uint ptr = (uint)(offset - firstBaseResourceOffset);
                dictEntries[i] = (ptr, obj.Data.Length, Array.IndexOf(typeRegistry, obj.TypeId), obj.TypeId);
            }

            // Backfill dict after we have BaseResource ptrs (done below via finalBlob)
        }
        else
        {
            blob.AddRange(BuildDictionary(dictEntries, spec.DictRelocIsZero));
        }

        if (!spec.DeferBaseResourceLayout && spec.Subrefs != null && spec.Subrefs.Records.Count > 0)
        {
            // Per real mesh dumps (F72E84DE): records first (lower offset), then dict (higher).
            subrefRecordsStart = blob.Count;
            foreach (var rec in spec.Subrefs.Records)
            {
                blob.AddRange(BeU32(rec.ObjectDictIndex));
                blob.AddRange(BeU32(rec.OffsetInObject));
            }
            subrefDictStart = blob.Count;
            WriteSubrefDictionary(blob, spec.Subrefs.Records.Count);
        }

        if (!spec.DeferBaseResourceLayout)
        {
            while (blob.Count % 4 != 0) blob.Add(0);
            for (int i = 0; i < 16; i++) blob.Add(0);
        }

        var finalBlob = blob.ToArray();

        if (spec.DeferBaseResourceLayout)
        {
            var dictBytes = BuildDictionary(dictEntries, spec.DictRelocIsZero);
            dictBytes.CopyTo(finalBlob.AsSpan(dictStart, dictBytes.Length));
        }
        // Backfill header (per Python _fill_header): build blob first, then write header with final values
        int mainBase = firstBaseResourceOffset >= 0 ? firstBaseResourceOffset : 0;
        int valueAt0x44 = spec.UseFileSizeAt0x44 ? finalBlob.Length : mainBase;

        // For mesh PSG (BaseResource type 0x00010034), real files store BaseResource span at 0x6C:
        // byte_count from main_base to EOF. Collision PSGs keep this as 0.
        int valueAt0x6C = (!spec.UseFileSizeAt0x44 && mainBase > 0 && mainBase <= finalBlob.Length)
            ? finalBlob.Length - mainBase
            : 0;

        WriteHeader(
            finalBlob,
            spec.ArenaId,
            objects.Count,
            dictStart,
            valueAt0x44,
            valueAt0x6C,
            spec.HeaderTypeIdAt0x70);

        // Backfill subref section (per Python _update_subref_pointers): dict + records = absolute file offsets
        if (spec.Subrefs != null && spec.Subrefs.Records.Count > 0)
            BackfillSubrefSection(finalBlob, HeaderSize + 0x14C, spec.Subrefs.Records.Count, subrefRecordsStart, subrefDictStart);

        output.Write(finalBlob, 0, finalBlob.Length);
    }

    private static int Align(int n, int a) => (n + a - 1) & ~(a - 1);

    private static byte[] BeU32(uint v)
    {
        var s = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(s, v);
        return s;
    }

    private static byte[] BuildSections(uint arenaId, uint[] typeRegistry, PsgSubrefSpec? subrefs)
    {
        var sec = new List<byte>();
        // Manifest 0x00: type 0x00010004, numEntries 4, dict at 0x0C → [0x1C, 0x128, 0x14C, 0x168]
        sec.AddRange(BeU32(0x00010004));
        sec.AddRange(BeU32(4));
        sec.AddRange(BeU32(0x0C));
        sec.AddRange(BeU32(0x1C));
        sec.AddRange(BeU32(0x128));
        sec.AddRange(BeU32(0x14C));
        sec.AddRange(BeU32(0x168));

        // Types 0x1C: type 0x00010005, numEntries = typeRegistry.Length, dict at 0x0C
        int typeCount = typeRegistry.Length;
        sec.AddRange(BeU32(0x00010005));
        sec.AddRange(BeU32((uint)typeCount));
        sec.AddRange(BeU32(0x0C));
        foreach (uint tid in typeRegistry)
            sec.AddRange(BeU32(tid));
        while (sec.Count < 0x128)
            sec.Add(0);

        // ExternalArenas 0x128
        sec.AddRange(BeU32(0x00010006));
        sec.AddRange(BeU32(3));
        sec.AddRange(BeU32(0x18));
        sec.AddRange(BeU32(arenaId));
        sec.AddRange(BeU32(0xFFB00000));
        sec.AddRange(BeU32(arenaId));
        sec.AddRange(BeU32(0));
        sec.AddRange(BeU32(0));
        sec.AddRange(BeU32(0));
        while (sec.Count < 0x14C)
            sec.Add(0);

        // Subreferences 0x14C (0x1C = 28 bytes per ArenaStuctDocumentation):
        // typeReg, numEntries, m_dictAfterRefix, m_recordsAfterRefix, dict, records, numUsed.
        // BackfillSubrefSection overwrites numEntries, dict, records, numUsed.
        int subrefCount = subrefs?.Records.Count ?? 0;
        sec.AddRange(BeU32(0x00010007));
        sec.AddRange(BeU32((uint)subrefCount));
        sec.AddRange(BeU32(0)); // m_dictAfterRefix
        sec.AddRange(BeU32(0)); // m_recordsAfterRefix
        sec.AddRange(BeU32(0)); // dict (backfilled)
        sec.AddRange(BeU32(0)); // records (backfilled)
        sec.AddRange(BeU32((uint)subrefCount)); // numUsed (backfilled, but set for consistency)
        while (sec.Count < 0x168)
            sec.Add(0);

        // Atoms 0x168 (12 bytes: typeReg, numEntries, dict — matches real ArenaSectionAtoms)
        sec.AddRange(BeU32(0x00010008));
        sec.AddRange(BeU32(0));
        sec.AddRange(BeU32(0)); // dict
        while (sec.Count < SectionsSize)
            sec.Add(0);
        return sec.ToArray();
    }

    private static byte[] BuildDictionary(IReadOnlyList<(uint Ptr, int Size, int TypeIndex, uint TypeId)> entries, bool dictRelocIsZero)
    {
        var blob = new byte[entries.Count * DictEntrySize];
        var s = blob.AsSpan();
        for (int i = 0; i < entries.Count; i++)
        {
            var (ptr, size, typeIndex, typeId) = entries[i];
            int baseOff = i * DictEntrySize;
            BinaryPrimitives.WriteUInt32BigEndian(s.Slice(baseOff, 4), ptr);
            BinaryPrimitives.WriteUInt32BigEndian(s.Slice(baseOff + 4, 4), dictRelocIsZero ? 0u : ptr);
            BinaryPrimitives.WriteUInt32BigEndian(s.Slice(baseOff + 8, 4), (uint)size);
            BinaryPrimitives.WriteUInt32BigEndian(s.Slice(baseOff + 12, 4), 0x10);
            BinaryPrimitives.WriteUInt32BigEndian(s.Slice(baseOff + 16, 4), (uint)typeIndex);
            BinaryPrimitives.WriteUInt32BigEndian(s.Slice(baseOff + 20, 4), typeId);
        }
        return blob;
    }

    /// <param name="valueAt0x44">Value at 0x44: mainBase (offset to first BaseResource) for mesh, or file size for collision.</param>
    /// <param name="valueAt0x6C">Value at 0x6C: BaseResource span size for mesh (0 for collision).</param>
    private static void WriteHeader(
        byte[] blob,
        uint arenaId,
        int numObjects,
        int dictStart,
        int valueAt0x44,
        int valueAt0x6C,
        uint headerTypeIdAt0x70 = 1)
    {
        if (blob.Length < HeaderSize)
            throw new ArgumentException("Blob too small for header.", nameof(blob));

        var s = blob.AsSpan();
        byte[] magic = { 0x89, (byte)'R', (byte)'W', (byte)'4', (byte)'p', (byte)'s', (byte)'3', 0x00, 0x0D, 0x0A, 0x1A, 0x0A };
        magic.CopyTo(s);
        s[0x0C] = 0x01;
        s[0x0D] = 0x20;
        s[0x0E] = 0x04;
        s[0x0F] = 0x00;
        "454\x00"u8.CopyTo(s.Slice(0x10, 4));
        "000\x00"u8.CopyTo(s.Slice(0x14, 4));
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x18, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x1C, 4), arenaId);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x20, 4), (uint)numObjects);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x24, 4), (uint)numObjects);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x28, 4), 0x10);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x2C, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x30, 4), (uint)dictStart);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x34, 4), 0xC0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x38, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x3C, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x40, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x44, 4), (uint)valueAt0x44);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x48, 4), 0x10);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x4C, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x50, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x54, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x58, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x5C, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x60, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x64, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x68, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x6C, 4), (uint)valueAt0x6C);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x70, 4), headerTypeIdAt0x70);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x74, 4), 0xC0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x78, 4), 4);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x7C, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x80, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x84, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x88, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x8C, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x90, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x94, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x98, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x9C, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0xA0, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0xA4, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0xA8, 4), 0);
        for (int i = 0xAC; i < HeaderSize; i++)
            s[i] = 0;
    }

    /// <summary>
    /// Writes SubreferenceDictionary. ArenaSectionSubreferences::Fixup writes resolved pointers
    /// with 24-byte stride (ArenaDictEntry size) per subref. Dict must be numSubrefs × 24 bytes
    /// or Fixup will overflow. Content is zero-filled; Fixup overwrites during load.
    /// </summary>
    private static void WriteSubrefDictionary(List<byte> blob, int numSubrefs)
    {
        int dictSize = numSubrefs * DictEntrySize; // 24 bytes per subref (Fixup stride)
        for (int i = 0; i < dictSize; i++)
            blob.Add(0);
    }

    /// <summary>
    /// Per real mesh dumps (F72E84DE): offset 0x10 = dict ptr (higher), 0x14 = records ptr (lower).
    /// Layout in file: records first (lower offset), then dict (higher).
    /// </summary>
    private static void BackfillSubrefSection(byte[] blob, int subrefSectionFileOffset, int numSubrefs, int recordsAbsOffset, int dictAbsOffset)
    {
        var s = blob.AsSpan(subrefSectionFileOffset);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x04, 4), (uint)numSubrefs);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x10, 4), (uint)dictAbsOffset);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x14, 4), (uint)recordsAbsOffset);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x18, 4), (uint)numSubrefs);
    }
}
