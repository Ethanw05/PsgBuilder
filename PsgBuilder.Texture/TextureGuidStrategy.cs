using System.Text;
using PsgBuilder.Core;

namespace PsgBuilder.Texture;

/// <summary>
/// Canonical texture key and GUID derivation so mesh material channels and texture TOC
/// entries use the same GUID (per PSG_STRUCTURE_CONNECTIONS).
/// </summary>
public static class TextureGuidStrategy
{
    /// <summary>
    /// Builds a stable, collision-resistant texture key from GLB/material binding context.
    /// Format: "&lt;glbFileStem&gt;/&lt;materialName&gt;/&lt;channelName&gt;/&lt;imageName&gt;".
    /// All components are normalized to ASCII for Lookup8Hash.
    /// </summary>
    /// <param name="glbFileStem">GLB filename without extension (e.g. "MyLevel").</param>
    /// <param name="materialName">Material name in the asset (e.g. "Material0").</param>
    /// <param name="channelName">Shader channel (e.g. "diffuse", "normal").</param>
    /// <param name="imageName">Image/sampler name or embedded image key (e.g. "tex_0", "image0").</param>
    /// <returns>Canonical key string; use <see cref="KeyToGuid"/> to get the 64-bit GUID.</returns>
    public static string BuildTextureKey(
        string? glbFileStem,
        string? materialName,
        string? channelName,
        string? imageName)
    {
        string a = NormalizeToAscii(glbFileStem ?? "");
        string b = NormalizeToAscii(materialName ?? "");
        string c = NormalizeToAscii(channelName ?? "");
        string d = NormalizeToAscii(imageName ?? "");
        return $"{a}/{b}/{c}/{d}";
    }

    /// <summary>
    /// Computes the 64-bit texture GUID from the canonical key.
    /// Same value must be used in texture PSG TOC entry and in mesh RenderMaterialData channel.
    /// </summary>
    public static ulong KeyToGuid(string textureKey)
    {
        if (string.IsNullOrEmpty(textureKey))
            return 0;
        return Lookup8Hash.HashString(textureKey);
    }

    /// <summary>
    /// TOC name string observed in real texture PSGs: "0x&lt;guidLowerHex&gt;.Texture" or "0x...T".
    /// Dumps show "0x054a480902fd9a8d.Texture" (template) and "0x2c70170a000d002a.Texture" (real);
    /// some use ".T" suffix. We use ".Texture" for readability; game looks up by GUID.
    /// </summary>
    public static string GuidToTocNameString(ulong guid)
    {
        return $"0x{guid:x16}.Texture";
    }

    /// <summary>
    /// Normalizes a string to ASCII so Lookup8Hash.HashString does not throw.
    /// Non-ASCII characters are replaced with '?'.
    /// </summary>
    private static string NormalizeToAscii(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
            sb.Append(c <= 0x7F ? c : '?');
        return sb.ToString();
    }
}
