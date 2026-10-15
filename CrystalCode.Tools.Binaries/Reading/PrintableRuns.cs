using System.Text;

namespace CrystalCode.Tools.Binaries.Reading;

/// <summary>
/// Scans a byte window for printable runs in one encoding. ASCII runs cover
/// printable ASCII bytes; UTF-16LE runs cover code units that are not
/// control characters. Scanning stops when the run limit is reached and the
/// next qualifying run proves that more runs exist.
/// </summary>
internal static class PrintableRuns
{
    public static async Task<PrintableScan> ScanAsync(
        Stream stream,
        long windowStart,
        long windowLength,
        int minimumLength,
        bool utf16le,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(windowLength);
        var runs = new List<PrintableRun>();
        var buffer = new byte[BinaryLimits.BufferBytes];
        var current = new StringBuilder();
        var currentStart = windowStart;
        var more = false;
        var stopped = false;

        bool TryFlush()
        {
            if (current.Length == 0)
            {
                return false;
            }

            var text = current.ToString();
            current.Clear();
            if (text.Length < minimumLength)
            {
                return false;
            }

            if (runs.Count == limit)
            {
                more = true;
                return true;
            }

            var byteLength = utf16le ? (long)text.Length * 2 : text.Length;
            runs.Add(new PrintableRun(currentStart, byteLength, text));
            return false;
        }

        long remaining = windowLength;
        if (utf16le)
        {
            long nextUnitOffset = windowStart;
            var hasPending = false;
            var pending = (byte)0;
            while (!stopped && remaining > 0)
            {
                var want = (int)Math.Min(BinaryLimits.BufferBytes, remaining);
                var read = await BinaryFile
                    .ReadAtMostAsync(stream, buffer.AsMemory(0, want), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                remaining -= read;
                var span = buffer.AsSpan(0, read);
                var index = 0;
                if (hasPending)
                {
                    var code = (char)(pending | (span[0] << 8));
                    if (char.IsControl(code))
                    {
                        stopped = TryFlush();
                    }
                    else
                    {
                        if (current.Length == 0)
                        {
                            currentStart = nextUnitOffset;
                        }

                        current.Append(code);
                    }

                    nextUnitOffset += 2;
                    index = 1;
                    hasPending = false;
                }

                while (!stopped && index + 2 <= read)
                {
                    var code = (char)(span[index] | (span[index + 1] << 8));
                    if (char.IsControl(code))
                    {
                        stopped = TryFlush();
                    }
                    else
                    {
                        if (current.Length == 0)
                        {
                            currentStart = nextUnitOffset;
                        }

                        current.Append(code);
                    }

                    nextUnitOffset += 2;
                    index += 2;
                }

                if (!stopped && index < read)
                {
                    pending = span[index];
                    hasPending = true;
                }
            }
        }
        else
        {
            long chunkStart = windowStart;
            while (!stopped && remaining > 0)
            {
                var want = (int)Math.Min(BinaryLimits.BufferBytes, remaining);
                var read = await BinaryFile
                    .ReadAtMostAsync(stream, buffer.AsMemory(0, want), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                remaining -= read;
                var span = buffer.AsSpan(0, read);
                for (var index = 0; index < read && !stopped; index++)
                {
                    var value = span[index];
                    if (value is >= 0x20 and <= 0x7E)
                    {
                        if (current.Length == 0)
                        {
                            currentStart = chunkStart + index;
                        }

                        current.Append((char)value);
                    }
                    else
                    {
                        stopped = TryFlush();
                    }
                }

                chunkStart += read;
            }
        }

        if (!stopped)
        {
            TryFlush();
        }

        return new PrintableScan(runs, more);
    }
}
