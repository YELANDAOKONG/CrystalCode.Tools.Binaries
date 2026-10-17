using System.Text;
using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Tools.Binaries.Reading;

namespace CrystalCode.Tools.Binaries.Tools;

/// <summary>
/// Shows a bounded byte window of a file as hex and ASCII rows.
/// </summary>
public sealed class HexDumpTool : ITool
{
    private const string ToolName = "hexdump";

    private const string ToolDescription =
        "Shows a bounded byte window of a file as hex and ASCII rows. "
        + "Use it to inspect binary or unknown files; identify formats from the bytes. "
        + "offset starts the window (default 0) and length is 1-16384 bytes (default 1024); "
        + "rows use eight-digit hex offsets and 16 bytes per row, and the result notes where to continue. "
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
            "offset": {
              "type": "integer",
              "minimum": 0,
              "description": "Byte offset to start the window at (default 0)."
            },
            "length": {
              "type": "integer",
              "minimum": 1,
              "maximum": 16384,
              "description": "Bytes to show (1-16384, default 1024)."
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
            if (!BinaryArguments.TryRejectUnknown(root, ["path", "offset", "length"], out error)
                || !BinaryArguments.TryReadRequiredString(root, "path", out var path, out error)
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
                    BinaryLimits.MaximumHexDumpBytes,
                    out var length,
                    out error))
            {
                return BinaryToolResult.Failure(error!);
            }

            return await DumpAsync(
                    path,
                    offset ?? 0,
                    (int)(length ?? BinaryLimits.DefaultHexDumpBytes),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<ToolOutput> DumpAsync(
        string path,
        long offset,
        int length,
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

            if (offset >= size)
            {
                return BinaryToolResult.Failure(PastEnd(path, size, offset));
            }

            var count = (int)Math.Min(length, size - offset);
            var window = new byte[count];
            stream.Seek(offset, SeekOrigin.Begin);
            var read = await BinaryFile
                .ReadAtMostAsync(stream, window, cancellationToken)
                .ConfigureAwait(false);
            var builder = new StringBuilder();
            BinaryText.AppendLine(builder, $"{path}: {size} bytes");
            HexRows.Append(builder, window.AsSpan(0, read), offset);
            if (offset + read < size)
            {
                BinaryText.AppendLine(
                    builder,
                    $"[showing {read} of {size} bytes; continue with offset {offset + read}]");
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
        return ToolDefinition.Json(ToolName, document.RootElement.Clone(), ToolDescription);
    }
}
