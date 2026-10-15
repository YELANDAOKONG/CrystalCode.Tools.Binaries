namespace CrystalCode.Tools.Binaries.Reading;

/// <summary>
/// Describes how the first bytes of a file look to a reader.
/// </summary>
internal static class TextProbe
{
    public static string Describe(ReadOnlySpan<byte> probe)
    {
        if (probe.Length == 0)
        {
            return "empty";
        }

        if (StartsWith(probe, 0xEF, 0xBB, 0xBF))
        {
            return "text with a UTF-8 BOM";
        }

        if (StartsWith(probe, 0xFF, 0xFE))
        {
            return "text with a UTF-16LE BOM";
        }

        if (StartsWith(probe, 0xFE, 0xFF))
        {
            return "text with a UTF-16BE BOM";
        }

        var nulAtEven = false;
        var nulAtOdd = false;
        for (var index = 0; index < probe.Length; index++)
        {
            if (probe[index] != 0)
            {
                continue;
            }

            if (index % 2 == 0)
            {
                nulAtEven = true;
            }
            else
            {
                nulAtOdd = true;
            }
        }

        if (!nulAtEven && !nulAtOdd)
        {
            return "text (no BOM)";
        }

        if (probe.Length >= 2 && nulAtOdd && !nulAtEven)
        {
            return "likely UTF-16LE text";
        }

        if (probe.Length >= 2 && nulAtEven && !nulAtOdd)
        {
            return "likely UTF-16BE text";
        }

        return "binary";
    }

    private static bool StartsWith(ReadOnlySpan<byte> probe, byte first, byte second, byte third) =>
        probe.Length >= 3 && probe[0] == first && probe[1] == second && probe[2] == third;

    private static bool StartsWith(ReadOnlySpan<byte> probe, byte first, byte second) =>
        probe.Length >= 2 && probe[0] == first && probe[1] == second;
}
