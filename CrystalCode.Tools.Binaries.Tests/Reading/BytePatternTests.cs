using CrystalCode.Tools.Binaries.Reading;

namespace CrystalCode.Tools.Binaries.Tests.Reading;

public sealed class BytePatternTests
{
    [Fact]
    public void ParsesSpacedHex()
    {
        Assert.True(BytePattern.TryParse("hex", "89 50 4E 47", out var pattern, out _));

        Assert.Equal([0x89, 0x50, 0x4E, 0x47], pattern!.Bytes);
        Assert.Equal("4-byte hex pattern", pattern.Description);
    }

    [Fact]
    public void ParsesContiguousHex()
    {
        Assert.True(BytePattern.TryParse("hex", "89504E47", out var pattern, out _));

        Assert.Equal([0x89, 0x50, 0x4E, 0x47], pattern!.Bytes);
    }

    [Fact]
    public void ParsesAsciiText()
    {
        Assert.True(BytePattern.TryParse("ascii", "hello", out var pattern, out _));

        Assert.Equal("hello"u8.ToArray(), pattern!.Bytes);
        Assert.Equal("5-byte ascii pattern", pattern.Description);
    }

    [Fact]
    public void ParsesUtf8Text()
    {
        Assert.True(BytePattern.TryParse("utf8", "é", out var pattern, out _));

        Assert.Equal("é"u8.ToArray(), pattern!.Bytes);
        Assert.Equal("2-byte utf8 pattern", pattern.Description);
    }

    [Fact]
    public void ParsesUtf16LeText()
    {
        Assert.True(BytePattern.TryParse("utf16le", "AB", out var pattern, out _));

        Assert.Equal([0x41, 0x00, 0x42, 0x00], pattern!.Bytes);
        Assert.Equal("4-byte utf16le pattern", pattern.Description);
    }

    [Fact]
    public void RejectsAnOddNumberOfHexDigits()
    {
        Assert.False(BytePattern.TryParse("hex", "895", out var pattern, out var error));

        Assert.Null(pattern);
        Assert.Equal("The hex pattern must contain an even number of hex digits.", error);
    }

    [Fact]
    public void RejectsInvalidHexCharacters()
    {
        Assert.False(BytePattern.TryParse("hex", "89 5G", out _, out var error));

        Assert.Equal(
            "The hex pattern must contain only hex digits and whitespace, for example '89 50 4E 47'.",
            error);
    }

    [Fact]
    public void RejectsAWhitespaceOnlyHexPattern()
    {
        Assert.False(BytePattern.TryParse("hex", "   ", out _, out var error));

        Assert.Equal("The hex pattern must contain at least one byte.", error);
    }

    [Fact]
    public void RejectsNonAsciiTextInAsciiEncoding()
    {
        Assert.False(BytePattern.TryParse("ascii", "héllo", out _, out var error));

        Assert.Equal(
            "The ascii pattern must contain only ASCII characters; "
            + "use the utf8 or utf16le encoding for other text.",
            error);
    }

    [Fact]
    public void RejectsAnOversizedPattern()
    {
        var hex = string.Concat(Enumerable.Repeat("aa", 300));

        Assert.False(BytePattern.TryParse("hex", hex, out _, out var error));

        Assert.Equal("The pattern is 300 bytes; the maximum is 256 bytes.", error);
    }

    [Fact]
    public void RejectsAnUnknownEncoding()
    {
        Assert.False(BytePattern.TryParse("yaml", "x", out _, out var error));

        Assert.Equal("Argument 'encoding' must be hex, ascii, utf8, or utf16le.", error);
    }
}
