using PsgBuilder.Collision.Rw;

namespace PsgBuilder.Collision.Cluster;

/// <summary>
/// Update KD-tree leaf nodes with final byte offsets (cluster ID + unit offset). AdjustKDTreeNodeEntriesForCluster (rwcclusteredmeshbuildermethods.cpp lines 1877-1946).
/// Ported from Collision_Export_Dumbad_Tuukkas_original.py lines 2058-2160.
/// </summary>
public static class KdTreeAdjustForCluster
{
    private const int UnitSizeBytes = 9;

    /// <summary>
    /// Update KD-tree leaf BuildNodes with (clusterId &lt;&lt; unitClusterIdShift) + unitByteOffset.
    /// RenderWare computes unitClusterIdShift as: (unitClusterCount &gt; 65536) ? 20 : 16.
    /// </summary>
    public static int Execute(RwUnitCluster cluster, int clusterId, int unitClusterIdShift, Dictionary<int, RwBuildNode> leafMap)
    {
        int numUnits = cluster.UnitIds.Count;
        uint shiftedClusterId = (uint)(clusterId << unitClusterIdShift);
        int sizeofUnitData = 0;
        for (int unitIndex = 0; unitIndex < numUnits; unitIndex++)
        {
            int unitId = cluster.UnitIds[unitIndex];
            if (leafMap.TryGetValue(unitId, out var buildNode))
            {
                if (buildNode.Left != null) throw new InvalidOperationException("BuildNode should be a leaf.");
                uint reformattedStartIndex = shiftedClusterId + (uint)sizeofUnitData;
                buildNode.MFirstEntry = reformattedStartIndex;
                if (buildNode.Parent != null)
                {
                    var parent = buildNode.Parent;
                    if (parent.Right == buildNode && parent.Left!.MNumEntries == 0)
                        parent.Left.MFirstEntry = reformattedStartIndex;
                    else if (parent.Left == buildNode && parent.Right!.MNumEntries == 0)
                        parent.Right.MFirstEntry = reformattedStartIndex;
                }
            }
            sizeofUnitData += UnitSizeBytes;
        }
        return sizeofUnitData;
    }
}
