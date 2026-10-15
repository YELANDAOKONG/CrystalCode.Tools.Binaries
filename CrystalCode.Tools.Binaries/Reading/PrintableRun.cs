namespace CrystalCode.Tools.Binaries.Reading;

/// <summary>
/// One printable text run found in a byte window.
/// </summary>
internal sealed record PrintableRun(long Offset, long ByteLength, string Text);
