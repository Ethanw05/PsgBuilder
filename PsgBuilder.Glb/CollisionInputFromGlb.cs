using System.Numerics;
using PsgBuilder.Collision;

namespace PsgBuilder.Glb;

/// <summary>
/// Adapter that feeds flattened GLB geometry into the collision pipeline.
/// </summary>
public sealed class CollisionInputFromGlb : ICollisionInput, ICollisionInputWithSurfaceIds
{
    public IReadOnlyList<Vector3> Vertices { get; }
    public IReadOnlyList<(int V0, int V1, int V2)> Faces { get; }
    public IReadOnlyList<IReadOnlyList<Vector3>>? Splines { get; }
    public (Vector3 Min, Vector3 Max) Bounds { get; }
    public IReadOnlyList<int>? SurfaceIds { get; }

    public CollisionInputFromGlb(
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<(int V0, int V1, int V2)> faces,
        IReadOnlyList<IReadOnlyList<Vector3>>? splines,
        int surfaceId)
    {
        Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
        Faces = faces ?? throw new ArgumentNullException(nameof(faces));
        Splines = splines;
        Bounds = ComputeBounds(vertices);
        SurfaceIds = Enumerable.Repeat(surfaceId, faces.Count).ToArray();
    }

    private static (Vector3 Min, Vector3 Max) ComputeBounds(IReadOnlyList<Vector3> v)
    {
        if (v.Count == 0) return (Vector3.Zero, Vector3.Zero);

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        foreach (var p in v)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z > maxZ) maxZ = p.Z;
        }
        return (new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
    }
}

