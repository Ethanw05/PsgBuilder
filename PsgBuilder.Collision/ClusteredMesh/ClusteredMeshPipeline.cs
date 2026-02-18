using System.Numerics;
using PsgBuilder.Collision.Cluster;
using PsgBuilder.Collision.EdgeCodes;
using PsgBuilder.Collision.KdTree;
using PsgBuilder.Collision.Rw;
using PsgBuilder.Collision.Validation;

namespace PsgBuilder.Collision.ClusteredMesh;

/// <summary>
/// Complete RenderWare ClusteredMesh build pipeline. Order and orchestration match RW_BuildClusteredMeshComplete exactly.
/// Python: Collision_Export_Dumbad_Tuukkas_original.py lines 2393-2586.
/// RenderWare: rwcclusteredmeshbuilder.cpp Build() — steps 1–6 in order.
/// </summary>
public static class ClusteredMeshPipeline
{
    /// <summary>
    /// Build clustered mesh: validate → neighbors → edge codes → (optional) smooth → KD-tree → walk → fill clusters → adjust → runtime KD-tree.
    /// </summary>
    /// <param name="verts">Vertex list.</param>
    /// <param name="tris">Triangle list (will be validated; degenerate removed).</param>
    /// <param name="granularity">Vertex compression granularity (e.g. from DetermineOptimalGranularity).</param>
    /// <param name="enableVertexSmoothing">If true, run SmoothVertices (EDGE_VERTEX_DISABLE on non-feature vertices).</param>
    public static ClusteredMeshPipelineResult BuildComplete(
        IReadOnlyList<Vector3> verts,
        IReadOnlyList<(int V0, int V1, int V2)> tris,
        float granularity = 0.001f,
        bool enableVertexSmoothing = false)
    {
        // STEP 0: Validate and filter degenerate triangles (RenderWare lines 235-245)
        var validatedTris = TriangleValidation.ValidateTriangles(verts, tris);
        int numTris = validatedTris.Count;

        // STEP 1: Find triangle neighbors (RenderWare lines 282-288)
        var triangleNeighbors = new int?[numTris][];
        var triangleEdgeCosines = new float[numTris][];
        for (int i = 0; i < numTris; i++)
        {
            triangleNeighbors[i] = new int?[3];
            triangleEdgeCosines[i] = new[] { 1.0f, 1.0f, 1.0f };
        }
        var vertexTriMap = TriangleNeighborFinder.BuildVertexTriangleMap(validatedTris);
        TriangleNeighborFinder.FindTriangleNeighbors(verts, validatedTris, triangleNeighbors, triangleEdgeCosines);

        // STEP 2: Generate triangle edge codes (RenderWare lines 319-323)
        var edgeCodesGlobal = EdgeCodeGenerator.GenerateEdgeCodes(numTris, triangleNeighbors, triangleEdgeCosines);
        // Python bounds check per tri: if vert indices out of range use fallback. We use validated tris so indices are valid.

        // STEP 3: Smooth non-feature vertices (RenderWare lines 327-336) — modifies edgeCodesGlobal in place
        if (enableVertexSmoothing)
            VertexSmoothing.Apply(verts, validatedTris, vertexTriMap, edgeCodesGlobal);

        // STEP 4: Build KD-tree (RenderWare lines 369-376) — produces sortedEntryIndices (MASTER ORDERING)
        var (rootBuildNode, sortedEntryIndices) = KdTreeBuilder.BuildKdTree(verts, validatedTris);
        if (rootBuildNode == null)
        {
            return new ClusteredMeshPipelineResult
            {
                Clusters = Array.Empty<RwUnitCluster>(),
                KdTreeNodes = Array.Empty<KdTreeNode>(),
                BboxMin = Vector3.Zero,
                BboxMax = Vector3.Zero,
                ValidatedTriangles = validatedTris
            };
        }

        // STEP 5: WalkBranch — create clusters (RenderWare lines 944-952)
        var leafMap = new Dictionary<int, RwBuildNode>();
        var clusterStack = new List<RwUnitCluster>();
        KdTreeClusterWalker.Execute(rootBuildNode, leafMap, clusterStack, validatedTris, sortedEntryIndices);

        // Fill vertex positions into each cluster (Python lines 2554-2558)
        foreach (var cluster in clusterStack)
        {
            cluster.Vertices.Clear();
            foreach (int vertId in cluster.VertexIds)
                cluster.Vertices.Add(verts[vertId]);
        }

        // Copy edge codes from global to each cluster (Python lines 2560-2568)
        var fallback = (EdgeCodeConstants.EdgeAngleZero | EdgeCodeConstants.EdgeUnmatched,
            EdgeCodeConstants.EdgeAngleZero | EdgeCodeConstants.EdgeUnmatched,
            EdgeCodeConstants.EdgeAngleZero | EdgeCodeConstants.EdgeUnmatched);
        foreach (var cluster in clusterStack)
        {
            cluster.EdgeCodes.Clear();
            foreach (int unitId in cluster.UnitIds)
            {
                if (unitId >= 0 && unitId < edgeCodesGlobal.Length)
                    cluster.EdgeCodes[unitId] = edgeCodesGlobal[unitId];
                else
                    cluster.EdgeCodes[unitId] = fallback;
            }
        }

        // STEP 6: Adjust KD-tree leaf offsets (RenderWare lines 968-976)
        int unitClusterIdShift = clusterStack.Count > 65536 ? 20 : 16; // RenderWare: AdjustKDTreeNodeEntriesForClusterCollection
        for (int clusterId = 0; clusterId < clusterStack.Count; clusterId++)
        {
            clusterStack[clusterId].ClusterId = (uint)clusterId;
            KdTreeAdjustForCluster.Execute(clusterStack[clusterId], clusterId, unitClusterIdShift, leafMap);
        }

        // STEP 7: Initialize runtime KD-tree (RenderWare line 486)
        var rtKdTreeNodes = KdTreeRuntime.InitializeRuntimeKdTree(rootBuildNode);

        var bboxMin = rootBuildNode.Bbox.Min;
        var bboxMax = rootBuildNode.Bbox.Max;

        return new ClusteredMeshPipelineResult
        {
            Clusters = clusterStack,
            KdTreeNodes = rtKdTreeNodes,
            BboxMin = bboxMin,
            BboxMax = bboxMax,
            ValidatedTriangles = validatedTris
        };
    }
}
