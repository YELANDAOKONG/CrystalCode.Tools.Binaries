using System.Text;
using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Tools.Binaries.Reading;

namespace CrystalCode.Tools.Binaries.Tools;

/// <summary>
/// Extracts printable text runs from a file with their byte offsets.
/// </summary>
public sealed class StringsTool : ITool
{
    private const string ToolName = "strings";

    private const string ToolDescription =
        "Extracts printable text runs from a file with their byte offsets. "
        + "encoding 'ascii' (default) finds printable ASCII runs; 'utf16le' finds UTF-16LE runs, "
        + "which is what PE and .NET binaries usually contain. 'utf8' extracts valid UTF-8 text, "
        + "counts Unicode scalar values, and splits runs at invalid bytes and control characters. "
        + "minimumLength is the minimum characters per run (4-1024, default 8). "
        + "offset and length bound the scan window in bytes (default: the whole file). "
        + "limit caps returned runs (1-500, default 200) and the result notes where to continue. "
        + "The first bytes of the file are probed and reported. "
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
            "minimumLength": {
              "type": "integer",
              "minimum": 4,
              "maximum": 1024,
              "description": "Minimum characters per run (4-1024, default 8)."
            },
            "encoding": {
              "type": "string",
              "enum": ["ascii", "utf16le", "utf8"],
              "description": "Run encoding: 'ascii' (default), 'utf16le', or strict 'utf8'. UTF-8 minimumLength counts Unicode scalar values."
            },
            "offset": {
              "type": "integer",
              "minimum": 0,
              "description": "Byte offset to start the scan at (default 0)."
            },
            "length": {
              "type": "integer",
              "minimum": 1,
              "description": "Bytes to scan from offset (default: to the end of the file)."
            },
            "limit": {
              "type": "integer",
              "minimum": 1,
              "maximum": 500,
              "description": "Maximum runs to return (1-500, default 200)."
            }
          },
          "required": ["path"],
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
                    ["path", "minimumLength", "encoding", "offset", "length", "limit"],
                    out error)
                || !BinaryArguments.TryReadRequiredString(root, "path", out var path, out error)
                || !BinaryArguments.TryReadOptionalInteger(
                    root,
                    "minimumLength",
                    BinaryLimits.LowestMinimumStringLength,
                    BinaryLimits.HighestMinimumStringLength,
                    out var minimumLength,
                    out error)
                || !BinaryArguments.TryReadOptionalChoice(
                    root,
                    "encoding",
                    ["ascii", "utf16le", "utf8"],
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
                    BinaryLimits.MaximumStringLimit,
                    out var limit,
                    out error))
            {
                return BinaryToolResult.Failure(error!);
            }

            return await ScanAsync(
                    path,
                    (int)(minimumLength ?? BinaryLimits.DefaultMinimumStringLength),
                    encoding ?? "ascii",
                    offset ?? 0,
                    length,
                    (int)(limit ?? BinaryLimits.DefaultStringLimit),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<ToolOutput> ScanAsync(
        string path,
        int minimumLength,
        string encoding,
        long offset,
        long? length,
        int limit,
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

            var windowLength = length is null
                ? size - offset
                : Math.Min(length.Value, size - offset);
            var probe = new byte[(int)Math.Min(BinaryLimits.ProbeBytes, size)];
            stream.Seek(0, SeekOrigin.Begin);
            var probeRead = await BinaryFile
                .ReadAtMostAsync(stream, probe, cancellationToken)
                .ConfigureAwait(false);
            stream.Seek(offset, SeekOrigin.Begin);
            var scan = encoding == "utf8"
                ? await Utf8Runs.ScanAsync(stream, offset, windowLength, minimumLength, limit, cancellationToken)
                    .ConfigureAwait(false)
                : await PrintableRuns.ScanAsync(stream, offset, windowLength, minimumLength, encoding == "utf16le", limit, cancellationToken)
                    .ConfigureAwait(false);

            var builder = new StringBuilder();
            BinaryText.AppendLine(builder, $"{path}: {size} bytes");
            BinaryText.AppendLine(builder, $"probe: {TextProbe.Describe(probe.AsSpan(0, probeRead))}");
            if (scan.Runs.Count == 0)
            {
                BinaryText.AppendLine(builder, "No printable runs found.");
            }
            else
            {
                var listed = 0;
                var continueAt = offset;
                var truncated = false;
                foreach (var run in scan.Runs)
                {
                    var previewLength = Math.Min(run.Text.Length, BinaryLimits.MaximumStringCharacters);
                    if (previewLength < run.Text.Length && char.IsHighSurrogate(run.Text[previewLength - 1]))
                    {
                        previewLength--;
                    }

                    var text = run.Text.Length > previewLength
                        ? run.Text[..previewLength] + "..."
                        : run.Text;
                    var line = $"offset {run.Offset}: {text}";
                    if (listed > 0
                        && builder.Length + line.Length + 1 > BinaryLimits.MaximumResultCharacters)
                    {
                        truncated = true;
                        break;
                    }

                    BinaryText.AppendLine(builder, line);
                    listed++;
                    continueAt = run.Offset + run.ByteLength;
                }

                if (scan.More || truncated)
                {
                    BinaryText.AppendLine(
                        builder,
                        $"[showing {listed} runs; continue with offset {continueAt}]");
                }
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

    private static string PastEnd(string path, long size, long offset) =>
        $"{path} has {size} bytes; offset {offset} is past the end.";

    private static ToolDefinition CreateTool()
    {
        using var document = JsonDocument.Parse(Schema);
        return new ToolDefinition(ToolName, document.RootElement.Clone(), ToolDescription);
    }
}
