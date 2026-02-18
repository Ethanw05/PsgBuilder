using System.Buffers.Binary;
using PsgBuilder.Core.Psg;
using PsgBuilder.Texture.Dds;
using PsgBuilder.Texture.RenderWare;

namespace PsgBuilder.Texture;

/// <summary>
/// Composes PsgArenaSpec for a single-texture PS3 PSG from DDS input and texture GUID.
/// Object order: BaseResource, Texture, TableOfContents, VersionData (dict order; file layout uses DeferBaseResourceLayout).
/// </summary>
public static class TexturePsgComposer
{
    private const int VersionDataSizeTexture = 8;

    /// <summary>
    /// Composes a full texture PsgArenaSpec. One texture per PSG.
    /// </summary>
    /// <param name="ddsInput">Parsed DDS (payload at offset 128).</param>
    /// <param name="textureGuid">TOC m_uiGuid and name; must match mesh material channel GUID.</param>
    public static PsgArenaSpec Compose(DdsTextureInput ddsInput, ulong textureGuid)
    {
        if (ddsInput == null)
            throw new ArgumentNullException(nameof(ddsInput));

        byte[] baseResourceData = ddsInput.Payload;
        byte[] textureObject = TextureRwBuilder.Build(ddsInput);
        byte[] tocObject = TextureTocBuilder.Build(textureGuid);
        byte[] versionData = BuildVersionData();

        var objects = new List<PsgObjectSpec>
        {
            new(baseResourceData, TexturePsgConstants.TypeIdBaseResource),
            new(textureObject, TexturePsgConstants.TypeIdTexture),
            new(tocObject, TexturePsgConstants.TypeIdTableOfContents),
            new(versionData, TexturePsgConstants.TypeIdVersionData)
        };

        uint arenaId = ComputeArenaId((uint)baseResourceData.Length, textureGuid);

        var tocSpec = new PsgTocSpec
        {
            Entries = new List<PsgTocEntry> { new PsgTocEntry((uint)(0x14 + 0x18), textureGuid, TexturePsgConstants.TocEntryTypeTexture, TexturePsgConstants.TocEntryObjectPointer) },
            TypeMap = Array.Empty<(uint, uint)>()
        };

        return new PsgArenaSpec
        {
            ArenaId = arenaId,
            Objects = objects,
            TypeRegistry = TexturePsgConstants.TextureTypeRegistry,
            Toc = tocSpec,
            Subrefs = null,
            HeaderTypeIdAt0x70 = 0x80,
            UseFileSizeAt0x44 = false,
            DictRelocIsZero = true,
            DeferBaseResourceLayout = true
        };
    }

    private static byte[] BuildVersionData()
    {
        var buf = new byte[VersionDataSizeTexture];
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(0, 4), TexturePsgConstants.VersionDataVersion);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4, 4), TexturePsgConstants.VersionDataRevision);
        return buf;
    }

    private static uint ComputeArenaId(uint payloadSize, ulong guid)
    {
        uint lo = (uint)(guid & 0xFFFFFFFF);
        uint hi = (uint)(guid >> 32);
        return (payloadSize ^ lo) + hi;
    }
}
