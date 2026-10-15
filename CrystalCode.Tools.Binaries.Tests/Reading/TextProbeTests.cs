using System.Text;

using CrystalCode.Tools.Binaries.Reading;

namespace CrystalCode.Tools.Binaries.Tests.Reading;

public sealed class TextProbeTests
{
    [Fact]
    public void DescribesAnEmptyProbe() =>
        Assert.Equal("empty", TextProbe.Describe([]));

    [Fact]
    public void RecognizesAUtf8Bom() =>
        Assert.Equal(
            "text with a UTF-8 BOM",
            TextProbe.Describe([0xEF, 0xBB, 0xBF, 0x41]));

    [Fact]
    public void RecognizesAUtf16LeBom() =>
        Assert.Equal(
            "text with a UTF-16LE BOM",
            TextProbe.Describe([0xFF, 0xFE, 0x41, 0x00]));

    [Fact]
    public void RecognizesAUtf16BeBom() =>
        Assert.Equal(
            "text with a UTF-16BE BOM",
            TextProbe.Describe([0xFE, 0xFF, 0x00, 0x41]));

    [Fact]
    public void RecognizesPlainText() =>
        Assert.Equal(
            "text (no BOM)",
            TextProbe.Describe(Encoding.ASCII.GetBytes("Hello")));

    [Fact]
    public void GuessesUtf16LeFromNullPlacement() =>
        Assert.Equal(
            "likely UTF-16LE text",
            TextProbe.Describe(Encoding.Unicode.GetBytes("Hello")));

    [Fact]
    public void GuessesUtf16BeFromNullPlacement() =>
        Assert.Equal(
            "likely UTF-16BE text",
            TextProbe.Describe([0x00, 0x48, 0x00, 0x69]));

    [Fact]
    public void DescribesMixedNullsAsBinary() =>
        Assert.Equal(
            "binary",
            TextProbe.Describe([0x00, 0x01, 0x02, 0x00, 0x03]));
}
