using System.Buffers.Binary;

namespace PsgBuilder.Core.Rw;

/// <summary>
/// Volume AGGREGATE RW object. Port of Python _build_volume.
/// </summary>
public static class VolumeRwBuilder
{
    public static byte[] Build()
    {
        var blob = new byte[0x60];
        var s = blob.AsSpan();
        float[] identity =
        {
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 0f
        };
        for (int i = 0; i < 16; i++)
            BinaryPrimitives.WriteInt32BigEndian(s.Slice(i * 4, 4), BitConverter.SingleToInt32Bits(identity[i]));
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x40, 4), 6);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x44, 4), 3);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x48, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x4C, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(s.Slice(0x50, 4), BitConverter.SingleToInt32Bits(0f));
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x54, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x58, 4), 0xFFFFFFFF);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0x5C, 4), 1);
        return blob;
    }
}

