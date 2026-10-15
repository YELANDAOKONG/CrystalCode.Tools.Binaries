namespace CrystalCode.Tools.Binaries;

/// <summary>
/// Model-facing and transport limits for the binary inspection tools.
/// </summary>
internal static class BinaryLimits
{
    public const int BufferBytes = 64 * 1024;

    public const int HexDumpBytesPerRow = 16;

    public const int DefaultHexDumpBytes = 1024;

    public const int MaximumHexDumpBytes = 16 * 1024;

    public const int ProbeBytes = 8192;

    public const int DefaultMinimumStringLength = 8;

    public const int LowestMinimumStringLength = 4;

    public const int HighestMinimumStringLength = 1024;

    public const int DefaultStringLimit = 200;

    public const int MaximumStringLimit = 500;

    public const int MaximumStringCharacters = 200;

    public const int DefaultSearchLimit = 100;

    public const int MaximumSearchLimit = 1000;

    public const int MaximumSearchContextBytes = 64;

    public const int MaximumPatternBytes = 256;

    public const int MaximumMatchPreviewBytes = 16;
}
