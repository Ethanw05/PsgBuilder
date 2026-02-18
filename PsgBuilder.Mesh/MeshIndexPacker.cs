using System.Buffers.Binary;

namespace PsgBuilder.Mesh;

/// <summary>
/// Packs triangle indices to uint16 big-endian. Per glbtopsg: make_face_bin.
/// reverseWinding: when true, each triangle (i0,i1,i2) is written as (i0,i2,i1) to flip front face (fixes invisible mesh if game culls back faces and our winding is opposite).
/// </summary>
public static class MeshIndexPacker
{
    public static byte[] PackIndices(IReadOnlyList<int> indices, bool reverseWinding = false)
    {
        var buf = new byte[indices.Count * 2];
        if (!reverseWinding)
        {
            for (int i = 0; i < indices.Count; i++)
                BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(i * 2, 2), (ushort)indices[i]);
        }
        else
        {
            for (int i = 0; i < indices.Count; i += 3)
            {
                int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan((i + 0) * 2, 2), (ushort)i0);
                BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan((i + 1) * 2, 2), (ushort)i2);
                BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan((i + 2) * 2, 2), (ushort)i1);
            }
        }
        return buf;
    }
}
