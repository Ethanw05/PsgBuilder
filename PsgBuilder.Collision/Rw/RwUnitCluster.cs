using System.Numerics;

namespace PsgBuilder.Collision.Rw;

/// <summary>
/// Cluster for storing triangles. Matches RenderWare UnitCluster (unitcluster.h lines 37-167).
/// Ported from Collision_Export_Dumbad_Tuukkas_original.py lines 291-324 (RWUnitCluster).
/// </summary>
public sealed class RwUnitCluster
{
    public uint ClusterId { get; set; }
    /// <summary>Vertex32 clusterOffset (used in 16-bit compression mode only). (x, y, z) int32.</summary>
    public (int X, int Y, int Z) ClusterOffset { get; set; }
    public List<int> UnitIds { get; } = new();
    public uint NumUnits => (uint)UnitIds.Count;
    public List<int> VertexIds { get; } = new();
    public uint NumVertices => (uint)VertexIds.Count;
    /// <summary>0=uncompressed, 1=16-bit, 2=32-bit.</summary>
    public byte CompressionMode { get; set; }

    /// <summary>Computed vertex positions (filled after compression).</summary>
    public List<Vector3> Vertices { get; } = new();
    /// <summary>Global vertex id -> local index.</summary>
    public Dictionary<int, int> VertexMap { get; } = new();
    /// <summary>Start byte offset in global serialization.</summary>
    public long ByteOffsetStart { get; set; }
    /// <summary>Per-triangle edge codes (key = triangle index in cluster).</summary>
    public Dictionary<int, (int E0, int E1, int E2)> EdgeCodes { get; } = new();
}
