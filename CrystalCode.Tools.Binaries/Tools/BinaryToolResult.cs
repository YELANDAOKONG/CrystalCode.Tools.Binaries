using Crystal.Tools;

namespace CrystalCode.Tools.Binaries.Tools;

/// <summary>
/// Builds model-visible results for the binary inspection tools.
/// </summary>
internal static class BinaryToolResult
{
    public static ToolOutput Failure(string message) => new(message, ToolResultStatus.Failure);
}
