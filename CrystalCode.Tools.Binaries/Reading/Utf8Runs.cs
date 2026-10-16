using System.Buffers;
using System.Globalization;
using System.Text;

namespace CrystalCode.Tools.Binaries.Reading;

/// <summary>
/// Finds valid UTF-8 runs; lengths count Unicode scalar values and offsets count bytes.
/// </summary>
internal static class Utf8Runs
{
    public static async Task<PrintableScan> ScanAsync(
        Stream stream, long offset, long length, int minimumLength, int limit,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BinaryLimits.BufferBytes + 4];
        var runs = new List<PrintableRun>();
        var preview = new StringBuilder();
        var carry = 0;
        var position = offset;
        var start = offset;
        long characters = 0;
        long byteLength = 0;
        var more = false;

        bool Flush()
        {
            if (characters >= minimumLength)
            {
                if (runs.Count == limit)
                {
                    more = true;
                    return true;
                }

                runs.Add(new PrintableRun(start, byteLength, preview.ToString()));
            }

            preview.Clear();
            characters = 0;
            byteLength = 0;
            return false;
        }

        while (length > 0 || carry > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var want = (int)Math.Min(BinaryLimits.BufferBytes, length);
            var read = await BinaryFile.ReadAtMostAsync(stream, buffer.AsMemory(carry, want), cancellationToken)
                .ConfigureAwait(false);
            length -= read;
            var available = carry + read;
            var index = 0;
            while (index < available)
            {
                var status = Rune.DecodeFromUtf8(buffer.AsSpan(index, available - index), out var rune, out var consumed);
                if (status == OperationStatus.NeedMoreData && length > 0 && read > 0)
                {
                    break;
                }

                var category = status == OperationStatus.Done ? Rune.GetUnicodeCategory(rune) : UnicodeCategory.Control;
                if (status != OperationStatus.Done || category is UnicodeCategory.Control or UnicodeCategory.Format
                    or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.OtherNotAssigned)
                {
                    if (Flush())
                    {
                        return new PrintableScan(runs, more);
                    }

                    consumed = status == OperationStatus.Done ? consumed : 1;
                }
                else
                {
                    if (characters == 0)
                    {
                        start = position;
                    }

                    if (preview.Length <= BinaryLimits.MaximumStringCharacters)
                    {
                        preview.Append(rune.ToString());
                    }

                    characters++;
                    byteLength += consumed;
                }

                index += consumed;
                position += consumed;
            }

            carry = available - index;
            buffer.AsSpan(index, carry).CopyTo(buffer);
            if (read == 0)
            {
                length = 0;
            }
        }

        Flush();
        return new PrintableScan(runs, more);
    }
}
