using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace PsgBuilder.Collision.IO;

/// <summary>
/// Big-endian write helpers for building byte buffers. PSG format is big-endian.
/// Ported semantics from Collision_Export_Dumbad_Tuukkas_original.py (be_u32, be_f32, be_u16, be_u8, etc.).
/// </summary>
public static class EndianHelpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBeU32(Span<byte> dest, uint value) => BinaryPrimitives.WriteUInt32BigEndian(dest, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBeI32(Span<byte> dest, int value) => BinaryPrimitives.WriteInt32BigEndian(dest, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBeU16(Span<byte> dest, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(dest, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBeI16(Span<byte> dest, short value) => BinaryPrimitives.WriteInt16BigEndian(dest, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBeU8(Span<byte> dest, byte value) => dest[0] = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBeF32(Span<byte> dest, float value) =>
        BinaryPrimitives.WriteInt32BigEndian(dest, BitConverter.SingleToInt32Bits(value));

    /// <summary>Little-endian u16 (used for SurfaceID in cluster unit stream only). Python le_u16.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteLeU16(Span<byte> dest, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(dest, value);
}
