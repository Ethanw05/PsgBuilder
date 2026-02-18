using PsgBuilder.Collision.Math;

namespace PsgBuilder.Collision.Rw;

/// <summary>
/// Split plane for KD-tree. Matches RenderWare KDTreeSplit (rwckdtreebuilder.cpp line 40).
/// Ported from Collision_Export_Dumbad_Tuukkas_original.py lines 281-289 (RWKDTreeSplit).
/// </summary>
public sealed class RwKdTreeSplit
{
    public int MAxis { get; set; }
    /// <summary>
    /// Split plane value. Use double to match Python float (64-bit) behavior for split decisions.
    /// Serialized KD-tree does not store this directly; it only affects partitioning.
    /// </summary>
    public double MValue { get; set; }
    public int MNumLeft { get; set; }
    public int MNumRight { get; set; }
    public AABBox MLeftBBox { get; set; }
    public AABBox MRightBBox { get; set; }

    public RwKdTreeSplit()
    {
        MAxis = 0;
        MValue = 0.0;
        MNumLeft = 0;
        MNumRight = 0;
        MLeftBBox = new AABBox(System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero);
        MRightBBox = new AABBox(System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero);
    }
}
