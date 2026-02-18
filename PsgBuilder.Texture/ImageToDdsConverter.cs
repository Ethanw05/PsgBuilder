using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PsgBuilder.Texture;

/// <summary>
/// Converts encoded raster images (PNG/JPG) to DDS (DXT5 / BC3).
/// </summary>
public static class ImageToDdsConverter
{
    /// <summary>
    /// Converts image bytes to DDS using BC3 (DXT5).
    /// </summary>
    public static byte[] ConvertToDxt5(byte[] encodedImageBytes, bool generateMipMaps = true)
    {
        if (encodedImageBytes == null || encodedImageBytes.Length == 0)
            throw new ArgumentException("Image bytes are required.", nameof(encodedImageBytes));

        using var image = Image.Load<Rgba32>(encodedImageBytes);
        var encoder = new BcEncoder();
        encoder.OutputOptions.Format = CompressionFormat.Bc3; // DXT5
        encoder.OutputOptions.FileFormat = OutputFileFormat.Dds;
        encoder.OutputOptions.GenerateMipMaps = generateMipMaps;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;

        using var ms = new MemoryStream();
        encoder.EncodeToStream(image, ms);
        return ms.ToArray();
    }
}

