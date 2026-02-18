namespace PsgBuilder.Collision.EdgeCodes;

/// <summary>
/// Quantize extended edge cosine to 5-bit angle byte. ClusteredMeshBuilderUtils::EdgeCosineToAngleByte (clusteredmeshbuilderutils.cpp lines 30-63).
/// Ported from Collision_Export_Dumbad_Tuukkas_original.py lines 166-213.
/// </summary>
public static class EdgeCosineToAngleByte
{
    private const double MinAngle = 6.6e-5;

    /// <summary>Returns value in range 0-26. B=0 fully convex, B=26 coplanar.</summary>
    public static int Execute(double edgeCosine)
    {
        double angle = edgeCosine > 1.0
            ? System.Math.Acos(2.0 - edgeCosine)
            : System.Math.Acos(edgeCosine);
        angle = System.Math.Max(angle, MinAngle);
        int result = (int)(-2.0 * System.Math.Log(angle / System.Math.PI) / System.Math.Log(2.0));
        if (result < 0) return 0;
        if (result > 26) return 26;
        return result;
    }
}
