using System.Text;
using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Tools.Binaries.Reading;

namespace CrystalCode.Tools.Binaries.Tools;

/// <summary>
/// Computes a file digest and optionally compares it with an expected value.
/// </summary>
public sealed class HashTool : ITool
{
    private const string ToolName = "hash";

    private const string ToolDescription =
        "Computes a file digest and its size. "
        + "algorithm is sha256 (default), sha384, sha512, sha1, md5, sha3-256, sha3-384, or sha3-512; "
        + "sha1 and md5 exist for matching existing published values. "
        + "expected optionally gives a hex digest to compare against; the result reports match yes or no. "
        + "Use it to verify a build, download, or artifact identity. "
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
            "algorithm": {
              "type": "string",
              "enum": ["sha256", "sha384", "sha512", "sha1", "md5", "sha3-256", "sha3-384", "sha3-512"],
              "description": "Digest algorithm (default sha256)."
            },
            "expected": {
              "type": "string",
              "description": "Optional expected digest in hex; the result reports whether it matches."
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
                    ["path", "algorithm", "expected"],
                    out error)
                || !BinaryArguments.TryReadRequiredString(root, "path", out var path, out error)
                || !BinaryArguments.TryReadOptionalChoice(
                    root,
                    "algorithm",
                    ["sha256", "sha384", "sha512", "sha1", "md5", "sha3-256", "sha3-384", "sha3-512"],
                    out var algorithm,
                    out error)
                || !BinaryArguments.TryReadOptionalString(root, "expected", out var expected, out error))
            {
                return BinaryToolResult.Failure(error!);
            }

            var selected = algorithm ?? "sha256";
            if (BinaryFile.Validate(path) is { } validationError)
            {
                return BinaryToolResult.Failure(validationError);
            }

            if (!FileHash.TryGetAlgorithm(selected, out var hashAlgorithm))
            {
                return BinaryToolResult.Failure(
                    "Argument 'algorithm' must be 'sha256', 'sha384', 'sha512', 'sha1', 'md5', "
                    + "'sha3-256', 'sha3-384', or 'sha3-512'.");
            }

            try
            {
                await using var stream = BinaryFile.Open(path);
                var (hex, bytes) = await FileHash
                    .ComputeAsync(stream, hashAlgorithm, cancellationToken)
                    .ConfigureAwait(false);
                var builder = new StringBuilder();
                BinaryText.AppendLine(builder, $"{path}: {bytes} bytes");
                BinaryText.AppendLine(builder, $"{selected}: {hex}");
                if (expected is not null)
                {
                    var match = string.Equals(expected, hex, StringComparison.OrdinalIgnoreCase);
                    BinaryText.AppendLine(builder, $"expected: {expected}");
                    BinaryText.AppendLine(builder, $"match: {(match ? "yes" : "no")}");
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
    }

    private static ToolDefinition CreateTool()
    {
        using var document = JsonDocument.Parse(Schema);
        return ToolDefinition.Json(ToolName, document.RootElement.Clone(), ToolDescription);
    }
}
