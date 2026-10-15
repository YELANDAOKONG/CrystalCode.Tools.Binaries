namespace CrystalCode.Tools.Binaries.Reading;

/// <summary>
/// Streams two files side by side and counts their differences.
/// </summary>
internal static class FileCompare
{
    public static async Task<FileComparison> CompareAsync(
        Stream first,
        Stream second,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var firstBuffer = new byte[BinaryLimits.BufferBytes];
        var secondBuffer = new byte[BinaryLimits.BufferBytes];
        long firstBytes = 0;
        long secondBytes = 0;
        long commonOffset = 0;
        long differing = 0;
        var firstDifference = -1L;
        var firstValue = (byte)0;
        var secondValue = (byte)0;
        while (true)
        {
            var readFirst = await BinaryFile
                .ReadAtMostAsync(first, firstBuffer, cancellationToken)
                .ConfigureAwait(false);
            var readSecond = await BinaryFile
                .ReadAtMostAsync(second, secondBuffer, cancellationToken)
                .ConfigureAwait(false);
            firstBytes += readFirst;
            secondBytes += readSecond;
            var common = Math.Min(readFirst, readSecond);
            if (common > 0)
            {
                var spanFirst = firstBuffer.AsSpan(0, common);
                var spanSecond = secondBuffer.AsSpan(0, common);
                if (!spanFirst.SequenceEqual(spanSecond))
                {
                    for (var index = 0; index < common; index++)
                    {
                        if (spanFirst[index] == spanSecond[index])
                        {
                            continue;
                        }

                        differing++;
                        if (firstDifference < 0)
                        {
                            firstDifference = commonOffset + index;
                            firstValue = spanFirst[index];
                            secondValue = spanSecond[index];
                        }
                    }
                }

                commonOffset += common;
            }

            if (readFirst == 0 && readSecond == 0)
            {
                break;
            }
        }

        return new FileComparison(
            firstBytes,
            secondBytes,
            firstDifference,
            firstValue,
            secondValue,
            differing);
    }
}
