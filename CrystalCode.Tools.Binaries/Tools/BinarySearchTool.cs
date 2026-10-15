using System.Globalization;
using System.Text;
using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Tools.Binaries.Reading;

namespace CrystalCode.Tools.Binaries.Tools;

/// <summary>
/// Finds literal byte patterns in a file and returns their offsets.
/// </summary>
public sealed class BinarySearchTool : ITool
{
    private const string ToolName = "binsearch";

    private const string ToolDescription =
        "Finds literal byte patterns in a file and returns their offsets. "
        + "encoding is 'hex' (default) for a hex string such as '89 50 4E 47', "
        + "or 'ascii', 'utf8', or 'utf16le' for literal text. "
        + "offset and length bound the scan window in bytes (default: the whole file). "
        + "limit caps matches (1-1000, default 100) and the result notes where to continue. "
        + "context shows up to 64 bytes before and after each match. "
        + "Overlapping matches are reported. "
        + "No matches answers 'No matches.'. "
        + "Paths inside the workspace run without asking. "
        + "Paths outside the workspace require approval.";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "Workspace-relative or absolute file path."
            },
            "pattern": {
              "type": "string",
              "description": "The bytes to find: a hex string such as '89 50 4E 47', or literal text for the ascii, utf8, and utf16le encodings."
            },
            "encoding": {
              "type": "string",
              "enum": ["hex", "ascii", "utf8", "utf16le"],
              "description": "How pattern is interpreted (default hex)."
            },
            "offset": {
              "type": "integer",
              "minimum": 0,
              "description": "Byte offset to start scanning at (default 0)."
            },
            "length": {
              "type": "integer",
              "minimum": 1,
              "description": "Bytes to scan from offset (default: to the end of the file)."
            },
            "limit": {
              "type": "integer",
              "minimum": 1,
              "maximum": 1000,
              "description": "Maximum matches to return (1-1000, default 100)."
            },
            "context": {
              "type": "integer",
              "minimum": 0,
              "maximum": 64,
              "description": "Bytes to show before and after each match (0-64, default 0)."
            }
          },
          "required": ["path", "pattern"],
          "additionalProperties": false
        }
        """;

    private static readonly ToolDefinition Tool = CreateTool();

    /// <inheritdoc />
    public ToolDefinition Definition => Tool;

    /// <inheritdoc />
    public async ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        cancellationToken.ThrowIfCancellationRequested();
        if (!BinaryArguments.TryOpen(call.Arguments, out var root, out var document, out var error))
        {
            return BinaryToolResult.Failure(error!);
        }

        using (document)
        {
            if (!BinaryArguments.TryRejectUnknown(
                    root,
                    ["path", "pattern", "encoding", "offset", "length", "limit", "context"],
                    out error)
                || !BinaryArguments.TryReadRequiredString(root, "path", out var path, out error)
                || !BinaryArguments.TryReadRequiredString(root, "pattern", out var patternText, out error)
                || !BinaryArguments.TryReadOptionalChoice(
                    root,
                    "encoding",
                    ["hex", "ascii", "utf8", "utf16le"],
                    out var encoding,
                    out error)
                || !BinaryArguments.TryReadOptionalInteger(
                    root,
                    "offset",
                    0,
                    long.MaxValue,
                    out var offset,
                    out error)
                || !BinaryArguments.TryReadOptionalInteger(
                    root,
                    "length",
                    1,
                    long.MaxValue,
                    out var length,
                    out error)
                || !BinaryArguments.TryReadOptionalInteger(
                    root,
                    "limit",
                    1,
                    BinaryLimits.MaximumSearchLimit,
                    out var limit,
                    out error)
                || !BinaryArguments.TryReadOptionalInteger(
                    root,
                    "context",
                    0,
                    BinaryLimits.MaximumSearchContextBytes,
                    out var context,
                    out error))
            {
                return BinaryToolResult.Failure(error!);
            }

            if (!BytePattern.TryParse(encoding ?? "hex", patternText, out var pattern, out error))
            {
                return BinaryToolResult.Failure(error!);
            }

            return await SearchAsync(
                    path,
                    pattern!,
                    offset ?? 0,
                    length,
                    (int)(limit ?? BinaryLimits.DefaultSearchLimit),
                    (int)(context ?? 0),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<ToolOutput> SearchAsync(
        string path,
        BytePattern pattern,
        long offset,
        long? length,
        int limit,
        int context,
        CancellationToken cancellationToken)
    {
        if (BinaryFile.Validate(path) is { } validationError)
        {
            return BinaryToolResult.Failure(validationError);
        }

        try
        {
            await using var stream = BinaryFile.Open(path);
            var size = stream.Length;
            if (size == 0)
            {
                return offset > 0
                    ? BinaryToolResult.Failure(PastEnd(path, size, offset))
                    : new ToolOutput($"{path}: 0 bytes (empty file)");
            }

            if (offset > size)
            {
                return BinaryToolResult.Failure(PastEnd(path, size, offset));
            }

            var windowEnd = length is null ? size : Math.Min(size, offset + length.Value);
            stream.Seek(offset, SeekOrigin.Begin);
            var (hits, more) = await FindAsync(
                    stream,
                    offset,
                    windowEnd - offset,
                    pattern.Bytes,
                    limit,
                    cancellationToken)
                .ConfigureAwait(false);
            if (hits.Count == 0)
            {
                return new ToolOutput("No matches.");
            }

            var builder = new StringBuilder();
            BinaryText.AppendLine(builder, $"{path}: {size} bytes");
            BinaryText.AppendLine(
                builder,
                more
                    ? $"at least {limit} matches for a {pattern.Description}"
                    : hits.Count == 1
                        ? $"1 match for a {pattern.Description}"
                        : $"{hits.Count} matches for a {pattern.Description}");
            var listed = 0;
            var continueAt = offset;
            var truncated = false;
            foreach (var hit in hits)
            {
                var block = new StringBuilder();
                BinaryText.AppendLine(block, $"offset {hit}: {HexPreview(pattern.Bytes)}");
                if (context > 0)
                {
                    await AppendContextAsync(
                            block,
                            stream,
                            size,
                            hit,
                            pattern.Bytes.Length,
                            context,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                var blockText = BinaryText.Finish(block);
                if (listed > 0
                    && builder.Length + blockText.Length + 1 > BinaryLimits.MaximumResultCharacters)
                {
                    truncated = true;
                    break;
                }

                builder.Append(blockText).Append('\n');
                listed++;
                continueAt = hit + 1;
            }

            if (more || truncated)
            {
                BinaryText.AppendLine(
                    builder,
                    $"[showing {listed} matches; continue with offset {continueAt}]");
            }

            return new ToolOutput(BinaryText.Finish(builder));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BinaryToolResult.Failure($"The file could not be read: {path}.");
        }
    }

    private static async Task<(List<long> Hits, bool More)> FindAsync(
        Stream stream,
        long windowStart,
        long windowLength,
        byte[] pattern,
        int limit,
        CancellationToken cancellationToken)
    {
        var hits = new List<long>();
        var more = false;
        var maxCarry = pattern.Length - 1;
        var combined = new byte[maxCarry + BinaryLimits.BufferBytes];
        var carryLength = 0;
        var carryStart = windowStart;
        long remaining = windowLength;
        while (remaining > 0)
        {
            var want = (int)Math.Min(BinaryLimits.BufferBytes, remaining);
            var read = await BinaryFile
                .ReadAtMostAsync(stream, combined.AsMemory(carryLength, want), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            remaining -= read;
            var combinedLength = carryLength + read;
            var span = combined.AsSpan(0, combinedLength);
            var searchFrom = 0;
            while (true)
            {
                var index = span[searchFrom..].IndexOf(pattern);
                if (index < 0)
                {
                    break;
                }

                if (hits.Count == limit)
                {
                    more = true;
                    break;
                }

                hits.Add(carryStart + searchFrom + index);
                searchFrom += index + 1;
            }

            if (more)
            {
                break;
            }

            var nextCarry = Math.Min(maxCarry, combinedLength);
            if (nextCarry > 0)
            {
                span[(combinedLength - nextCarry)..].CopyTo(combined);
                carryStart += combinedLength - nextCarry;
            }

            carryLength = nextCarry;
        }

        return (hits, more);
    }

    private static async Task AppendContextAsync(
        StringBuilder builder,
        Stream stream,
        long size,
        long hit,
        int patternLength,
        int context,
        CancellationToken cancellationToken)
    {
        var beforeStart = Math.Max(0, hit - context);
        var beforeLength = (int)(hit - beforeStart);
        if (beforeLength > 0)
        {
            var before = new byte[beforeLength];
            stream.Seek(beforeStart, SeekOrigin.Begin);
            await BinaryFile
                .ReadAtMostAsync(stream, before, cancellationToken)
                .ConfigureAwait(false);
            BinaryText.AppendLine(builder, $"  before: {HexBytes(before)}");
        }

        var afterStart = hit + patternLength;
        if (afterStart < size)
        {
            var afterLength = (int)Math.Min(context, size - afterStart);
            var after = new byte[afterLength];
            stream.Seek(afterStart, SeekOrigin.Begin);
            await BinaryFile
                .ReadAtMostAsync(stream, after, cancellationToken)
                .ConfigureAwait(false);
            BinaryText.AppendLine(builder, $"  after: {HexBytes(after)}");
        }
    }

    private static string HexPreview(byte[] bytes)
    {
        var count = Math.Min(bytes.Length, BinaryLimits.MaximumMatchPreviewBytes);
        var preview = HexBytes(bytes.AsSpan(0, count));
        return count < bytes.Length ? preview + " ..." : preview;
    }

    private static string HexBytes(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length * 3);
        for (var index = 0; index < bytes.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            builder.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string PastEnd(string path, long size, long offset) =>
        $"{path} has {size} bytes; offset {offset} is past the end.";

    private static ToolDefinition CreateTool()
    {
        using var document = JsonDocument.Parse(Schema);
        return new ToolDefinition(ToolName, document.RootElement.Clone(), ToolDescription);
    }
}
