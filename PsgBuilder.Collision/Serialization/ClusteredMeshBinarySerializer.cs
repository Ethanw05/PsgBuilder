using System.Buffers.Binary;
using System.Numerics;
using PsgBuilder.Collision.ClusteredMesh;
using PsgBuilder.Collision.IO;
using PsgBuilder.Collision.KdTree;
using PsgBuilder.Collision.Math;
using PsgBuilder.Collision.Rw;

namespace PsgBuilder.Collision.Serialization;

/// <summary>
/// Serialize ClusteredMesh to binary. Python _serialize_clusteredmesh_binary (lines 3593-3687).
/// Header 96 bytes, KD-tree, cluster pointer array, cluster blobs. Skate-3 m_numTagBits formula.
/// </summary>
public static class ClusteredMeshBinarySerializer
{
    private const int HeaderSize = 0x60;

    /// <summary>
    /// Surface IDs: null = use 0 for all; otherwise one per validated triangle index.
    /// </summary>
    public static byte[] Serialize(
        ClusteredMeshPipelineResult result,
        float granularity,
        bool forceUncompressed,
        IReadOnlyList<int>? surfaceIds = null)
    {
        var clusters = result.Clusters;
        var kdTreeNodes = result.KdTreeNodes;
        var bboxMin = result.BboxMin;
        var bboxMax = result.BboxMax;
        var validatedTris = result.ValidatedTriangles;

        int numClusters = clusters.Count;
        int totalTriangles = 0;
        foreach (var c in clusters) totalTriangles += c.UnitIds.Count;

        int mNumClusterTagBits = 1 + (int)System.Math.Log2(System.Math.Max(1, numClusters));
        int maxUnitStreamLength = 0;
        foreach (var c in clusters)
        {
            int len = c.UnitIds.Count * 9;
            if (len > maxUnitStreamLength) maxUnitStreamLength = len;
        }
        int numUnitTagBits = 1 + (int)System.Math.Log2(System.Math.Max(1, maxUnitStreamLength));
        uint mNumTagBits = (uint)(mNumClusterTagBits + numUnitTagBits - 4);

        // Python pre-allocates a 0x60 (96-byte) header (out = bytearray(0x60)).
        // KD-tree starts after this header. If we don't reserve it, offsets are wrong and data is corrupted.
        var header = new byte[HeaderSize];
        var hs = header.AsSpan();
        // PROCEDURAL BASE (48 bytes: +0x00-0x2F)
        BinaryPrimitives.WriteInt32BigEndian(hs.Slice(0x00, 4), BitConverter.SingleToInt32Bits(bboxMin.X));
        BinaryPrimitives.WriteInt32BigEndian(hs.Slice(0x04, 4), BitConverter.SingleToInt32Bits(bboxMin.Y));
        BinaryPrimitives.WriteInt32BigEndian(hs.Slice(0x08, 4), BitConverter.SingleToInt32Bits(bboxMin.Z));
        BinaryPrimitives.WriteUInt32BigEndian(hs.Slice(0x0C, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(hs.Slice(0x10, 4), BitConverter.SingleToInt32Bits(bboxMax.X));
        BinaryPrimitives.WriteInt32BigEndian(hs.Slice(0x14, 4), BitConverter.SingleToInt32Bits(bboxMax.Y));
        BinaryPrimitives.WriteInt32BigEndian(hs.Slice(0x18, 4), BitConverter.SingleToInt32Bits(bboxMax.Z));
        BinaryPrimitives.WriteUInt32BigEndian(hs.Slice(0x1C, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(hs.Slice(0x20, 4), 0);              // m_vTable
        BinaryPrimitives.WriteUInt32BigEndian(hs.Slice(0x24, 4), mNumTagBits);     // m_numTagBits
        BinaryPrimitives.WriteUInt32BigEndian(hs.Slice(0x28, 4), (uint)totalTriangles);
        BinaryPrimitives.WriteUInt32BigEndian(hs.Slice(0x2C, 4), 0);              // m_flags

        var outList = new List<byte>(HeaderSize + 1024);
        outList.AddRange(header);

        int kdOff = (int)AlignmentHelpers.AlignQw(outList.Count);
        if (kdOff != HeaderSize)
            throw new InvalidOperationException($"ClusteredMesh KD-tree offset mismatch: expected 0x{HeaderSize:X}, got 0x{kdOff:X}.");
        while (outList.Count < kdOff) outList.Add(0);
        var kdBlob = KdTreeBinarySerializer.Serialize(kdTreeNodes, bboxMin, bboxMax, totalTriangles);
        outList.AddRange(kdBlob);

        int clPtrOff = (int)AlignmentHelpers.AlignQw(outList.Count);
        while (outList.Count < clPtrOff) outList.Add(0);
        for (int i = 0; i < numClusters; i++)
        {
            WriteBeU32(outList, 0);
        }

        int blobsStart = (int)AlignmentHelpers.AlignQw(outList.Count);
        while (outList.Count < blobsStart) outList.Add(0);

        var clusterPtrs = new List<int>();
        for (int i = 0; i < clusters.Count; i++)
        {
            int cOff = outList.Count;
            clusterPtrs.Add(cOff);
            var clusterBlob = ClusterBinarySerializer.Serialize(clusters[i], granularity, validatedTris, forceUncompressed, surfaceIds);
            outList.AddRange(clusterBlob);
        }

        byte[] outArr = outList.ToArray();
        for (int i = 0; i < clusterPtrs.Count; i++)
        {
            int ptr = clusterPtrs[i];
            BinaryPrimitives.WriteUInt32BigEndian(outArr.AsSpan(clPtrOff + i * 4, 4), (uint)ptr);
        }

        // CLUSTEREDMESH FIELDS (48 bytes: +0x30-0x5F)
        BinaryPrimitives.WriteUInt32BigEndian(outArr.AsSpan(0x30, 4), (uint)kdOff);
        BinaryPrimitives.WriteUInt32BigEndian(outArr.AsSpan(0x34, 4), (uint)clPtrOff);

        uint granularityBits = BitConverter.SingleToUInt32Bits(granularity);
        ulong clusterParamsU64 = ((ulong)granularityBits << 32) | (0x0010u << 16) | (0x00u << 8) | 0x02u;
        BinaryPrimitives.WriteUInt64BigEndian(outArr.AsSpan(0x38, 8), clusterParamsU64);
        BinaryPrimitives.WriteUInt32BigEndian(outArr.AsSpan(0x40, 4), (uint)numClusters);
        BinaryPrimitives.WriteUInt32BigEndian(outArr.AsSpan(0x44, 4), (uint)numClusters);
        BinaryPrimitives.WriteUInt32BigEndian(outArr.AsSpan(0x48, 4), (uint)totalTriangles);
        BinaryPrimitives.WriteUInt32BigEndian(outArr.AsSpan(0x4C, 4), (uint)totalTriangles);
        BinaryPrimitives.WriteUInt32BigEndian(outArr.AsSpan(0x50, 4), (uint)outArr.Length);
        BinaryPrimitives.WriteUInt16BigEndian(outArr.AsSpan(0x54, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(outArr.AsSpan(0x56, 2), 0);
        outArr[0x58] = 128;
        for (int i = 0x59; i < 0x60; i++) outArr[i] = 0;

        return outArr;
    }

    private static void WriteBeU32(List<byte> list, uint v)
    {
        var s = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(s, v);
        list.AddRange(s);
    }
}
