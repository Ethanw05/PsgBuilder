using System.Numerics;
using SharpGLTF.Schema2;

namespace PsgBuilder.Glb;

/// <summary>
/// Flattens a GLB mesh for mesh PSG. First mesh, first primitive.
/// Uses vertex welding so vertex count stays ≤ 65536 (uint16 index limit for Noesis/game).
/// Extraction logic matches glbtopsg (tangent computation, world matrix) but welds vertices.
/// </summary>
public static class MeshVertexFlattener
{
    public sealed record Result(
        IReadOnlyList<Vector3> Positions,
        IReadOnlyList<Vector3> Normals,
        IReadOnlyList<Vector2> Uvs,
        IReadOnlyList<int> Indices,
        string MaterialName,
        (Vector3 Min, Vector3 Max) Bounds);

    public static Result Flatten(string glbPath)
    {
        var model = ModelRoot.Load(glbPath);
        return Flatten(model);
    }

    /// <summary>
    /// Flattens first mesh, first primitive only (backup-compatible). Use FlattenChunked for multi-mesh or overflow.
    /// </summary>
    public static Result Flatten(ModelRoot model)
    {
        if (model?.LogicalMeshes == null || model.LogicalMeshes.Count == 0)
            throw new InvalidOperationException("GLB has no meshes.");

        var mesh = model.LogicalMeshes[0];
        var prim = mesh.Primitives[0];

        if (prim.DrawPrimitiveType != PrimitiveType.TRIANGLES)
            throw new InvalidOperationException("Mesh must use TRIANGLES.");

        var positions = prim.GetVertexAccessor("POSITION")?.AsVector3Array().ToArray()
            ?? throw new InvalidOperationException("Mesh has no POSITION.");
        var normals = prim.GetVertexAccessor("NORMAL")?.AsVector3Array().ToArray();
        var uvs = prim.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array().ToArray();
        var indices = prim.GetIndexAccessor() != null
            ? prim.GetIndexAccessor()!.AsIndicesArray().Select(i => (int)i).ToArray()
            : Enumerable.Range(0, positions.Length).ToArray();

        if (normals == null || normals.Length < positions.Length)
            normals = GenerateDefaultNormals(positions, indices);
        if (uvs == null || uvs.Length < positions.Length)
            uvs = Enumerable.Range(0, positions.Length).Select(_ => Vector2.Zero).ToArray();

        var world = GetWorldMatrixForMesh(model, mesh);

        var indexMap = new Dictionary<VertKey, int>();
        var outPos = new List<Vector3>();
        var outNorm = new List<Vector3>();
        var outUv = new List<Vector2>();
        var outIdx = new List<int>();

        for (int i = 0; i < indices.Length; i++)
        {
            int vi = indices[i];
            var p = positions[vi];
            var n = normals != null && vi < normals.Length ? normals[vi] : Vector3.UnitY;
            var u = uvs != null && vi < uvs.Length ? uvs[vi] : Vector2.Zero;
            var key = new VertKey(p, n, u);
            if (!indexMap.TryGetValue(key, out int newIdx))
            {
                newIdx = outPos.Count;
                indexMap[key] = newIdx;
                outPos.Add(Vector3.Transform(p, world));
                outNorm.Add(Vector3.Normalize(TransformNormal(n, world)));
                outUv.Add(u);
            }
            outIdx.Add(newIdx);
        }

        if (outPos.Count > 65536)
            throw new InvalidOperationException($"Mesh has {outPos.Count} unique vertices; PSG uint16 indices support max 65536. Consider simplifying the mesh.");

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in outPos)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        string matName = prim.Material?.Name ?? "DefaultMaterial";
        return new Result(outPos, outNorm, outUv, outIdx, matName, (min, max));
    }

    /// <summary>
    /// Flattens all meshes and returns one or more chunks. When combined vertex count exceeds 65536,
    /// overflow is split into additional chunks (each with its own vertices/UVs).
    /// </summary>
    public static IReadOnlyList<Result> FlattenChunked(string glbPath)
    {
        var model = ModelRoot.Load(glbPath);
        return FlattenChunked(model);
    }

    public static IReadOnlyList<Result> FlattenChunked(ModelRoot model)
    {
        var all = FlattenAll(model);
        return all.Count == 1 && all[0].Positions.Count <= MaxVerticesPerChunk
            ? all
            : CombineOrChunk(all);
    }

    /// <summary>
    /// Flattens all meshes in the GLB. One Result per mesh primitive (all geometry).
    /// </summary>
    public static IReadOnlyList<Result> FlattenAll(string glbPath)
    {
        var model = ModelRoot.Load(glbPath);
        return FlattenAll(model);
    }

    public static IReadOnlyList<Result> FlattenAll(ModelRoot model)
    {
        if (model?.LogicalMeshes == null || model.LogicalMeshes.Count == 0)
            throw new InvalidOperationException("GLB has no meshes.");
        var list = new List<Result>();
        foreach (var mesh in model.LogicalMeshes)
        {
            for (int p = 0; p < mesh.Primitives.Count; p++)
                list.Add(FlattenPrimitive(model, mesh, mesh.Primitives[p]));
        }
        if (list.Count == 0)
            throw new InvalidOperationException("GLB has no mesh primitives.");
        return list;
    }

    private const int MaxVerticesPerChunk = 65536;

    /// <summary>
    /// Combines multiple flattened mesh results into one. Concatenates vertices and indices
    /// (indices are offset by vertex count). Use to produce a single-mesh PSG from multi-mesh GLBs.
    /// </summary>
    public static Result Combine(IReadOnlyList<Result> results)
    {
        var chunks = CombineOrChunk(results);
        if (chunks.Count != 1)
            throw new InvalidOperationException($"Combined mesh has more than {MaxVerticesPerChunk} vertices; use CombineOrChunk to get chunked results.");
        return chunks[0];
    }

    /// <summary>
    /// Combines meshes into one or more chunks. When total vertices exceed 65536, splits into
    /// multiple chunks so each has ≤ 65536 vertices. Overflow vertices/UVs go into new PSG chunks.
    /// </summary>
    public static IReadOnlyList<Result> CombineOrChunk(IReadOnlyList<Result> results)
    {
        if (results == null || results.Count == 0)
            throw new InvalidOperationException("No results to combine.");
        if (results.Count == 1 && results[0].Positions.Count <= MaxVerticesPerChunk)
            return results;

        string matName = PickDominantMaterial(results);
        var chunks = new List<Result>();
        var outPos = new List<Vector3>();
        var outNorm = new List<Vector3>();
        var outUv = new List<Vector2>();
        var outIdx = new List<int>();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        void EmitChunk()
        {
            if (outPos.Count == 0) return;
            chunks.Add(new Result(
                outPos.ToArray(),
                outNorm.ToArray(),
                outUv.ToArray(),
                outIdx.ToArray(),
                matName,
                (min, max)));
            outPos.Clear();
            outNorm.Clear();
            outUv.Clear();
            outIdx.Clear();
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
        }

        foreach (var r in results)
        {
            if (r.Positions.Count > MaxVerticesPerChunk)
            {
                EmitChunk();
                foreach (var split in SplitLargeResult(r))
                    chunks.Add(split);
                continue;
            }

            int baseVertex = outPos.Count;
            if (baseVertex + r.Positions.Count > MaxVerticesPerChunk)
            {
                EmitChunk();
                baseVertex = 0;
            }

            outPos.AddRange(r.Positions);
            outNorm.AddRange(r.Normals);
            outUv.AddRange(r.Uvs);
            foreach (int idx in r.Indices)
                outIdx.Add(idx + baseVertex);
            min = Vector3.Min(min, r.Bounds.Min);
            max = Vector3.Max(max, r.Bounds.Max);
        }

        EmitChunk();
        return chunks;
    }

    /// <summary>
    /// Returns one or more Results. If the primitive exceeds 65536 vertices, splits into chunks.
    /// Use for one-mesh-per-primitive PSG with overflow staying in the same file.
    /// </summary>
    public static IReadOnlyList<Result> ChunkResultIfOverflow(Result r)
    {
        if (r.Positions.Count <= MaxVerticesPerChunk)
            return new[] { r };
        return SplitLargeResult(r);
    }

    /// <summary>
    /// Flattens all primitives; each that exceeds 65536 vertices is split into chunks.
    /// One Result per mesh in the final PSG (one per primitive, plus overflow splits).
    /// </summary>
    public static IReadOnlyList<Result> FlattenAllWithOverflowSplits(string glbPath)
    {
        var model = ModelRoot.Load(glbPath);
        return FlattenAllWithOverflowSplits(model);
    }

    public static IReadOnlyList<Result> FlattenAllWithOverflowSplits(ModelRoot model)
    {
        var all = FlattenAll(model);
        var result = new List<Result>();
        foreach (var r in all)
            result.AddRange(ChunkResultIfOverflow(r));
        return result;
    }

    /// <summary>
    /// Splits a single Result that exceeds 65536 vertices into chunks by triangle batches.
    /// </summary>
    private static IReadOnlyList<Result> SplitLargeResult(Result r)
    {
        var chunks = new List<Result>();
        var pos = r.Positions;
        var norm = r.Normals;
        var uv = r.Uvs;
        var idx = r.Indices;

        var outPos = new List<Vector3>();
        var outNorm = new List<Vector3>();
        var outUv = new List<Vector2>();
        var outIdx = new List<int>();
        var oldToNew = new Dictionary<int, int>();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        void Emit()
        {
            if (outPos.Count == 0) return;
            chunks.Add(new Result(
                outPos.ToArray(),
                outNorm.ToArray(),
                outUv.ToArray(),
                outIdx.ToArray(),
                r.MaterialName,
                (min, max)));
            outPos.Clear();
            outNorm.Clear();
            outUv.Clear();
            outIdx.Clear();
            oldToNew.Clear();
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
        }

        for (int i = 0; i < idx.Count; i += 3)
        {
            int i0 = idx[i], i1 = idx[i + 1], i2 = idx[i + 2];
            int AddVert(int vi)
            {
                if (oldToNew.TryGetValue(vi, out int n)) return n;
                if (outPos.Count >= MaxVerticesPerChunk)
                {
                    Emit();
                }
                n = outPos.Count;
                oldToNew[vi] = n;
                outPos.Add(pos[vi]);
                outNorm.Add(norm[vi]);
                outUv.Add(uv[vi]);
                var p = pos[vi];
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                return n;
            }
            outIdx.Add(AddVert(i0));
            outIdx.Add(AddVert(i1));
            outIdx.Add(AddVert(i2));
        }

        Emit();
        return chunks;
    }

    /// <summary>
    /// Picks the material name from the result with the most triangles (dominant geometry).
    /// Ensures texture linkage uses the material that covers most of the combined mesh.
    /// </summary>
    private static string PickDominantMaterial(IReadOnlyList<Result> results)
    {
        int bestTriCount = -1;
        string bestMat = results[0].MaterialName;
        foreach (var r in results)
        {
            int triCount = r.Indices.Count / 3;
            if (triCount > bestTriCount)
            {
                bestTriCount = triCount;
                bestMat = r.MaterialName;
            }
        }
        return bestMat;
    }

    private static Result FlattenPrimitive(ModelRoot model, Mesh mesh, MeshPrimitive prim)
    {
        if (prim.DrawPrimitiveType != PrimitiveType.TRIANGLES)
            throw new InvalidOperationException("Mesh must use TRIANGLES.");

        var positions = prim.GetVertexAccessor("POSITION")?.AsVector3Array().ToArray()
            ?? throw new InvalidOperationException("Mesh has no POSITION.");
        var normals = prim.GetVertexAccessor("NORMAL")?.AsVector3Array().ToArray();
        var uvs = prim.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array().ToArray();
        var indices = prim.GetIndexAccessor() != null
            ? prim.GetIndexAccessor()!.AsIndicesArray().Select(i => (int)i).ToArray()
            : Enumerable.Range(0, positions.Length).ToArray();

        if (normals == null || normals.Length < positions.Length)
            normals = GenerateDefaultNormals(positions, indices);
        if (uvs == null || uvs.Length < positions.Length)
            uvs = Enumerable.Range(0, positions.Length).Select(_ => Vector2.Zero).ToArray();

        var world = GetWorldMatrixForMesh(model, mesh);

        // Welded extraction: same vertex (pos+normal+uv) = same index. Keeps count ≤ 65536 for uint16 indices.
        var indexMap = new Dictionary<VertKey, int>();
        var outPos = new List<Vector3>();
        var outNorm = new List<Vector3>();
        var outUv = new List<Vector2>();
        var outIdx = new List<int>();

        for (int i = 0; i < indices.Length; i++)
        {
            int vi = indices[i];
            var p = positions[vi];
            var n = normals != null && vi < normals.Length ? normals[vi] : Vector3.UnitY;
            var u = uvs != null && vi < uvs.Length ? uvs[vi] : Vector2.Zero;
            var key = new VertKey(p, n, u);
            if (!indexMap.TryGetValue(key, out int newIdx))
            {
                newIdx = outPos.Count;
                indexMap[key] = newIdx;
                outPos.Add(Vector3.Transform(p, world));
                outNorm.Add(Vector3.Normalize(TransformNormal(n, world)));
                outUv.Add(u);
            }
            outIdx.Add(newIdx);
        }

        if (outPos.Count > 65536)
            throw new InvalidOperationException($"Mesh has {outPos.Count} unique vertices; PSG uint16 indices support max 65536. Consider simplifying the mesh.");

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in outPos)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        string matName = prim.Material?.Name ?? "DefaultMaterial";
        return new Result(outPos, outNorm, outUv, outIdx, matName, (min, max));
    }

    private readonly struct VertKey : IEquatable<VertKey>
    {
        private readonly Vector3 _p;
        private readonly Vector3 _n;
        private readonly Vector2 _u;

        public VertKey(Vector3 p, Vector3 n, Vector2 u)
        {
            _p = p;
            _n = n;
            _u = u;
        }

        public bool Equals(VertKey other) =>
            _p.Equals(other._p) && _n.Equals(other._n) && _u.Equals(other._u);

        public override bool Equals(object? obj) => obj is VertKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_p, _n, _u);
    }

    private static Matrix4x4 GetWorldMatrixForMesh(ModelRoot model, Mesh targetMesh)
    {
        // Find first node that references this mesh and compute full world matrix
        var node = model.LogicalNodes.FirstOrDefault(n => n.Mesh == targetMesh);
        return node?.WorldMatrix ?? Matrix4x4.Identity;
    }

    private static Vector3[] ComputeTangentsOnRaw(
        Vector3[] positions, Vector3[] normals, Vector2[] uvs, int[] indices)
    {
        var tangentAcc = new Vector3[positions.Length];
        for (int i = 0; i < indices.Length; i += 3)
        {
            int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            var p0 = positions[i0]; var p1 = positions[i1]; var p2 = positions[i2];
            var uv0 = uvs[i0]; var uv1 = uvs[i1]; var uv2 = uvs[i2];
            var edge1 = p1 - p0;
            var edge2 = p2 - p0;
            var deltaUV1 = uv1 - uv0;
            var deltaUV2 = uv2 - uv0;
            float f = deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y;
            if (Math.Abs(f) > 1e-6f)
            {
                float r = 1f / f;
                var tangent = (edge1 * deltaUV2.Y - edge2 * deltaUV1.Y) * r;
                tangentAcc[i0] += tangent;
                tangentAcc[i1] += tangent;
                tangentAcc[i2] += tangent;
            }
        }
        var result = new Vector3[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            var t = tangentAcc[i];
            var n = normals[i];
            t -= n * Vector3.Dot(n, t);
            if (t.LengthSquared() > 1e-9f)
                result[i] = Vector3.Normalize(t);
            else
                result[i] = Vector3.UnitX;
        }
        return result;
    }

    private static Vector3[] GenerateDefaultNormals(Vector3[] positions, int[] indices)
    {
        var normals = new Vector3[positions.Length];
        for (int i = 0; i < indices.Length; i += 3)
        {
            int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            var p0 = positions[i0]; var p1 = positions[i1]; var p2 = positions[i2];
            var n = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
            normals[i0] += n;
            normals[i1] += n;
            normals[i2] += n;
        }
        for (int i = 0; i < normals.Length; i++)
        {
            if (normals[i].LengthSquared() > 1e-9f)
                normals[i] = Vector3.Normalize(normals[i]);
            else
                normals[i] = Vector3.UnitY;
        }
        return normals;
    }

    private static Vector3 TransformNormal(Vector3 n, Matrix4x4 world)
    {
        return Vector3.Normalize(new Vector3(
            n.X * world.M11 + n.Y * world.M21 + n.Z * world.M31,
            n.X * world.M12 + n.Y * world.M22 + n.Z * world.M32,
            n.X * world.M13 + n.Y * world.M23 + n.Z * world.M33));
    }
}
