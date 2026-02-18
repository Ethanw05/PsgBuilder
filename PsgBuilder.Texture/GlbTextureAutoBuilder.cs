using PsgBuilder.Core.Psg;
using PsgBuilder.Texture.Dds;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;

namespace PsgBuilder.Texture;

/// <summary>
/// Extracts textures from a GLB material, converts PNG/JPG to DDS (DXT5), builds texture PSG files,
/// and returns channel GUIDs so mesh material overrides can link to them.
/// Supports optional BlenRose sidecar JSON texture paths with fallback to GLB textures.
/// </summary>
public static class GlbTextureAutoBuilder
{
    public sealed record BuiltTexturePsg(
        string ChannelName,
        string SourceImageName,
        ulong Guid,
        string PsgPath);

    public sealed record GlbTextureAutoBuildResult(
        ulong? DiffuseGuid,
        ulong? NormalGuid,
        ulong? LightmapGuid,
        ulong? SpecularGuid,
        IReadOnlyList<BuiltTexturePsg> BuiltTextures,
        IReadOnlyList<string> Warnings,
        string? AttributorMaterialPath = null)
    {
        public bool HasOverrides =>
            DiffuseGuid.HasValue ||
            NormalGuid.HasValue ||
            LightmapGuid.HasValue ||
            SpecularGuid.HasValue;
    }

    /// <summary>
    /// Auto-builds texture PSGs for one material context in the GLB.
    /// Channel mapping:
    /// BaseColor -> diffuse, Normal -> normal, Occlusion -> lightmap, MetallicRoughness -> specular.
    /// If <paramref name="materialsJsonPath"/> is provided (or a sidecar .json exists), channel image_path
    /// values are used first and missing/null channels fall back to GLB textures.
    /// </summary>
    public static GlbTextureAutoBuildResult BuildFromGlb(
        string glbPath,
        string outputDirectory,
        bool generateMipMaps = true,
        string? materialsJsonPath = null,
        string? materialNameOverride = null)
    {
        if (string.IsNullOrWhiteSpace(glbPath))
            throw new ArgumentException("GLB path is required.", nameof(glbPath));
        if (!File.Exists(glbPath))
            throw new FileNotFoundException("GLB file not found.", glbPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);

        var built = new List<BuiltTexturePsg>();
        var warnings = new List<string>();
        ulong? diffuseGuid = null;
        ulong? normalGuid = null;
        ulong? lightmapGuid = null;
        ulong? specularGuid = null;

        var model = ModelRoot.Load(glbPath);
        if (model.LogicalMeshes.Count == 0 || model.LogicalMeshes[0].Primitives.Count == 0)
        {
            warnings.Add("GLB has no meshes/primitives; skipped texture build.");
            return new GlbTextureAutoBuildResult(null, null, null, null, built, warnings);
        }

        var prim = model.LogicalMeshes[0].Primitives[0];
        var material = prim.Material ?? model.LogicalMaterials.FirstOrDefault();
        if (material == null)
        {
            warnings.Add("No material found on first primitive; skipped texture build.");
            return new GlbTextureAutoBuildResult(null, null, null, null, built, warnings);
        }

        if (!string.IsNullOrWhiteSpace(materialNameOverride))
        {
            var matchedMaterial = model.LogicalMaterials.FirstOrDefault(
                m => string.Equals(m.Name, materialNameOverride, StringComparison.OrdinalIgnoreCase));
            if (matchedMaterial != null)
                material = matchedMaterial;
        }

        string glbStem = Path.GetFileNameWithoutExtension(glbPath);
        string materialName = !string.IsNullOrWhiteSpace(materialNameOverride)
            ? materialNameOverride!
            : (string.IsNullOrWhiteSpace(material.Name) ? "DefaultMaterial" : material.Name);

        var jsonMaterial = TryResolveJsonMaterialConfig(glbPath, materialsJsonPath, materialName, glbStem, warnings);
        string? attributorMaterialPath = jsonMaterial?.MaterialName;

        var diffuse = TryBuildChannelTexture(
            material,
            glbPath,
            glbStem,
            materialName,
            jsonMaterial,
            jsonChannelName: "diffuse",
            glbChannelName: "BaseColor",
            meshChannelName: "diffuse",
            outputDirectory,
            generateMipMaps,
            out string? diffuseWarning);
        if (diffuseWarning != null) warnings.Add(diffuseWarning);
        if (diffuse != null)
        {
            built.Add(diffuse);
            diffuseGuid = diffuse.Guid;
        }

        var normal = TryBuildChannelTexture(
            material,
            glbPath,
            glbStem,
            materialName,
            jsonMaterial,
            jsonChannelName: "normal",
            glbChannelName: "Normal",
            meshChannelName: "normal",
            outputDirectory,
            generateMipMaps,
            out string? normalWarning);
        if (normalWarning != null) warnings.Add(normalWarning);
        if (normal != null)
        {
            built.Add(normal);
            normalGuid = normal.Guid;
        }

        var lightmap = TryBuildChannelTexture(
            material,
            glbPath,
            glbStem,
            materialName,
            jsonMaterial,
            jsonChannelName: "lightmap",
            glbChannelName: "Occlusion",
            meshChannelName: "lightmap",
            outputDirectory,
            generateMipMaps,
            out string? lightmapWarning);
        if (lightmapWarning != null) warnings.Add(lightmapWarning);
        if (lightmap != null)
        {
            built.Add(lightmap);
            lightmapGuid = lightmap.Guid;
        }

        var specular = TryBuildChannelTexture(
            material,
            glbPath,
            glbStem,
            materialName,
            jsonMaterial,
            jsonChannelName: "specular",
            glbChannelName: "MetallicRoughness",
            meshChannelName: "specular",
            outputDirectory,
            generateMipMaps,
            out string? specularWarning);
        if (specularWarning != null) warnings.Add(specularWarning);
        if (specular != null)
        {
            built.Add(specular);
            specularGuid = specular.Guid;
        }

        return new GlbTextureAutoBuildResult(
            diffuseGuid,
            normalGuid,
            lightmapGuid,
            specularGuid,
            built,
            warnings,
            attributorMaterialPath);
    }

    private static BuiltTexturePsg? TryBuildChannelTexture(
        Material material,
        string glbPath,
        string glbStem,
        string materialName,
        BlenroseMaterialJsonReader.MaterialTextureConfig? jsonMaterial,
        string jsonChannelName,
        string glbChannelName,
        string meshChannelName,
        string outputDirectory,
        bool generateMipMaps,
        out string? warning)
    {
        warning = null;

        // 1) Prefer explicit BlenRose JSON image_path when provided.
        if (TryGetJsonChannelImagePath(jsonMaterial, jsonChannelName, out string? jsonImagePath))
        {
            try
            {
                string resolvedImagePath = ResolveJsonImagePath(jsonImagePath!, jsonMaterial!.SourceJsonPath, glbPath);
                if (File.Exists(resolvedImagePath))
                {
                    return BuildTextureFromFilePath(
                        resolvedImagePath,
                        glbStem,
                        materialName,
                        meshChannelName,
                        outputDirectory,
                        generateMipMaps);
                }

                warning = $"Texture JSON path not found for channel '{meshChannelName}': {jsonImagePath}";
            }
            catch (Exception ex)
            {
                warning = $"Texture JSON image failed for channel '{meshChannelName}': {ex.Message}";
            }
        }

        // 2) Fallback to GLB channel texture (embedded or external glTF references).
        var fromGlb = TryBuildChannelTextureFromGlb(
            material,
            glbStem,
            materialName,
            glbChannelName,
            meshChannelName,
            outputDirectory,
            generateMipMaps,
            out string? glbWarning);

        if (glbWarning != null)
            warning = warning == null ? glbWarning : $"{warning} | GLB fallback: {glbWarning}";

        return fromGlb;
    }

    private static BuiltTexturePsg? TryBuildChannelTextureFromGlb(
        Material material,
        string glbStem,
        string materialName,
        string glbChannelName,
        string meshChannelName,
        string outputDirectory,
        bool generateMipMaps,
        out string? warning)
    {
        warning = null;
        try
        {
            var texture = material.FindChannel(glbChannelName)?.Texture;
            if (texture == null) return null;

            var image = texture.PrimaryImage ?? texture.FallbackImage;
            if (image == null) return null;

            MemoryImage content = image.Content;
            byte[] encodedImageBytes = ReadAllBytes(content);
            bool sourceIsDds = content.IsDds;
            string imageName = ResolveImageName(image, content, meshChannelName);

            return BuildTextureFromEncodedSource(
                encodedImageBytes,
                sourceIsDds,
                imageName,
                glbStem,
                materialName,
                meshChannelName,
                outputDirectory,
                generateMipMaps);
        }
        catch (Exception ex)
        {
            warning = $"Texture build skipped for GLB channel '{meshChannelName}': {ex.Message}";
            return null;
        }
    }

    private static BuiltTexturePsg BuildTextureFromFilePath(
        string imagePath,
        string glbStem,
        string materialName,
        string meshChannelName,
        string outputDirectory,
        bool generateMipMaps)
    {
        byte[] encoded = File.ReadAllBytes(imagePath);
        bool isDds = string.Equals(Path.GetExtension(imagePath), ".dds", StringComparison.OrdinalIgnoreCase);
        string imageName = Path.GetFileNameWithoutExtension(imagePath);
        return BuildTextureFromEncodedSource(
            encoded,
            isDds,
            imageName,
            glbStem,
            materialName,
            meshChannelName,
            outputDirectory,
            generateMipMaps);
    }

    private static BuiltTexturePsg BuildTextureFromEncodedSource(
        byte[] encodedImageBytes,
        bool sourceIsDds,
        string imageName,
        string glbStem,
        string materialName,
        string meshChannelName,
        string outputDirectory,
        bool generateMipMaps)
    {
        byte[] ddsBytes = sourceIsDds
            ? encodedImageBytes
            : ImageToDdsConverter.ConvertToDxt5(encodedImageBytes, generateMipMaps);

        DdsTextureInput ddsInput = DdsReader.Read(ddsBytes);
        string textureKey = TextureGuidStrategy.BuildTextureKey(glbStem, materialName, meshChannelName, imageName);
        ulong textureGuid = TextureGuidStrategy.KeyToGuid(textureKey);
        string outPath = Path.Combine(outputDirectory, $"{textureGuid:X16}.psg");

        var spec = TexturePsgComposer.Compose(ddsInput, textureGuid);
        using (var fs = File.Create(outPath))
            GenericArenaWriter.Write(spec, fs);

        return new BuiltTexturePsg(meshChannelName, imageName, textureGuid, outPath);
    }

    private static BlenroseMaterialJsonReader.MaterialTextureConfig? TryResolveJsonMaterialConfig(
        string glbPath,
        string? materialsJsonPath,
        string materialName,
        string glbStem,
        List<string> warnings)
    {
        string? resolvedJsonPath = ResolveMaterialsJsonPath(glbPath, materialsJsonPath);
        if (resolvedJsonPath == null)
            return null;

        if (!File.Exists(resolvedJsonPath))
        {
            if (!string.IsNullOrWhiteSpace(materialsJsonPath))
                warnings.Add($"Materials JSON not found: {resolvedJsonPath}. Falling back to GLB textures.");
            return null;
        }

        IReadOnlyDictionary<string, BlenroseMaterialJsonReader.MaterialTextureConfig> byName;
        try
        {
            byName = BlenroseMaterialJsonReader.Read(resolvedJsonPath);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to read materials JSON '{resolvedJsonPath}': {ex.Message}. Falling back to GLB textures.");
            return null;
        }

        if (byName.TryGetValue(materialName, out var matched))
            return matched;
        if (byName.TryGetValue(glbStem, out matched))
            return matched;

        // If exactly one unique material exists, use it as a pragmatic fallback.
        var unique = new List<BlenroseMaterialJsonReader.MaterialTextureConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in byName.Values)
        {
            if (seen.Add(candidate.MaterialName))
                unique.Add(candidate);
        }

        if (unique.Count == 1)
        {
            warnings.Add($"Materials JSON had no entry for '{materialName}'. Using sole JSON material '{unique[0].MaterialName}'.");
            return unique[0];
        }

        warnings.Add($"Materials JSON had no entry for '{materialName}'. Using GLB textures for this material.");
        return null;
    }

    private static string? ResolveMaterialsJsonPath(string glbPath, string? materialsJsonPath)
    {
        if (!string.IsNullOrWhiteSpace(materialsJsonPath))
            return Path.GetFullPath(materialsJsonPath);

        string sidecarPath = Path.ChangeExtension(Path.GetFullPath(glbPath), ".json");
        return File.Exists(sidecarPath) ? sidecarPath : null;
    }

    private static bool TryGetJsonChannelImagePath(
        BlenroseMaterialJsonReader.MaterialTextureConfig? jsonMaterial,
        string channelName,
        out string? imagePath)
    {
        imagePath = null;
        if (jsonMaterial == null)
            return false;

        if (!jsonMaterial.ChannelImagePaths.TryGetValue(channelName, out string? rawPath))
            return false;
        if (string.IsNullOrWhiteSpace(rawPath))
            return false;

        imagePath = rawPath;
        return true;
    }

    private static string ResolveJsonImagePath(string rawImagePath, string jsonPath, string glbPath)
    {
        string trimmed = rawImagePath.Trim();
        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed);

        string jsonDir = Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? ".";
        string fromJsonDir = Path.GetFullPath(Path.Combine(jsonDir, trimmed));
        if (File.Exists(fromJsonDir))
            return fromJsonDir;

        string glbDir = Path.GetDirectoryName(Path.GetFullPath(glbPath)) ?? ".";
        string fromGlbDir = Path.GetFullPath(Path.Combine(glbDir, trimmed));
        if (File.Exists(fromGlbDir))
            return fromGlbDir;

        return fromJsonDir;
    }

    private static byte[] ReadAllBytes(MemoryImage memoryImage)
    {
        using var stream = memoryImage.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string ResolveImageName(Image image, MemoryImage content, string fallbackChannelName)
    {
        if (!string.IsNullOrWhiteSpace(image.Name))
            return image.Name!;

        if (!string.IsNullOrWhiteSpace(content.SourcePath))
            return Path.GetFileNameWithoutExtension(content.SourcePath);

        return fallbackChannelName;
    }
}

