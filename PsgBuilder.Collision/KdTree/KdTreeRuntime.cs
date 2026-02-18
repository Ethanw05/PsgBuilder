using System.Numerics;
using PsgBuilder.Collision.Math;
using PsgBuilder.Collision.Rw;

namespace PsgBuilder.Collision.KdTree;

/// <summary>
/// Runtime KD-tree node. Matches RenderWare BranchNode / NodeRef. rwckdtreebuilder.cpp lines 1309-1387.
/// Ported from Collision_Export_Dumbad_Tuukkas_original.py KDNode (lines 686-695) and RW_InitializeRuntimeKDTree (2162-2316).
/// </summary>
public sealed class KdTreeNode
{
    public int Parent { get; set; }
    public uint Axis { get; set; }
    public float Ext0 { get; set; }
    public float Ext1 { get; set; }
    /// <summary>Child refs: (content, index). content=0xFFFFFFFF for branch, else numEntries; index=child node index or firstEntry.</summary>
    public (uint Content, uint Index)[] Entries { get; set; } = new (uint, uint)[2];

    public const uint BranchNode = 0xFFFFFFFF;
    public const uint InvalidIndex = 0xFFFFFFFF;
}

/// <summary>
/// Convert BuildNode tree to runtime KDNode array. RW_InitializeRuntimeKDTree (rwckdtreebuilder.cpp lines 1309-1387).
/// </summary>
public static class KdTreeRuntime
{
    private static int CountBranches(RwBuildNode? node)
    {
        if (node == null || node.Left == null) return 0;
        return 1 + CountBranches(node.Left) + CountBranches(node.Right);
    }

    private static int CountAllNodes(RwBuildNode? node)
    {
        if (node == null) return 0;
        if (node.Left == null) return 1;
        return 1 + CountAllNodes(node.Left) + CountAllNodes(node.Right);
    }

    public static IReadOnlyList<KdTreeNode> InitializeRuntimeKdTree(RwBuildNode? root)
    {
        if (root == null) return Array.Empty<KdTreeNode>();
        int numBranches = CountBranches(root);
        int totalNodes = CountAllNodes(root);
        if (1 + 2 * numBranches != totalNodes)
            throw new InvalidOperationException($"Invalid tree structure: 1 + 2*{numBranches} != {totalNodes}");
        if (numBranches == 0) return Array.Empty<KdTreeNode>();

        var rtNodes = new KdTreeNode[numBranches];
        for (int i = 0; i < rtNodes.Length; i++)
            rtNodes[i] = new KdTreeNode();
        var stack = new List<(int RtParent, int RtChild, RwBuildNode Node)>();
        stack.Add((0, 0, root));
        int top = 1;
        int rtIndex = 0;

        while (top > 0)
        {
            top--;
            var cur = stack[top];
            if (rtIndex != 0)
            {
                var parentNode = rtNodes[cur.RtParent];
                parentNode.Entries[cur.RtChild] = (KdTreeNode.BranchNode, (uint)rtIndex);
            }

            var rtNode = rtNodes[rtIndex];
            var childNodes = new[] { cur.Node.Left!, cur.Node.Right! };
            rtNode.Parent = cur.RtParent;
            rtNode.Axis = cur.Node.MSplitAxis;
            rtNode.Ext0 = Vector3Extensions.GetComponent(childNodes[0].Bbox.Max, (int)rtNode.Axis);
            rtNode.Ext1 = Vector3Extensions.GetComponent(childNodes[1].Bbox.Min, (int)rtNode.Axis);
            rtNode.Entries = new (uint, uint)[2];

            for (int i = 1; i >= 0; i--)
            {
                var child = childNodes[i];
                if (child.Left == null)
                    rtNode.Entries[i] = (child.MNumEntries, child.MFirstEntry);
                else
                {
                    while (stack.Count <= top) stack.Add(default);
                    stack[top] = (rtIndex, i, child);
                    top++;
                    rtNode.Entries[i] = (KdTreeNode.BranchNode, KdTreeNode.InvalidIndex);
                }
            }
            rtIndex++;
        }

        if (rtIndex != numBranches)
            throw new InvalidOperationException($"Invalid number of nodes: expected {numBranches}, got {rtIndex}");
        return rtNodes;
    }
}
