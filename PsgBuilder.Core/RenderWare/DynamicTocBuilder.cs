using System.Buffers.Binary;
using System.Linq;
using PsgBuilder.Core.Psg;

namespace PsgBuilder.Core.Rw;

/// <summary>
/// Builds raw TableOfContents RW bytes from a data-driven <see cref="PsgTocSpec"/>.
/// Handles header, entry array, optional type map, and optional names.
/// </summary>
public static class DynamicTocBuilder
{
    private const int TocHeaderSize = 0x14;
    private const int TocEntrySize = 0x18;
    private const int TypeMapEntrySize = 8;

    /// <summary>
    /// Builds the TOC binary. Big-endian.
    /// </summary>
    /// <param name="spec">TOC spec (entries + optional type map).</param>
    /// <returns>Raw TOC bytes (header + entries + type map; names not yet implemented).</returns>
    public static byte[] Build(PsgTocSpec spec)
    {
        if (spec.Entries is null || spec.Entries.Count == 0)
            throw new ArgumentException("TOC must have at least one entry.", nameof(spec));

        int numItems = spec.Entries.Count;

        // Type map: null = derive from entries; empty = texture (0 types); otherwise use spec
        (uint TypeId, uint StartIndex)[] typeMap;
        if (spec.TypeMap != null)
        {
            typeMap = spec.TypeMap;
        }
        else
        {
            typeMap = DeriveTypeMap(spec.Entries, numItems);
        }

        int typeCount = typeMap.Length;
        uint entriesOffset = TocHeaderSize;
        uint typeMapOffset = typeCount > 0
            ? entriesOffset + (uint)(numItems * TocEntrySize)
            : 0u;
        // Per PSG Type DUMP: m_pNames = offset. m_Name in entry = offset from names offset.
        // Real mesh: m_pNames = m_pTypeMap (0x74); m_Name=0 → first byte of names = type map start = 0x00 (valid).
        uint namesOffset = typeMapOffset;

        var buf = new List<byte>();

        // Header (0x14 bytes)
        buf.AddRange(BeU32((uint)numItems));
        buf.AddRange(BeU32(entriesOffset));
        buf.AddRange(BeU32(namesOffset));
        buf.AddRange(BeU32((uint)typeCount));
        buf.AddRange(BeU32(typeMapOffset));

        // Entries (24 bytes each). m_Name = offset from names offset (doc).
        foreach (var e in spec.Entries)
        {
            buf.AddRange(BeU32(e.NameOrHash));
            buf.AddRange(BeU32(0xFEFFFFFF)); // Guid high sentinel
            buf.AddRange(BeU32((uint)(e.Guid >> 32)));
            buf.AddRange(BeU32((uint)e.Guid));
            buf.AddRange(BeU32(e.TypeId));
            buf.AddRange(BeU32(e.ObjectPtr));
        }

        // Type map (8 bytes per type)
        foreach (var (typeId, startIndex) in typeMap)
        {
            buf.AddRange(BeU32(typeId));
            buf.AddRange(BeU32(startIndex));
        }

        return buf.ToArray();
    }

    /// <summary>
    /// Derives type map from entries: distinct types in order of first appearance, with start index.
    /// </summary>
    private static (uint TypeId, uint StartIndex)[] DeriveTypeMap(IReadOnlyList<PsgTocEntry> entries, int numItems)
    {
        var seen = new Dictionary<uint, uint>();
        for (int i = 0; i < entries.Count; i++)
        {
            uint typeId = entries[i].TypeId;
            if (!seen.ContainsKey(typeId))
                seen[typeId] = (uint)i;
        }
        // Order by first occurrence index
        return seen.OrderBy(kv => kv.Value).Select(kv => (kv.Key, kv.Value)).ToArray();
    }

    private static byte[] BeU32(uint v)
    {
        var s = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(s, v);
        return s;
    }
}
