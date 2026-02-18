using System.Text.Json;

namespace PsgBuilder.Texture;

/// <summary>
/// Reads BlenRose material JSON and exposes per-material texture image paths.
/// Expected shape:
/// {
///   "MaterialKey": {
///     "material_name": "MaterialName",
///     "textures": {
///       "diffuse": { "image_path": "..." },
///       "normal":  { "image_path": "..." }
///     }
///   }
/// }
/// </summary>
internal static class BlenroseMaterialJsonReader
{
    internal sealed record MaterialTextureConfig(
        string MaterialName,
        string SourceJsonPath,
        IReadOnlyDictionary<string, string?> ChannelImagePaths);

    public static IReadOnlyDictionary<string, MaterialTextureConfig> Read(string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
            throw new ArgumentException("JSON path is required.", nameof(jsonPath));
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("JSON file not found.", jsonPath);

        using var fs = File.OpenRead(jsonPath);
        using var doc = JsonDocument.Parse(fs);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Materials JSON root must be an object.");

        string sourceJsonPath = Path.GetFullPath(jsonPath);
        var byName = new Dictionary<string, MaterialTextureConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var materialProp in doc.RootElement.EnumerateObject())
        {
            if (materialProp.Value.ValueKind != JsonValueKind.Object)
                continue;

            string materialName = materialProp.Name;
            if (materialProp.Value.TryGetProperty("material_name", out var materialNameEl) &&
                materialNameEl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(materialNameEl.GetString()))
            {
                materialName = materialNameEl.GetString()!;
            }

            var channelImagePaths = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (materialProp.Value.TryGetProperty("textures", out var texturesEl) &&
                texturesEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var textureProp in texturesEl.EnumerateObject())
                {
                    string? imagePath = null;
                    if (textureProp.Value.ValueKind == JsonValueKind.Object &&
                        textureProp.Value.TryGetProperty("image_path", out var imagePathEl) &&
                        imagePathEl.ValueKind == JsonValueKind.String)
                    {
                        imagePath = imagePathEl.GetString();
                    }

                    channelImagePaths[textureProp.Name] = string.IsNullOrWhiteSpace(imagePath) ? null : imagePath;
                }
            }

            var config = new MaterialTextureConfig(materialName, sourceJsonPath, channelImagePaths);
            byName[materialProp.Name] = config;
            byName[materialName] = config;
        }

        return byName;
    }
}
