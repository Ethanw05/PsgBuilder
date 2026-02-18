namespace PsgBuilder.Collision.Compression;

/// <summary>
/// Vertex compression mode and constants. Matches Python/RenderWare.
/// Ported from Collision_Export_Dumbad_Tuukkas_original.py (VERTICES_* usage) and granularity tolerance.
/// </summary>
public static class CompressionConstants
{
    public const byte VerticesUncompressed = 0;
    public const byte Vertices16BitCompressed = 1;
    public const byte Vertices32BitCompressed = 2;

    /// <summary>Granularity tolerance for 16-bit range check (RenderWare: 65534).</summary>
    public const int GranularityTolerance = 65534;
}
