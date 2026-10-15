using System.Globalization;
using System.Text;

namespace CrystalCode.Tools.Binaries.Reading;

/// <summary>
/// Formats a byte window as classic hex and ASCII rows.
/// </summary>
internal static class HexRows
{
    public static void Append(StringBuilder builder, ReadOnlySpan<byte> bytes, long baseOffset)
    {
        ArgumentNullException.ThrowIfNull(builder);
        for (var start = 0; start < bytes.Length; start += BinaryLimits.HexDumpBytesPerRow)
        {
            var count = Math.Min(BinaryLimits.HexDumpBytesPerRow, bytes.Length - start);
            var row = bytes.Slice(start, count);
            builder.Append((baseOffset + start).ToString("x8", CultureInfo.InvariantCulture));
            builder.Append("  ");
            for (var index = 0; index < BinaryLimits.HexDumpBytesPerRow; index++)
            {
                if (index == BinaryLimits.HexDumpBytesPerRow / 2)
                {
                    builder.Append(' ');
                }

                if (index < count)
                {
                    builder.Append(row[index].ToString("x2", CultureInfo.InvariantCulture)).Append(' ');
                }
                else
                {
                    builder.Append("   ");
                }
            }

            builder.Append(" |");
            for (var index = 0; index < count; index++)
            {
                var value = row[index];
                builder.Append(value is >= 0x20 and <= 0x7E ? (char)value : '.');
            }

            builder.Append('|');
            BinaryText.AppendLine(builder, string.Empty);
        }
    }
}
