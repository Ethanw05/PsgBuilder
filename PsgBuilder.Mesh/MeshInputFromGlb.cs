using PsgBuilder.Core.Rw;
using PsgBuilder.Glb;

namespace PsgBuilder.Mesh;

/// <summary>
/// Builds IMeshPsgInput from GLB via MeshVertexFlattener.
/// Uses first mesh, first primitive only (backup-compatible).
/// </summary>
public sealed class MeshInputFromGlb : IMeshPsgInput
{
    public (float X, float Y, float Z) BoundsMin { get; }
    public (float X, float Y, float Z) BoundsMax { get; }
    public IReadOnlyList<MeshPart> Parts { get; }
    public string MaterialName { get; }
    public RenderMaterialDataRwBuilder.MaterialTextureOverrides? TextureChannelOverrides { get; set; }
    public string? AttributorMaterialPath { get; set; }

    public MeshInputFromGlb(string glbPath, float scale = 1f, bool reverseWinding = false)
    {
        var result = MeshVertexFlattener.Flatten(glbPath);
        MaterialName = result.MaterialName;
        BoundsMin = (result.Bounds.Min.X * scale, result.Bounds.Min.Y * scale, result.Bounds.Min.Z * scale);
        BoundsMax = (result.Bounds.Max.X * scale, result.Bounds.Max.Y * scale, result.Bounds.Max.Z * scale);

        var vertexData = MeshVertexPacker.PackVertices(
            result.Positions,
            result.Normals,
            result.Uvs,
            result.Indices,
            scale);
        var indexData = MeshIndexPacker.PackIndices(result.Indices, reverseWinding);

        Parts = new[] { new MeshPart(vertexData, indexData, 0) };
    }
}
