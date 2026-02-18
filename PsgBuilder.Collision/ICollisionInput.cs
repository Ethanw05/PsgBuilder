namespace PsgBuilder.Collision;

/// <summary>
/// Abstraction for collision mesh input. Implemented by the Blender add-on or other hosts;
/// PsgBuilder.Collision consumes this interface only (no Blender dependency).
/// Plan: Section 2 – Input abstraction.
/// </summary>
public interface ICollisionInput
{
    /// <summary>Vertex positions (X, Y, Z).</summary>
    IReadOnlyList<System.Numerics.Vector3> Vertices { get; }

    /// <summary>Triangles as (vertex index 0, 1, 2).</summary>
    IReadOnlyList<(int V0, int V1, int V2)> Faces { get; }

    /// <summary>Optional spline curves (each curve is a list of points).</summary>
    IReadOnlyList<IReadOnlyList<System.Numerics.Vector3>>? Splines { get; }

    /// <summary>Axis-aligned bounding box (min, max) of the mesh.</summary>
    (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max) Bounds { get; }
}
