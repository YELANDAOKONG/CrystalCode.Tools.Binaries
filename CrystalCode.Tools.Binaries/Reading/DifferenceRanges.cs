using System.Text;

namespace CrystalCode.Tools.Binaries.Reading;

/// <summary>
/// Streams aligned differences without retaining whole differing regions.
/// </summary>
internal static class DifferenceRanges
{
    private const int PreviewBytes = 16;

    public static async Task<string> ReadAsync(
        Stream first, Stream second, long offset, long? length, int limit,
        CancellationToken cancellationToken)
    {
        var size = Math.Max(first.Length, second.Length);
        var remaining = length is null ? size - offset : Math.Min(length.Value, size - offset);
        first.Seek(Math.Min(offset, first.Length), SeekOrigin.Begin);
        second.Seek(Math.Min(offset, second.Length), SeekOrigin.Begin);
        var firstBuffer = new byte[BinaryLimits.BufferBytes];
        var secondBuffer = new byte[BinaryLimits.BufferBytes];
        var firstPreview = new List<byte>(PreviewBytes);
        var secondPreview = new List<byte>(PreviewBytes);
        var builder = new StringBuilder();
        var position = offset;
        var start = -1L;
        var listed = 0;
        var continueAt = offset;
        long firstCount = 0;
        long secondCount = 0;

        void Flush()
        {
            if (start < 0)
            {
                return;
            }

            BinaryText.AppendLine(builder, $"offset {start}: {position - start} differing bytes");
            BinaryText.AppendLine(builder, $"  first ({firstCount} bytes): {Preview(firstPreview, firstCount)}");
            BinaryText.AppendLine(builder, $"  second ({secondCount} bytes): {Preview(secondPreview, secondCount)}");
            listed++;
            continueAt = position;
            start = -1;
            firstCount = 0;
            secondCount = 0;
            firstPreview.Clear();
            secondPreview.Clear();
        }

        while (remaining > 0)
        {
            var want = (int)Math.Min(remaining, BinaryLimits.BufferBytes);
            var firstRead = await BinaryFile.ReadAtMostAsync(first, firstBuffer.AsMemory(0, want), cancellationToken)
                .ConfigureAwait(false);
            var secondRead = await BinaryFile.ReadAtMostAsync(second, secondBuffer.AsMemory(0, want), cancellationToken)
                .ConfigureAwait(false);
            var read = Math.Max(firstRead, secondRead);
            if (read == 0)
            {
                break;
            }

            for (var index = 0; index < read; index++, position++)
            {
                var different = index >= firstRead || index >= secondRead || firstBuffer[index] != secondBuffer[index];
                if (!different)
                {
                    Flush();
                    continue;
                }

                if (listed == limit)
                {
                    BinaryText.AppendLine(builder, $"[showing {listed} ranges; continue with offset {continueAt}]");
                    return BinaryText.Finish(builder);
                }

                if (start < 0)
                {
                    start = position;
                }

                if (index < firstRead)
                {
                    firstCount++;
                    if (firstPreview.Count < PreviewBytes)
                    {
                        firstPreview.Add(firstBuffer[index]);
                    }
                }

                if (index < secondRead)
                {
                    secondCount++;
                    if (secondPreview.Count < PreviewBytes)
                    {
                        secondPreview.Add(secondBuffer[index]);
                    }
                }
            }

            remaining -= read;
        }

        Flush();
        return listed == 0 ? "No differences in the requested window." : BinaryText.Finish(builder);
    }

    private static string Preview(List<byte> bytes, long count) =>
        count == 0 ? "<absent>" : Convert.ToHexStringLower(bytes.ToArray()) + (count > bytes.Count ? " ..." : string.Empty);
}
