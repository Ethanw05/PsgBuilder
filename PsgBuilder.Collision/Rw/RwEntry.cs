namespace PsgBuilder.Collision.Rw;

/// <summary>
/// Entry in KD-tree. Matches RenderWare Entry struct (rwckdtreebuilder.cpp line 76).
/// Ported from Collision_Export_Dumbad_Tuukkas_original.py lines 249-253 (RWEntry).
/// </summary>
public sealed class RwEntry
{
    /// <summary>Original triangle ID (entry index).</summary>
    public int EntryIndex { get; set; }

    /// <summary>Bounding box surface area of this entry.</summary>
    public double EntryBBoxSurfaceArea { get; set; }

    public RwEntry(int entryIndex, double bboxSurfaceArea)
    {
        EntryIndex = entryIndex;
        EntryBBoxSurfaceArea = bboxSurfaceArea;
    }
}
