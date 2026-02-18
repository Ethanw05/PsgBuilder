using PsgBuilder.Collision.Math;

namespace PsgBuilder.Collision.Rw;

/// <summary>
/// KD-tree BuildNode. Matches RenderWare BuildNode (kdtreebuilder.h lines 129-180).
/// ext0/ext1 are NOT stored here; they are computed during InitializeRuntimeKDTree from child bboxes.
/// Ported from Collision_Export_Dumbad_Tuukkas_original.py lines 255-279 (RWBuildNode).
/// </summary>
public sealed class RwBuildNode
{
    public RwBuildNode? Parent { get; set; }
    /// <summary>int32_t m_index (initialized to 0).</summary>
    public int MIndex { get; set; }
    public AABBox Bbox { get; set; }
    /// <summary>uint32_t m_firstEntry.</summary>
    public uint MFirstEntry { get; set; }
    /// <summary>Saved initial value to detect if already set.</summary>
    public uint MFirstEntryInitial { get; set; }
    /// <summary>uint32_t m_numEntries.</summary>
    public uint MNumEntries { get; set; }
    /// <summary>uint32_t m_splitAxis (0=X, 1=Y, 2=Z).</summary>
    public uint MSplitAxis { get; set; }
    public RwBuildNode? Left { get; set; }
    public RwBuildNode? Right { get; set; }

    public RwBuildNode(RwBuildNode? parent, AABBox bbox, uint firstEntry, uint numEntries)
    {
        Parent = parent;
        MIndex = 0;
        Bbox = bbox;
        MFirstEntry = firstEntry;
        MFirstEntryInitial = firstEntry;
        MNumEntries = numEntries;
        MSplitAxis = 0;
        Left = null;
        Right = null;
    }
}
