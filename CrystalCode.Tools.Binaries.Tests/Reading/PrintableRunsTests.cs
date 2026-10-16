using System.Text;

using CrystalCode.Tools.Binaries.Reading;

namespace CrystalCode.Tools.Binaries.Tests.Reading;

public sealed class PrintableRunsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundsPreviewWhilePreservingRunLengthAndContinuation(bool utf16le)
    {
        const int characterCount = 100_000;
        var encoding = utf16le ? Encoding.Unicode : Encoding.ASCII;
        using var stream = new MemoryStream(encoding.GetBytes(new string('A', characterCount) + "\0BBBB"));

        var scan = await PrintableRuns.ScanAsync(stream, 0, stream.Length, 1024, utf16le, 1, default);

        var run = Assert.Single(scan.Runs);
        Assert.Equal(0, run.Offset);
        Assert.Equal((long)characterCount * (utf16le ? 2 : 1), run.ByteLength);
        Assert.Equal(new string('A', BinaryLimits.MaximumStringCharacters + 1), run.Text);
        Assert.False(scan.More);

        stream.Position = run.Offset + run.ByteLength;
        var next = await PrintableRuns.ScanAsync(
            stream, stream.Position, stream.Length - stream.Position, 4, utf16le, 1, default);

        var nextRun = Assert.Single(next.Runs);
        Assert.Equal(run.ByteLength + (utf16le ? 2 : 1), nextRun.Offset);
        Assert.Equal("BBBB", nextRun.Text);
    }
}
