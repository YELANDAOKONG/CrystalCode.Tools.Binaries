using System.Text;
using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Tools.Binaries.Reading;

namespace CrystalCode.Tools.Binaries.Tools;

/// <summary>
/// Reads fixed-width numeric values with explicit byte order.
/// </summary>
public sealed class BinaryReadTool : ITool
{
    private const int MaximumCount = 1024;
    private static readonly string[] Types = ["u8", "i8", "u16", "i16", "u32", "i32", "u64", "i64", "f32", "f64"];
    private static readonly ToolDefinition Tool = CreateTool();

    public ToolDefinition Definition => Tool;

    public async ValueTask<ToolOutput> InvokeAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        cancellationToken.ThrowIfCancellationRequested();
        if (!BinaryArguments.TryOpen(call.Arguments, out var root, out var document, out var error))
        {
            return BinaryToolResult.Failure(error!);
        }

        using (document)
        {
            if (!BinaryArguments.TryRejectUnknown(root, ["path", "offset", "type", "endianness", "count"], out error)
                || !BinaryArguments.TryReadRequiredString(root, "path", out var path, out error)
                || !BinaryArguments.TryReadOptionalChoice(root, "type", Types, out var type, out error)
                || !BinaryArguments.TryReadOptionalChoice(root, "endianness", ["little", "big"], out var endianness, out error)
                || !BinaryArguments.TryReadOptionalInteger(root, "offset", 0, long.MaxValue, out var offset, out error)
                || !BinaryArguments.TryReadOptionalInteger(root, "count", 1, MaximumCount, out var count, out error))
            {
                return BinaryToolResult.Failure(error!);
            }

            if (type is null)
            {
                return BinaryToolResult.Failure("Argument 'type' is required.");
            }

            if (BinaryFile.Validate(path) is { } validationError)
            {
                return BinaryToolResult.Failure(validationError);
            }

            try
            {
                await using var stream = BinaryFile.Open(path);
                var start = offset ?? 0;
                var width = NumericValue.Width(type);
                var requested = (int)(count ?? 1);
                var bytes = new byte[width * requested];
                if (start > stream.Length || bytes.Length > stream.Length - start)
                {
                    return BinaryToolResult.Failure("The requested values extend past the end of the file; no partial values were returned.");
                }

                stream.Seek(start, SeekOrigin.Begin);
                var read = await BinaryFile.ReadAtMostAsync(stream, bytes, cancellationToken).ConfigureAwait(false);
                if (read != bytes.Length)
                {
                    return BinaryToolResult.Failure("The file ended before all requested values could be read.");
                }

                var builder = new StringBuilder();
                BinaryText.AppendLine(builder, $"{path}: {stream.Length} bytes");
                BinaryText.AppendLine(builder, $"{requested} {type} values ({endianness ?? "little"}-endian)");
                for (var index = 0; index < requested; index++)
                {
                    var raw = bytes.AsSpan(index * width, width);
                    BinaryText.AppendLine(builder, $"offset {start + index * width}: {Convert.ToHexStringLower(raw)} = "
                        + NumericValue.Decode(raw, type, endianness == "big"));
                }

                return new ToolOutput(BinaryText.Finish(builder));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return BinaryToolResult.Failure($"The file could not be read: {path}.");
            }
        }
    }

    private static ToolDefinition CreateTool()
    {
        using var schema = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Workspace-relative or absolute file path." },
                "offset": { "type": "integer", "minimum": 0, "description": "Starting byte offset (default 0)." },
                "type": { "type": "string", "enum": ["u8", "i8", "u16", "i16", "u32", "i32", "u64", "i64", "f32", "f64"] },
                "endianness": { "type": "string", "enum": ["little", "big"], "description": "Byte order (default little)." },
                "count": { "type": "integer", "minimum": 1, "maximum": 1024, "description": "Number of values (default 1, maximum 1024)." }
              },
              "required": ["path", "type"],
              "additionalProperties": false
            }
            """);
        return ToolDefinition.Json("binread", schema.RootElement.Clone(),
            "Reads fixed-width integers or IEEE 754 floats at a byte offset with explicit endianness. "
            + "Returns original bytes and invariant numeric values; does not interpret file formats. "
            + "All requested values must fit in the file. Paths outside the workspace require approval.");
    }
}
