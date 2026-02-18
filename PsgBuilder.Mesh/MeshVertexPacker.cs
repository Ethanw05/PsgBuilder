using System.Buffers.Binary;
using System.Numerics;

namespace PsgBuilder.Mesh;

/// <summary>
/// Packs vertex data from float arrays to PSG format.
/// Real mesh layout (stride 28): Position (float3), TEX0 (half2), TEX1 (int16x2), 4-byte reserved gap, Tangent (dec3n).
/// Stride = 28 bytes.
/// </summary>
public static class MeshVertexPacker
{
    public const int Stride = 28;

    /// <summary>
    /// Packs a single vertex. Positions can be scaled (e.g. 256.0 for game units).
    /// </summary>
    public static void PackVertex(
        Span<byte> output,
        in Vector3 position,
        in Vector2 uv0,
        in Vector2 uv1,
        in Vector3 tangent,
        float scale = 1f)
    {
        int off = 0;
        BinaryPrimitives.WriteSingleBigEndian(output.Slice(off, 4), position.X * scale);
        off += 4;
        BinaryPrimitives.WriteSingleBigEndian(output.Slice(off, 4), position.Y * scale);
        off += 4;
        BinaryPrimitives.WriteSingleBigEndian(output.Slice(off, 4), position.Z * scale);
        off += 4;

        // TEX0 format 0x03 = half2.
        BinaryPrimitives.WriteHalfBigEndian(output.Slice(off, 2), (Half)uv0.X);
        off += 2;
        BinaryPrimitives.WriteHalfBigEndian(output.Slice(off, 2), (Half)uv0.Y);
        off += 2;

        // TEX1 format 0x01 in real meshes: signed normalized int16 pair.
        BinaryPrimitives.WriteInt16BigEndian(output.Slice(off, 2), ToSnorm16(uv1.X));
        off += 2;
        BinaryPrimitives.WriteInt16BigEndian(output.Slice(off, 2), ToSnorm16(uv1.Y));
        off += 2;

        // Reserved gap present in real layout (offset 20..23).
        output.Slice(off, 4).Clear();
        off += 4;

        BinaryPrimitives.WriteUInt32BigEndian(output.Slice(off, 4), PackDec3n(tangent));
    }

    /// <summary>
    /// Packs unique vertices. Index buffer references 0..positions.Count-1.
    /// </summary>
    public static byte[] PackVertices(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector2> uvs,
        IReadOnlyList<int> indices,
        float scale = 1f,
        IReadOnlyList<Vector2>? uvs1 = null)
    {
        var tangents = ComputeTangents(positions, normals, uvs, indices);

        var outBuf = new byte[positions.Count * Stride];
        for (int i = 0; i < positions.Count; i++)
        {
            var tex1 = (uvs1 is not null && i < uvs1.Count) ? uvs1[i] : uvs[i];
            PackVertex(
                outBuf.AsSpan(i * Stride, Stride),
                positions[i],
                uvs[i],
                tex1,
                tangents[i],
                scale);
        }
        return outBuf;
    }

    private static short ToSnorm16(float v)
    {
        float clamped = Math.Clamp(v, -1f, 1f);
        return (short)MathF.Round(clamped * 32767f);
    }

    /// <summary>Packs a normal/tangent as dec3n (11+11+10 bits). Matches glbtopsg: round then mask.</summary>
    private static uint PackDec3n(Vector3 n)
    {
        float nx = Math.Clamp(n.X, -1f, 1f);
        float ny = Math.Clamp(n.Y, -1f, 1f);
        float nz = Math.Clamp(n.Z, -1f, 1f);
        int ix = (int)MathF.Round(nx * 1023f) & 0x7FF;
        int iy = (int)MathF.Round(ny * 1023f) & 0x7FF;
        int iz = (int)MathF.Round(nz * 511f) & 0x3FF;
        return (uint)((iz << 22) | (iy << 11) | ix);
    }

    private static Vector3[] ComputeTangents(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector2> uvs,
        IReadOnlyList<int> indices)
    {
        var tangents = new Vector3[positions.Count];
        for (int i = 0; i < indices.Count; i += 3)
        {
            int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            var p0 = positions[i0]; var p1 = positions[i1]; var p2 = positions[i2];
            var uv0 = uvs[i0]; var uv1 = uvs[i1]; var uv2 = uvs[i2];
            var edge1 = p1 - p0;
            var edge2 = p2 - p0;
            var deltaUV1 = uv1 - uv0;
            var deltaUV2 = uv2 - uv0;
            float f = deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y;
            if (Math.Abs(f) > 1e-6f)
            {
                var tangent = (edge1 * deltaUV2.Y - edge2 * deltaUV1.Y) / f;
                tangents[i0] += tangent;
                tangents[i1] += tangent;
                tangents[i2] += tangent;
            }
        }
        for (int i = 0; i < tangents.Length; i++)
        {
            var t = tangents[i];
            var n = normals[i];
            t -= n * Vector3.Dot(n, t);
            if (t.LengthSquared() > 1e-9f)
                tangents[i] = Vector3.Normalize(t);
            else
                tangents[i] = Vector3.UnitX;
        }
        return tangents;
    }
}
