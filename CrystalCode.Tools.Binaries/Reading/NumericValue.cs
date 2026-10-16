using System.Buffers.Binary;
using System.Globalization;

namespace CrystalCode.Tools.Binaries.Reading;

/// <summary>
/// Decodes fixed-width numeric values without interpreting their meaning.
/// </summary>
internal static class NumericValue
{
    public static int Width(string type) => type switch
    {
        "u8" or "i8" => 1,
        "u16" or "i16" => 2,
        "u32" or "i32" or "f32" => 4,
        "u64" or "i64" or "f64" => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(type), "Unsupported numeric type.")
    };

    public static string Decode(ReadOnlySpan<byte> bytes, string type, bool bigEndian)
    {
        Span<byte> value = stackalloc byte[8];
        bytes.CopyTo(value);
        if (bigEndian)
        {
            value[..bytes.Length].Reverse();
        }

        var culture = CultureInfo.InvariantCulture;
        return type switch
        {
            "u8" => value[0].ToString(culture),
            "i8" => unchecked((sbyte)value[0]).ToString(culture),
            "u16" => BinaryPrimitives.ReadUInt16LittleEndian(value).ToString(culture),
            "i16" => BinaryPrimitives.ReadInt16LittleEndian(value).ToString(culture),
            "u32" => BinaryPrimitives.ReadUInt32LittleEndian(value).ToString(culture),
            "i32" => BinaryPrimitives.ReadInt32LittleEndian(value).ToString(culture),
            "u64" => BinaryPrimitives.ReadUInt64LittleEndian(value).ToString(culture),
            "i64" => BinaryPrimitives.ReadInt64LittleEndian(value).ToString(culture),
            "f32" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(value)).ToString("R", culture),
            "f64" => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(value)).ToString("R", culture),
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Unsupported numeric type.")
        };
    }
}
