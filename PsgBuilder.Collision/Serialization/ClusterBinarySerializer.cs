using System.Buffers.Binary;
using System.Numerics;
using PsgBuilder.Collision.Cluster;
using PsgBuilder.Collision.Compression;
using PsgBuilder.Collision.IO;
using PsgBuilder.Collision.Math;
using PsgBuilder.Collision.Rw;

namespace PsgBuilder.Collision.Serialization;

/// <summary>
/// Serialize single cluster to binary. Python _serialize_cluster_binary (lines 3702-3870).
/// Cluster header 16 bytes, vertex payload, unit stream (9 bytes per unit: flag + 3 verts + 3 edges + 2 surfaceID LE).
/// </summary>
public static class ClusterBinarySerializer
{
    private const int ClusterHeaderSize = 16;
    private const byte UnitFlags = 0xA1; // TRIANGLE(1) + EDGEANGLE(0x20) + SURFACEID(0x80)
    private const int UnitSize = 9;

    /// <summary>
    /// Serialize one cluster. Surface IDs: pass null to use 0 for all triangles; otherwise one per validated triangle index (unitId).
    /// </summary>
    public static byte[] Serialize(
        RwUnitCluster cluster,
        float granularity,
        IReadOnlyList<(int V0, int V1, int V2)> validatedTris,
        bool forceUncompressed,
        IReadOnlyList<int>? surfaceIds = null)
    {
        // Python/RenderWare invariant: vertexIDs must be sorted+compressed before GetVertexCode.
        // If this invariant is violated, unit stream can contain invalid vertex indices -> "stringy" meshes.
        // We enforce it here and rebuild Vertices/VertexMap to stay consistent with any dedup/sort changes.
        if (cluster.Vertices.Count != cluster.VertexIds.Count)
            throw new InvalidOperationException($"Cluster Vertices/VertexIds mismatch: verts={cluster.Vertices.Count} ids={cluster.VertexIds.Count}.");
        var posByGlobalId = new Dictionary<int, Vector3>(cluster.VertexIds.Count);
        for (int i = 0; i < cluster.VertexIds.Count; i++)
        {
            int gid = cluster.VertexIds[i];
            // In a well-formed cluster this will be unique already; if duplicates exist, last wins (positions should be identical).
            posByGlobalId[gid] = cluster.Vertices[i];
        }
        ClusterVertexSet.SortAndCompress(cluster);
        cluster.Vertices.Clear();
        foreach (int gid in cluster.VertexIds)
        {
            if (!posByGlobalId.TryGetValue(gid, out var p))
                throw new InvalidOperationException($"Cluster missing vertex position for global vertex id {gid}.");
            cluster.Vertices.Add(p);
        }
        cluster.VertexMap.Clear();
        for (int i = 0; i < cluster.VertexIds.Count; i++)
            cluster.VertexMap[cluster.VertexIds[i]] = i;

        int unitCount = cluster.UnitIds.Count;
        int unitDataSize = unitCount * UnitSize;

        byte compressionMode;
        (int X, int Y, int Z) offset;
        byte[] payload;

        if (forceUncompressed)
        {
            compressionMode = CompressionConstants.VerticesUncompressed;
            offset = (0, 0, 0);
            payload = SerializeClusterUncompressed.Serialize(cluster.Vertices);
        }
        else
        {
            (compressionMode, offset) = DetermineCompressionMode.Execute(cluster.Vertices, granularity);
            cluster.CompressionMode = compressionMode;
            cluster.ClusterOffset = offset;
            try
            {
                payload = compressionMode switch
                {
                    CompressionConstants.VerticesUncompressed => SerializeClusterUncompressed.Serialize(cluster.Vertices),
                    CompressionConstants.Vertices16BitCompressed => SerializeCluster16Bit.Serialize(cluster.Vertices, granularity, offset),
                    _ => SerializeCluster32Bit.Serialize(cluster.Vertices, granularity)
                };
            }
            catch (InvalidOperationException ex)
            {
                // Match Python fallback behavior in _serialize_cluster_binary (lines 3746-3769):
                // - 16-bit overflow -> fall back to 32-bit; if 32-bit also overflows -> uncompressed
                // - 32-bit overflow -> fall back to uncompressed
                // - otherwise rethrow
                string msg = ex.Message ?? string.Empty;
                if (msg.Contains("16-bit compression overflow", StringComparison.OrdinalIgnoreCase))
                {
                    compressionMode = CompressionConstants.Vertices32BitCompressed;
                    cluster.CompressionMode = compressionMode;
                    cluster.ClusterOffset = (0, 0, 0);
                    try
                    {
                        payload = SerializeCluster32Bit.Serialize(cluster.Vertices, granularity);
                    }
                    catch (InvalidOperationException ex2)
                    {
                        string msg2 = ex2.Message ?? string.Empty;
                        if (msg2.Contains("32-bit compression overflow", StringComparison.OrdinalIgnoreCase))
                        {
                            compressionMode = CompressionConstants.VerticesUncompressed;
                            cluster.CompressionMode = compressionMode;
                            cluster.ClusterOffset = (0, 0, 0);
                            payload = SerializeClusterUncompressed.Serialize(cluster.Vertices);
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
                else if (msg.Contains("32-bit compression overflow", StringComparison.OrdinalIgnoreCase))
                {
                    compressionMode = CompressionConstants.VerticesUncompressed;
                    cluster.CompressionMode = compressionMode;
                    cluster.ClusterOffset = (0, 0, 0);
                    payload = SerializeClusterUncompressed.Serialize(cluster.Vertices);
                }
                else
                {
                    throw;
                }
            }
        }

        int vertexSectionEnd = ClusterHeaderSize + payload.Length;
        long vertexSectionEndAligned = AlignmentHelpers.AlignQw(vertexSectionEnd);

        ushort normalStartValue;
        ushort unitDataStartValue;
        if (compressionMode == CompressionConstants.VerticesUncompressed)
        {
            normalStartValue = (ushort)cluster.NumVertices;
            unitDataStartValue = (ushort)cluster.NumVertices;
        }
        else
        {
            normalStartValue = (ushort)((vertexSectionEndAligned - ClusterHeaderSize) / 16);
            unitDataStartValue = normalStartValue;
        }

        var outList = new List<byte>(ClusterHeaderSize + (int)vertexSectionEndAligned + unitCount * UnitSize + 16);

        WriteBeU16(outList, (ushort)unitCount);
        WriteBeU16(outList, (ushort)unitDataSize);
        WriteBeU16(outList, unitDataStartValue);
        WriteBeU16(outList, normalStartValue);
        int totalSizePlaceholder = outList.Count;
        WriteBeU16(outList, 0);
        outList.Add((byte)cluster.NumVertices);
        outList.Add(0);
        outList.Add(compressionMode);
        outList.Add(0);
        outList.Add(0);
        outList.Add(0);

        outList.AddRange(payload);
        // NOTE: vertexSectionEndAligned is an absolute offset from start of cluster (includes the 16-byte header).
        // Padding to (header + alignedOffset) would over-pad by 16 bytes and corrupt unit stream offsets.
        while (outList.Count < vertexSectionEndAligned)
            outList.Add(0);

        for (int i = 0; i < cluster.UnitIds.Count; i++)
        {
            int unitId = cluster.UnitIds[i];
            var tri = validatedTris[unitId];
            if (!cluster.VertexMap.TryGetValue(tri.V0, out int v0Local) ||
                !cluster.VertexMap.TryGetValue(tri.V1, out int v1Local) ||
                !cluster.VertexMap.TryGetValue(tri.V2, out int v2Local))
            {
                throw new InvalidOperationException($"Cluster vertex not found for unitId={unitId}. tri=({tri.V0},{tri.V1},{tri.V2}) clusterVerts={cluster.VertexIds.Count}.");
            }
            // RenderWare local vertex indices are uint8 [0..254]. 0xFF reserved.
            if ((uint)v0Local >= cluster.VertexIds.Count || (uint)v1Local >= cluster.VertexIds.Count || (uint)v2Local >= cluster.VertexIds.Count)
                throw new InvalidOperationException($"Cluster produced invalid local vertex index for unitId={unitId}: ({v0Local},{v1Local},{v2Local}) numVerts={cluster.VertexIds.Count}.");
            if (v0Local > 254 || v1Local > 254 || v2Local > 254)
                throw new InvalidOperationException($"Cluster local vertex index exceeds 254 (0xFF reserved). unitId={unitId} v=({v0Local},{v1Local},{v2Local}) numVerts={cluster.VertexIds.Count}.");

            outList.Add(UnitFlags);
            outList.Add((byte)v0Local);
            outList.Add((byte)v1Local);
            outList.Add((byte)v2Local);
            var ec = cluster.EdgeCodes.TryGetValue(unitId, out var e) ? e : (0x1A | 0x80, 0x1A | 0x80, 0x1A | 0x80);
            outList.Add((byte)ec.Item1);
            outList.Add((byte)ec.Item2);
            outList.Add((byte)ec.Item3);
            int surfaceId = (surfaceIds != null && unitId < surfaceIds.Count) ? surfaceIds[unitId] : 0;
            WriteLeU16(outList, (ushort)(surfaceId & 0xFFFF));
        }

        int actualUnitBytes = outList.Count - (int)vertexSectionEndAligned;
        int pad = (int)AlignmentHelpers.AlignQw(actualUnitBytes) - actualUnitBytes;
        for (int i = 0; i < pad; i++) outList.Add(0);

        int finalTotalSizeBytes = outList.Count - ClusterHeaderSize;
        ushort totalSizeValue = (ushort)(finalTotalSizeBytes > 65535 ? finalTotalSizeBytes & 0xFFFF : finalTotalSizeBytes);
        var outArr = outList.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(outArr.AsSpan(totalSizePlaceholder, 2), totalSizeValue);
        return outArr;
    }

    private static void WriteBeU16(List<byte> list, ushort v)
    {
        var s = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(s, v);
        list.AddRange(s);
    }

    private static void WriteLeU16(List<byte> list, ushort v)
    {
        var s = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(s, v);
        list.AddRange(s);
    }
}
