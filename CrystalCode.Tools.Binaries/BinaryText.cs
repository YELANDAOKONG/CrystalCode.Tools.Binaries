using System.Text;

namespace CrystalCode.Tools.Binaries;

/// <summary>
/// Builds model-visible text with explicit LF separators on every platform.
/// </summary>
internal static class BinaryText
{
    public static void AppendLine(StringBuilder builder, string text)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Append(text).Append('\n');
    }

    public static string Finish(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ToString().TrimEnd('\n');
    }
}
