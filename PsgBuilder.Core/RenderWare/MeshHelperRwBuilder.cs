using System.Buffers.Binary;

namespace PsgBuilder.Core.Rw;

/// <summary>
/// MeshHelper RW object (0x00020081). Per psg_structure_dumper: +0x00 numIndexBuffers, +0x04 numVertexBuffers, +0x08 buffer pointers.
/// Buffer pointers are encoded: dict index for direct ref, or 0x00800000|subrefIndex for subref.
/// For simple mesh: 1 index buffer, 1 vertex buffer; pointers are dict indices.
/// </summary>
public static class MeshHelperRwBuilder
{
    /// <summary>
    /// Builds MeshHelper. indexBufferDictIndex and vertexBufferDictIndex are dictionary indices.
    /// </summary>
    public static byte[] Build(uint indexBufferDictIndex, uint vertexBufferDictIndex)
    {
        var buf = new List<byte>();
        buf.AddRange(BeU32(1)); // numIndexBuffers
        buf.AddRange(BeU32(1)); // numVertexBuffers
        buf.AddRange(BeU32(indexBufferDictIndex));
        buf.AddRange(BeU32(vertexBufferDictIndex));
        return buf.ToArray();
    }

    private static byte[] BeU32(uint v)
    {
        var s = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(s, v);
        return s;
    }
}
