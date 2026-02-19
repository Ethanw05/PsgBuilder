using System.IO;
using System.Numerics;
using PsgBuilder.Core.Rw;
using PsgBuilder.Glb;
using SharpGLTF.Schema2;

namespace PsgBuilder.Mesh;

/// <summary>
/// Builds IMeshPsgInput from GLB with one mesh per primitive. When a primitive exceeds 65536
/// vertices, it is split into multiple meshes—all in the same PSG (no separate files).
/// </summary>
public sealed class MeshInputFromGlbMulti : IMeshPsgInput
{
    public (float X, float Y, float Z) BoundsMin { get; }
    public (float X, float Y, float Z) BoundsMax { get; }
    public IReadOnlyList<MeshPart> Parts { get; }
    public string MaterialName { get; }
    public RenderMaterialDataRwBuilder.MaterialTextureOverrides? TextureChannelOverrides { get; set; }
    /// <inheritdoc />
    public string? AttributorMaterialPath { get; set; }
    public string? InstanceGuidSalt { get; }

    public MeshInputFromGlbMulti(string glbPath, float scale = 1f, bool reverseWinding = false)
    {
        InstanceGuidSalt = Path.GetFileNameWithoutExtension(glbPath);
        var model = ModelRoot.Load(glbPath);
        Build(model, scale, reverseWinding, out var boundsMin, out var boundsMax, out var parts, out var materialName);
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        Parts = parts;
        MaterialName = materialName;
    }

    public MeshInputFromGlbMulti(ModelRoot model, float scale = 1f, bool reverseWinding = false)
    {
        Build(model, scale, reverseWinding, out var boundsMin, out var boundsMax, out var parts, out var materialName);
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        Parts = parts;
        MaterialName = materialName;
    }

    private static void Build(
        ModelRoot model,
        float scale,
        bool reverseWinding,
        out (float X, float Y, float Z) boundsMin,
        out (float X, float Y, float Z) boundsMax,
        out IReadOnlyList<MeshPart> parts,
        out string materialName)
    {
        var results = MeshVertexFlattener.FlattenAllWithOverflowSplits(model);
        if (results.Count == 0)
            throw new InvalidOperationException("GLB produced no mesh geometry.");

        materialName = PickDominantMaterial(results);

        var partList = new List<MeshPart>();
        var globalMin = new Vector3(float.MaxValue);
        var globalMax = new Vector3(float.MinValue);

        foreach (var r in results)
        {
            var vertexData = MeshVertexPacker.PackVertices(
                r.Positions,
                r.Normals,
                r.Uvs,
                r.Indices,
                scale);
            var indexData = MeshIndexPacker.PackIndices(r.Indices, reverseWinding);

            partList.Add(new MeshPart(vertexData, indexData, 0));

            globalMin = Vector3.Min(globalMin, r.Bounds.Min);
            globalMax = Vector3.Max(globalMax, r.Bounds.Max);
        }

        boundsMin = (globalMin.X * scale, globalMin.Y * scale, globalMin.Z * scale);
        boundsMax = (globalMax.X * scale, globalMax.Y * scale, globalMax.Z * scale);
        parts = partList;
    }

    private static string PickDominantMaterial(IReadOnlyList<MeshVertexFlattener.Result> results)
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
}
