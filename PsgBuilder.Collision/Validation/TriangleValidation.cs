using System.Numerics;
using PsgBuilder.Collision.Math;

namespace PsgBuilder.Collision.Validation;

/// <summary>
/// Triangle validity check. RenderWare TriangleValidator::IsTriangleValid (trianglevalidator.cpp lines 37-53).
/// validate_triangles: rwcclusteredmeshbuildermethods.cpp lines 100-141.
/// Ported from Collision_Export_Dumbad_Tuukkas_original.py lines 2320-2390.
/// </summary>
public static class TriangleValidation
{
    private const double MinimumReciprocal = 1e-10;

    /// <summary>True if triangle has non-zero area (normal length squared &gt; threshold). Python line 2344: MINIMUM_RECIPROCAL = 1e-10.</summary>
    public static bool IsTriangleValid(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var normal = Vector3Extensions.Cross(edge1, edge2);
        float lengthSquared = Vector3.Dot(normal, normal);
        return (double)lengthSquared > MinimumReciprocal;
    }

    /// <summary>Filter out degenerate triangles. Returns only valid triangles; throws if all degenerate.</summary>
    public static IReadOnlyList<(int V0, int V1, int V2)> ValidateTriangles(IReadOnlyList<Vector3> verts, IReadOnlyList<(int V0, int V1, int V2)> tris)
    {
        var validTris = new List<(int, int, int)>();
        foreach (var (v0Idx, v1Idx, v2Idx) in tris)
        {
            var v0 = verts[v0Idx];
            var v1 = verts[v1Idx];
            var v2 = verts[v2Idx];
            if (IsTriangleValid(v0, v1, v2))
                validTris.Add((v0Idx, v1Idx, v2Idx));
        }
        if (validTris.Count == 0)
            throw new InvalidOperationException("All triangles are degenerate. Check mesh geometry.");
        return validTris;
    }
}
