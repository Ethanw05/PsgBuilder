using System.Buffers.Binary;

namespace PsgBuilder.Core.Rw;

/// <summary>CollisionModelData RW object. Port of Python _build_collision_model.</summary>
public static class CollisionModelDataRwBuilder
{
    public static byte[] Build()
    {
        var blob = new byte[20];
        var s = blob.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(0, 4), 0);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(4, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(8, 4), 0x0C);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(12, 4), 2);
        BinaryPrimitives.WriteUInt32BigEndian(s.Slice(16, 4), 0);
        return blob;
    }
}

