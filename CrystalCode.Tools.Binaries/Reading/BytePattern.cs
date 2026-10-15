using System.Globalization;
using System.Text;

namespace CrystalCode.Tools.Binaries.Reading;

/// <summary>
/// One literal byte pattern parsed from model arguments.
/// </summary>
internal sealed class BytePattern
{
    private BytePattern(byte[] bytes, string description)
    {
        Bytes = bytes;
        Description = description;
    }

    public byte[] Bytes { get; }

    public string Description { get; }

    public static bool TryParse(
        string encoding,
        string pattern,
        out BytePattern? value,
        out string? error)
    {
        value = null;
        error = null;
        byte[] bytes;
        switch (encoding)
        {
            case "hex":
                if (!TryParseHex(pattern, out bytes, out error))
                {
                    return false;
                }

                break;
            case "ascii":
                if (!TryParseAscii(pattern, out bytes, out error))
                {
                    return false;
                }

                break;
            case "utf8":
                bytes = Encoding.UTF8.GetBytes(pattern);
                break;
            case "utf16le":
                bytes = Encoding.Unicode.GetBytes(pattern);
                break;
            default:
                error = "Argument 'encoding' must be hex, ascii, utf8, or utf16le.";
                return false;
        }

        if (bytes.Length > BinaryLimits.MaximumPatternBytes)
        {
            error = $"The pattern is {bytes.Length} bytes; "
                + $"the maximum is {BinaryLimits.MaximumPatternBytes} bytes.";
            return false;
        }

        value = new BytePattern(bytes, $"{bytes.Length}-byte {encoding} pattern");
        return true;
    }

    private static bool TryParseHex(string pattern, out byte[] bytes, out string? error)
    {
        bytes = [];
        error = null;
        var digits = new StringBuilder(pattern.Length);
        foreach (var character in pattern)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            if (!IsHexDigit(character))
            {
                error = "The hex pattern must contain only hex digits and whitespace, "
                    + "for example '89 50 4E 47'.";
                return false;
            }

            digits.Append(character);
        }

        if (digits.Length == 0)
        {
            error = "The hex pattern must contain at least one byte.";
            return false;
        }

        if (digits.Length % 2 != 0)
        {
            error = "The hex pattern must contain an even number of hex digits.";
            return false;
        }

        bytes = new byte[digits.Length / 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = byte.Parse(
                digits.ToString(index * 2, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
        }

        return true;
    }

    private static bool TryParseAscii(string pattern, out byte[] bytes, out string? error)
    {
        bytes = [];
        error = null;
        foreach (var character in pattern)
        {
            if (character > 0x7F)
            {
                error = "The ascii pattern must contain only ASCII characters; "
                    + "use the utf8 or utf16le encoding for other text.";
                return false;
            }
        }

        bytes = Encoding.ASCII.GetBytes(pattern);
        return true;
    }

    private static bool IsHexDigit(char character) =>
        character is >= '0' and <= '9'
        or >= 'a' and <= 'f'
        or >= 'A' and <= 'F';
}
