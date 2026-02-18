namespace PsgBuilder.Collision.EdgeCodes;

/// <summary>
/// Generate per-triangle edge codes from neighbor/cosine data. edgecodegenerator.cpp lines 36-119.
/// Ported from Collision_Export_Dumbad_Tuukkas_original.py lines 2461-2496.
/// </summary>
public static class EdgeCodeGenerator
{
    private const float MinConcaveEdgeCosine = -1f;

    /// <summary>Produce (e0, e1, e2) per triangle. Caller allocates/copies to clusters.</summary>
    public static (int E0, int E1, int E2)[] GenerateEdgeCodes(int numTris, int?[][] triangleNeighbors, float[][] triangleEdgeCosines)
    {
        var result = new (int, int, int)[numTris];
        float cappedMinConcave = System.Math.Clamp(MinConcaveEdgeCosine, -1f, 1f);
        float concaveThreshold = 2f - cappedMinConcave;
        for (int triId = 0; triId < numTris; triId++)
        {
            var codes = new int[3];
            for (int edgeIdx = 0; edgeIdx < 3; edgeIdx++)
            {
                bool matched = triangleNeighbors[triId][edgeIdx].HasValue;
                float extendedEdgeCos = triangleEdgeCosines[triId][edgeIdx];
                int code = EdgeCosineToAngleByte.Execute(extendedEdgeCos);
                if (extendedEdgeCos < 1.0f) code |= EdgeCodeConstants.EdgeConvex;
                if (extendedEdgeCos > concaveThreshold) code = EdgeCodeConstants.EdgeAngleZero;
                if (!matched) code |= EdgeCodeConstants.EdgeUnmatched;
                codes[edgeIdx] = code;
            }
            result[triId] = (codes[0], codes[1], codes[2]);
        }
        return result;
    }
}
