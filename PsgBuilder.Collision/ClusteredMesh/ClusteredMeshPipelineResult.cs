using System.Numerics;
using PsgBuilder.Collision.KdTree;
using PsgBuilder.Collision.Rw;

namespace PsgBuilder.Collision.ClusteredMesh;

/// <summary>
/// Result of ClusteredMeshPipeline.BuildComplete. Matches Python RW_BuildClusteredMeshComplete return.
/// </summary>
public sealed class ClusteredMeshPipelineResult
{
    public IReadOnlyList<RwUnitCluster> Clusters { get; init; } = null!;
    public IReadOnlyList<KdTreeNode> KdTreeNodes { get; init; } = null!;
    public Vector3 BboxMin { get; init; }
    public Vector3 BboxMax { get; init; }
    /// <summary>Validated triangle list (degenerate triangles removed).</summary>
    public IReadOnlyList<(int V0, int V1, int V2)> ValidatedTriangles { get; init; } = null!;
}
