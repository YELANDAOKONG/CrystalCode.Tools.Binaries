namespace CrystalCode.Tools.Binaries.Reading;

/// <summary>
/// The result of comparing two files byte by byte. The first-difference
/// offset is -1 when the common region is identical.
/// </summary>
internal sealed record FileComparison(
    long FirstBytes,
    long SecondBytes,
    long FirstDifferenceOffset,
    byte FirstDifferenceValue,
    byte SecondDifferenceValue,
    long DifferingBytes)
{
    public long CommonBytes => Math.Min(FirstBytes, SecondBytes);

    public bool Identical => FirstBytes == SecondBytes && DifferingBytes == 0;
}
