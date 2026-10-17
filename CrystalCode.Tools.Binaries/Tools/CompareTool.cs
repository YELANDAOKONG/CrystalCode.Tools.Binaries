using System.Globalization;
using System.Text;
using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Tools.Binaries.Reading;

namespace CrystalCode.Tools.Binaries.Tools;

/// <summary>
/// Compares two files byte by byte and reports their differences.
/// </summary>
public sealed class CompareTool : ITool
{
    private const string ToolName = "compare";

    private const string ToolDescription =
        "Compares two files byte by byte and reports equality, both sizes, "
        + "the first difference offset with both byte values, "
        + "and the number of differing bytes in the common region. "
        + "Use it to verify rebuilds, patch application, or artifact changes; "
        + "a hash comparison only answers equality. "
        + "Use mode 'ranges' for paginated consecutive difference regions with byte previews; "
        + "offset, length, and limit apply only to ranges mode. No insertion/deletion alignment. "
        + "Paths inside the workspace run without asking. "
        + "Paths outside the workspace require approval.";

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "Workspace-relative or absolute path of the first file."
            },
            "mode": {
              "type": "string",
              "enum": ["summary", "ranges"],
              "description": "summary (default) compares whole files; ranges lists consecutive differing regions at matching offsets, including extra tails. No insertion/deletion alignment."
            },
            "offset": {
              "type": "integer", "minimum": 0,
              "description": "Start byte offset for ranges mode (default 0)."
            },
            "length": {
              "type": "integer", "minimum": 1,
              "description": "Bytes to compare in ranges mode (default through the longer file)."
            },
            "limit": {
              "type": "integer", "minimum": 1, "maximum": 100,
              "description": "Maximum ranges in ranges mode (default 20); output includes a continuation offset."
            },
            "other": {
              "type": "string",
              "description": "Workspace-relative or absolute path of the second file."
            }
          },
          "required": ["path", "other"],
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
            if (!BinaryArguments.TryRejectUnknown(root, ["path", "other", "mode", "offset", "length", "limit"], out error)
                || !BinaryArguments.TryReadRequiredString(root, "path", out var path, out error)
                || !BinaryArguments.TryReadRequiredString(root, "other", out var other, out error)
                || !BinaryArguments.TryReadOptionalChoice(root, "mode", ["summary", "ranges"], out var mode, out error)
                || !BinaryArguments.TryReadOptionalInteger(root, "offset", 0, long.MaxValue, out var offset, out error)
                || !BinaryArguments.TryReadOptionalInteger(root, "length", 1, long.MaxValue, out var length, out error)
                || !BinaryArguments.TryReadOptionalInteger(root, "limit", 1, 100, out var limit, out error))
            {
                return BinaryToolResult.Failure(error!);
            }

            if (mode != "ranges" && (offset is not null || length is not null || limit is not null))
            {
                return BinaryToolResult.Failure("Arguments 'offset', 'length', and 'limit' require mode 'ranges'.");
            }

            return await CompareAsync(path, other, mode == "ranges", offset ?? 0, length,
                (int)(limit ?? 20), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<ToolOutput> CompareAsync(
        string path,
        string other,
        bool ranges,
        long offset,
        long? length,
        int limit,
        CancellationToken cancellationToken)
    {
        if (BinaryFile.Validate(path) is { } firstError)
        {
            return BinaryToolResult.Failure(firstError);
        }

        if (BinaryFile.Validate(other) is { } secondError)
        {
            return BinaryToolResult.Failure(secondError);
        }

        if (!TryOpenFile(path, out var first, out var firstFailure))
        {
            return firstFailure!;
        }

        await using (first)
        {
            if (!TryOpenFile(other, out var second, out var secondFailure))
            {
                return secondFailure!;
            }

            await using (second)
            {
                FileComparison comparison;
                try
                {
                    if (ranges)
                    {
                        if (offset > Math.Max(first.Length, second.Length))
                        {
                            return BinaryToolResult.Failure("The offset is past the end of both files.");
                        }

                        var details = await DifferenceRanges.ReadAsync(first, second, offset, length, limit, cancellationToken)
                            .ConfigureAwait(false);
                        return new ToolOutput($"{path}: {first.Length} bytes\n{other}: {second.Length} bytes\n{details}");
                    }

                    comparison = await FileCompare
                        .CompareAsync(first, second, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return BinaryToolResult.Failure(
                        $"The files could not be read: {path}, {other}.");
                }

                var builder = new StringBuilder();
                BinaryText.AppendLine(builder, $"{path}: {comparison.FirstBytes} bytes");
                BinaryText.AppendLine(builder, $"{other}: {comparison.SecondBytes} bytes");
                BinaryText.AppendLine(builder, comparison.Identical ? "identical: yes" : "identical: no");
                if (!comparison.Identical)
                {
                    if (comparison.CommonBytes > 0)
                    {
                        if (comparison.FirstDifferenceOffset >= 0)
                        {
                            BinaryText.AppendLine(
                                builder,
                                $"first difference: offset {comparison.FirstDifferenceOffset} "
                                + $"({Hex(comparison.FirstDifferenceValue)} vs "
                                + $"{Hex(comparison.SecondDifferenceValue)})");
                        }
                        else
                        {
                            BinaryText.AppendLine(
                                builder,
                                $"first difference: none in the first {comparison.CommonBytes} bytes");
                        }

                        if (comparison.DifferingBytes > 0)
                        {
                            BinaryText.AppendLine(
                                builder,
                                $"differing bytes: {comparison.DifferingBytes} "
                                + $"of the first {comparison.CommonBytes} bytes");
                        }
                    }

                    if (comparison.FirstBytes != comparison.SecondBytes)
                    {
                        var longer = comparison.FirstBytes > comparison.SecondBytes ? path : other;
                        var delta = Math.Abs(comparison.FirstBytes - comparison.SecondBytes);
                        BinaryText.AppendLine(
                            builder,
                            $"lengths differ: {longer} is {delta} bytes longer");
                    }
                }

                return new ToolOutput(BinaryText.Finish(builder));
            }
        }
    }

    private static bool TryOpenFile(string path, out FileStream stream, out ToolOutput? failure)
    {
        failure = null;
        try
        {
            stream = BinaryFile.Open(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            stream = null!;
            failure = BinaryToolResult.Failure($"The file could not be read: {path}.");
            return false;
        }
    }

    private static string Hex(byte value) => value.ToString("x2", CultureInfo.InvariantCulture);

    private static ToolDefinition CreateTool()
    {
        using var document = JsonDocument.Parse(Schema);
        return ToolDefinition.Json(ToolName, document.RootElement.Clone(), ToolDescription);
    }
}
