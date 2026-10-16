using System.Text;

using CrystalCode.Tools.Binaries.Reading;

namespace CrystalCode.Tools.Binaries.Tests.Reading;

public sealed class Utf8RunsTests
{
    [Fact]
    public async Task CountsScalarsAndPreservesByteOffsets()
    {
        var text = "中文测试😀";
        using var stream = new MemoryStream([0xFF, .. Encoding.UTF8.GetBytes(text), 0, .. "short"u8.ToArray()]);
        var scan = await Utf8Runs.ScanAsync(stream, 0, stream.Length, 5, 1, default);

        var run = Assert.Single(scan.Runs);
        Assert.Equal(1, run.Offset);
        Assert.Equal(Encoding.UTF8.GetByteCount(text), run.ByteLength);
        Assert.Equal(text, run.Text);
        Assert.True(scan.More);
    }

    [Theory]
    [InlineData(65533)]
    [InlineData(65534)]
    [InlineData(65535)]
    public async Task DecodesScalarsAcrossChunkBoundaries(int prefixLength)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('A', prefixLength) + "😀中文测试"));
        var scan = await Utf8Runs.ScanAsync(stream, 0, stream.Length, 4, 1, default);

        var run = Assert.Single(scan.Runs);
        Assert.Equal(stream.Length, run.ByteLength);
        Assert.Equal(BinaryLimits.MaximumStringCharacters + 1, run.Text.Length);
        Assert.False(scan.More);
    }

    [Fact]
    public async Task InvalidAndIncompleteSequencesSplitRunsWithoutReplacementCharacters()
    {
        using var stream = new MemoryStream([.. "good"u8.ToArray(), 0xFF, .. "text"u8.ToArray(), 0xF0, 0x9F]);
        var scan = await Utf8Runs.ScanAsync(stream, 0, stream.Length, 4, 10, default);

        Assert.Equal(["good", "text"], scan.Runs.Select(run => run.Text).ToArray());
        Assert.Equal([0L, 5L], scan.Runs.Select(run => run.Offset).ToArray());
    }

    [Fact]
    public async Task RespectsWindowsThatStartOrEndInsideAScalar()
    {
        var bytes = Encoding.UTF8.GetBytes("😀text😀");
        using var stream = new MemoryStream(bytes);
        stream.Position = 1;
        var scan = await Utf8Runs.ScanAsync(stream, 1, bytes.Length - 2, 4, 10, default);

        var run = Assert.Single(scan.Runs);
        Assert.Equal(4, run.Offset);
        Assert.Equal("text", run.Text);
        Assert.Equal(4, run.ByteLength);
    }

    [Fact]
    public async Task RejectsControlCharactersAndDoesNotCountUtf16UnitsAsScalars()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("😀😀\n中文测试\u2028next"));
        var scan = await Utf8Runs.ScanAsync(stream, 0, stream.Length, 4, 10, default);
        Assert.Equal(["中文测试", "next"], scan.Runs.Select(run => run.Text).ToArray());
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        using var stream = new MemoryStream("text"u8.ToArray());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Utf8Runs.ScanAsync(stream, 0, stream.Length, 4, 1, cancellation.Token));
    }
}
