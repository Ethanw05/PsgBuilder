namespace PsgBuilder.Collision;

/// <summary>
/// SurfaceID encode/decode. Exact port of Collision_Export_Dumbad_Tuukkas_original.py lines 51-79.
/// Used by domain/serialization when writing triangle surface IDs.
/// </summary>
public static class SurfaceIdHelper
{
    /// <summary>Encode SurfaceID from component bitfields. audio 0-127, physics 0-31, pattern 0-15.</summary>
    public static int EncodeSurfaceId(int audio, int physics, int pattern)
    {
        return (audio & 0x7F) | ((physics & 0x1F) << 7) | ((pattern & 0x0F) << 12);
    }

    /// <summary>Decode SurfaceID into (audio, physics, pattern).</summary>
    public static (int Audio, int Physics, int Pattern) DecodeSurfaceId(int surfaceId)
    {
        int audio = surfaceId & 0x7F;
        int physics = (surfaceId >> 7) & 0x1F;
        int pattern = (surfaceId >> 12) & 0x0F;
        return (audio, physics, pattern);
    }
}
