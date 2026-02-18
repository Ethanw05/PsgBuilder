using System.Buffers.Binary;
using System.Text;

namespace PsgBuilder.Texture.RenderWare;

/// <summary>
/// Builds the TableOfContents object for a single-texture PSG.
/// Layout: header (0x14), one entry (0x18) with marker 0x9B0F1678, then names blob; total 72 bytes.
/// </summary>
public static class TextureTocBuilder
{
    private const int TocHeaderSize = 0x14;
    private const int TocEntrySize = 0x18;
    private const int TocTotalSize = 72;

    /// <summary>
    /// Builds the 72-byte TOC for one texture. Name string is "0x&lt;guidHex&gt;.Texture\0".
    /// </summary>
    /// <param name="textureGuid">TOC entry m_uiGuid (cross-file identifier).</param>
    public static byte[] Build(ulong textureGuid)
    {
        string nameString = TextureGuidStrategy.GuidToTocNameString(textureGuid);
        byte[] nameBytes = Encoding.ASCII.GetBytes(nameString + "\0");
        uint nameOffset = TocHeaderSize + TocEntrySize; // names start after the single entry

        var buf = new byte[TocTotalSize];
        var s = buf.AsSpan();

        // Header
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0, 4), 1);           // m_uiItemsCount
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(4, 4), TocHeaderSize); // m_pArray
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(8, 4), (uint)nameOffset); // m_pNames
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(12, 4), 0);           // m_uiTypeCount
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(16, 4), 0x48);       // m_pTypeMap (past names)

        // Entry: m_Name (offset to name), marker, guid, type, m_pObject
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(20, 4), (uint)nameOffset);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(24, 4), TexturePsgConstants.TocEntryMarker);
        BinaryPrimitives.WriteUInt64BigEndian(s.Slice(28, 8), textureGuid);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(36, 4), TexturePsgConstants.TocEntryTypeTexture);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(40, 4), TexturePsgConstants.TocEntryObjectPointer);

        // Names (fit within remaining space)
        int nameStart = (int)nameOffset;
        int nameLen = Math.Min(nameBytes.Length, TocTotalSize - nameStart);
        nameBytes.AsSpan(0, nameLen).CopyTo(s.Slice(nameStart, nameLen));

        return buf;
    }
}
