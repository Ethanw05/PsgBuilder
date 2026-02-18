using PsgBuilder.Collision.Math;

namespace PsgBuilder.Collision.KdTree;

/// <summary>
/// Multi-axis split stats. RenderWare KDTreeMultiAxisSplit (rwckdtreebuilder.cpp).
/// Ported from Collision_Export_Dumbad_Tuukkas_original.py lines 813-821.
/// </summary>
public sealed class KdTreeMultiAxisSplit
{
    /// <summary>Split values for all 3 axes. Double for Python float parity.</summary>
    public double[] MValue { get; } = new double[3];
    public int[] MNumLeft { get; } = new int[3];
    public int[] MNumRight { get; } = new int[3];
    public AABBox[] MLeftBBox { get; } = new AABBox[3];
    public AABBox[] MRightBBox { get; } = new AABBox[3];
}
