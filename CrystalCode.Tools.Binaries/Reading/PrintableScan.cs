namespace CrystalCode.Tools.Binaries.Reading;

/// <summary>
/// The runs found by one scan and whether scanning stopped at the limit.
/// </summary>
internal sealed record PrintableScan(IReadOnlyList<PrintableRun> Runs, bool More);
