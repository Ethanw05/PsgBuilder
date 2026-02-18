using PsgBuilder.Collision.Rw;

namespace PsgBuilder.Collision.Cluster;

/// <summary>
/// Walks the KdTree and builds unit clusters (RenderWare WalkBranch).
/// </summary>
public static class KdTreeClusterWalker
{
    public static int Execute(RwBuildNode buildNode, Dictionary<int, RwBuildNode> leafMap, List<RwUnitCluster> clusterStack,
        IReadOnlyList<(int V0, int V1, int V2)> tris, IReadOnlyList<int> sortedObjects)
    {
        int vcount0 = 0, vcount1 = 0;
        if (buildNode.Left == null)
        {
            uint start = buildNode.MFirstEntry;
            uint totalNumUnitsToAdd = buildNode.MNumEntries;
            if (totalNumUnitsToAdd == 0) return 0;
            leafMap[sortedObjects[(int)start]] = buildNode;
            var cluster = new RwUnitCluster();
            clusterStack.Add(cluster);
            // RenderWare uses maxVerticesPerUnit=4 (worst-case QUAD), even if mesh is triangles-only.
            int numUnitsAdded = ClusterUnitOps.AddOrderedUnitsToUnitCluster(cluster, sortedObjects, (int)start, (int)totalNumUnitsToAdd, tris, 4);
            vcount0 = cluster.VertexIds.Count;
            if (vcount0 == 0) throw new InvalidOperationException("Cluster with no vertices.");
            return vcount0;
        }
        vcount0 = Execute(buildNode.Left!, leafMap, clusterStack, tris, sortedObjects);
        vcount1 = Execute(buildNode.Right!, leafMap, clusterStack, tris, sortedObjects);
        if (vcount0 > 0 && vcount0 <= ClusterConstants.MaxVertexCount && vcount1 > 0 && vcount1 <= ClusterConstants.MaxVertexCount && clusterStack.Count >= 2)
        {
            var last = clusterStack[clusterStack.Count - 1];
            var penultimate = clusterStack[clusterStack.Count - 2];
            if (last.VertexIds.Count == vcount1 && penultimate.VertexIds.Count == vcount0 && ClusterMerger.MergeLastTwo(clusterStack, tris))
            {
                vcount0 = clusterStack[clusterStack.Count - 1].VertexIds.Count;
                vcount1 = 0;
            }
        }
        return vcount0 + vcount1;
    }
}
