using PsgBuilder.Core.Rw;

namespace PsgBuilder.Mesh;

/// <summary>
/// Input for mesh PSG composition. Provides vertices, indices, bounds, and material info.
/// </summary>
public interface IMeshPsgInput
{
    (float X, float Y, float Z) BoundsMin { get; }
    (float X, float Y, float Z) BoundsMax { get; }
    IReadOnlyList<MeshPart> Parts { get; }
    /// <summary>Material name for AttribulatorMaterialName channel (Blender importer).</summary>
    string MaterialName { get; }
    /// <summary>Optional. When set, material channels use these GUIDs so they resolve to texture PSGs in the same cPres folder.</summary>
    RenderMaterialDataRwBuilder.MaterialTextureOverrides? TextureChannelOverrides { get; }
    /// <summary>AttributorMaterialName stream path. Null = "environmentsimple.default" (StartPark). Use "environment.default" for SkateSchool-style static meshes.</summary>
    string? AttributorMaterialPath => null;
}

/// <summary>
/// One mesh part: vertex bytes, index bytes, material index.
/// </summary>
public sealed record MeshPart(
    byte[] VertexData,
    byte[] IndexData,
    int MaterialIndex);
