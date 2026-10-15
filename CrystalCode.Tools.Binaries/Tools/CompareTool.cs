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
            if (!BinaryArguments.TryRejectUnknown(root, ["path", "other"], out error)
                || !BinaryArguments.TryReadRequiredString(root, "path", out var path, out error)
                || !BinaryArguments.TryReadRequiredString(root, "other", out var other, out error))
            {
                return BinaryToolResult.Failure(error!);
            }

            return await CompareAsync(path, other, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<ToolOutput> CompareAsync(
        string path,
        string other,
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
        return new ToolDefinition(ToolName, document.RootElement.Clone(), ToolDescription);
    }
}
