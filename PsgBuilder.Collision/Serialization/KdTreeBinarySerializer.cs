using System.Buffers.Binary;
using System.Numerics;
using PsgBuilder.Collision.IO;
using PsgBuilder.Collision.KdTree;

namespace PsgBuilder.Collision.Serialization;

/// <summary>
/// Serialize KD-tree to binary. Python _serialize_kdtree_binary (lines 3872-3911).
/// Header 48 bytes, then 32 bytes per branch node.
/// </summary>
public static class KdTreeBinarySerializer
{
    private const int HeaderSize = 48;

    public static byte[] Serialize(
        IReadOnlyList<KdTreeNode> kdTreeNodes,
        Vector3 bboxMin,
        Vector3 bboxMax,
        int numEntries)
    {
        int numBranches = kdTreeNodes.Count;
        var buffer = new byte[HeaderSize + numBranches * 32];
        var span = buffer.AsSpan();

        // Header (48 bytes)
        span = WriteBeU32(span, 0x90);  // m_branchNodes offset
        span = WriteBeU32(span, (uint)numBranches);
        span = WriteBeU32(span, (uint)numEntries);
        span = WriteBeU32(span, 0);
        span = WriteBeF32Vec3WithPad(span, bboxMin);
        span = WriteBeF32Vec3WithPad(span, bboxMax);

        for (int i = 0; i < numBranches; i++)
        {
            var node = kdTreeNodes[i];
            span = WriteBeU32(span, (uint)node.Parent);
            span = WriteBeU32(span, node.Axis);
            if (node.Entries.Length > 0)
            {
                span = WriteBeU32(span, node.Entries[0].Content);
                span = WriteBeU32(span, node.Entries[0].Index);
            }
            else
            {
                span = WriteBeU32(span, 0);
                span = WriteBeU32(span, 0);
            }
            if (node.Entries.Length > 1)
            {
                span = WriteBeU32(span, node.Entries[1].Content);
                span = WriteBeU32(span, node.Entries[1].Index);
            }
            else
            {
                span = WriteBeU32(span, 0);
                span = WriteBeU32(span, 0);
            }
            span = WriteBeF32(span, node.Ext0);
            span = WriteBeF32(span, node.Ext1);
        }

        return buffer;
    }

    private static Span<byte> WriteBeU32(Span<byte> s, uint v)
    {
        BinaryPrimitives.WriteUInt32BigEndian(s, v);
        return s[4..];
    }

    private static Span<byte> WriteBeF32(Span<byte> s, float v)
    {
        BinaryPrimitives.WriteInt32BigEndian(s, BitConverter.SingleToInt32Bits(v));
        return s[4..];
    }

    private static Span<byte> WriteBeF32Vec3WithPad(Span<byte> s, Vector3 v)
    {
        WriteBeF32(s, v.X);
        WriteBeF32(s[4..], v.Y);
        WriteBeF32(s[8..], v.Z);
        // +0x0C / +0x1C: padding uint32 = 0 (part of the 16-byte vec3 slot in file layout)
        WriteBeU32(s[12..], 0);
        return s[16..];
    }
}
