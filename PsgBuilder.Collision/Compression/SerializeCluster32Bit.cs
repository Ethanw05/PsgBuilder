using System.Buffers.Binary;
using System.Numerics;

namespace PsgBuilder.Collision.Compression;

/// <summary>
/// Serialize vertices using 32-bit compression: 12 bytes per vertex (3 x int32).
/// RenderWare: ClusteredMeshCluster::SetVertex for VERTICES_32BIT_COMPRESSED (rwcclusteredmeshcluster.cpp lines 284-298).
/// Ported from Collision_Export_Dumbad_Tuukkas_original.py lines 478-521. Uses truncation, not rounding.
/// </summary>
public static class SerializeCluster32Bit
{
    public static byte[] Serialize(IReadOnlyList<Vector3> verts, float granularity)
    {
        if (verts == null || verts.Count == 0)
            return Array.Empty<byte>();

        var buffer = new byte[verts.Count * 12];
        var span = buffer.AsSpan();
        for (int i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            int xVal = (int)(v.X / granularity);
            int yVal = (int)(v.Y / granularity);
            int zVal = (int)(v.Z / granularity);
            const int int32Min = -2147483648;
            const int int32Max = 2147483647;
            if (xVal < int32Min || xVal > int32Max || yVal < int32Min || yVal > int32Max || zVal < int32Min || zVal > int32Max)
                throw new InvalidOperationException(
                    $"32-bit compression overflow. Values ({xVal},{yVal},{zVal}). Vertex=({v.X},{v.Y},{v.Z}), Granularity={granularity}");
            int baseOff = i * 12;
            BinaryPrimitives.WriteInt32BigEndian(span.Slice(baseOff), xVal);
            BinaryPrimitives.WriteInt32BigEndian(span.Slice(baseOff + 4), yVal);
            BinaryPrimitives.WriteInt32BigEndian(span.Slice(baseOff + 8), zVal);
        }
        return buffer;
    }
}
