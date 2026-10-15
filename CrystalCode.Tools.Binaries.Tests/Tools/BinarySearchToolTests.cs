using System.Text;
using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Tools.Binaries.Tools;

namespace CrystalCode.Tools.Binaries.Tests.Tools;

public sealed class BinarySearchToolTests
{
    [Fact]
    public void ExposesTheBinsearchDefinition()
    {
        var tool = new BinarySearchTool();

        Assert.Equal("binsearch", tool.Definition.Name);
        Assert.NotNull(tool.Definition.Description);
        using var schema = JsonDocument.Parse(tool.Definition.InputSchema.GetRawText());
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["path", "pattern"],
            schema.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    [Fact]
    public async Task FindsASpacedHexPattern()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("magic.bin", [1, 2, 3, 0x89, 0x50, 0x4E, 0x47, 9]);

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall("1", "binsearch", JsonSerializer.Serialize(new { path, pattern = "89 50 4e 47" })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 8 bytes\n"
            + "1 match for a 4-byte hex pattern\n"
            + "offset 3: 89 50 4e 47",
            output.Text);
    }

    [Fact]
    public async Task AcceptsContiguousUppercaseHex()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("magic.bin", [1, 2, 3, 0x89, 0x50, 0x4E, 0x47, 9]);

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall("1", "binsearch", JsonSerializer.Serialize(new { path, pattern = "89504E47" })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("offset 3: 89 50 4e 47", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindsAsciiText()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("words.txt", "hello world hello");

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall(
                "1",
                "binsearch",
                JsonSerializer.Serialize(new { path, pattern = "hello", encoding = "ascii" })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 17 bytes\n"
            + "2 matches for a 5-byte ascii pattern\n"
            + "offset 0: 68 65 6c 6c 6f\n"
            + "offset 12: 68 65 6c 6c 6f",
            output.Text);
    }

    [Fact]
    public async Task FindsUtf16LeText()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("utf16.bin", [0x41, 0x00, 0x42, 0x00]);

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall(
                "1",
                "binsearch",
                JsonSerializer.Serialize(new { path, pattern = "AB", encoding = "utf16le" })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 4 bytes\n"
            + "1 match for a 4-byte utf16le pattern\n"
            + "offset 0: 41 00 42 00",
            output.Text);
    }

    [Fact]
    public async Task ReportsOverlappingMatches()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("overlap.bin", [0xAA, 0xAA, 0xAA]);

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall("1", "binsearch", JsonSerializer.Serialize(new { path, pattern = "aa aa" })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 3 bytes\n"
            + "2 matches for a 2-byte hex pattern\n"
            + "offset 0: aa aa\n"
            + "offset 1: aa aa",
            output.Text);
    }

    [Fact]
    public async Task StopsAtTheLimitAndNotesWhereToContinue()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("many.bin", [0xAA, 0xBB, 0xAA, 0xBB, 0xAA]);

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall("1", "binsearch", JsonSerializer.Serialize(new { path, pattern = "aa", limit = 2 })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 5 bytes\n"
            + "at least 2 matches for a 1-byte hex pattern\n"
            + "offset 0: aa\n"
            + "offset 2: aa\n"
            + "[showing 2 matches; continue with offset 3]",
            output.Text);
    }

    [Fact]
    public async Task ShowsContextAroundAMatch()
    {
        using var files = new TemporaryFiles();
        var path = files.Write(
            "frame.bin",
            [0xDE, 0xAD, 0xBE, 0xEF, 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01]);

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall(
                "1",
                "binsearch",
                JsonSerializer.Serialize(new { path, pattern = "89 50 4e 47", context = 2 })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 10 bytes\n"
            + "1 match for a 4-byte hex pattern\n"
            + "offset 4: 89 50 4e 47\n"
            + "  before: be ef\n"
            + "  after: 00 01",
            output.Text);
    }

    [Fact]
    public async Task FindsAPatternThatCrossesTheChunkBoundary()
    {
        using var files = new TemporaryFiles();
        var bytes = new byte[65534 + 4];
        bytes[65534] = 0x89;
        bytes[65535] = 0x50;
        bytes[65536] = 0x4E;
        bytes[65537] = 0x47;
        var path = files.Write("boundary.bin", bytes);

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall("1", "binsearch", JsonSerializer.Serialize(new { path, pattern = "89 50 4e 47" })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 65538 bytes\n"
            + "1 match for a 4-byte hex pattern\n"
            + "offset 65534: 89 50 4e 47",
            output.Text);
    }

    [Fact]
    public async Task ScansOnlyTheRequestedWindow()
    {
        using var files = new TemporaryFiles();
        var bytes = new byte[100];
        bytes[10] = 0xAA;
        bytes[90] = 0xAA;
        var path = files.Write("window.bin", bytes);

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall(
                "1",
                "binsearch",
                JsonSerializer.Serialize(new { path, pattern = "aa", offset = 50 })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 100 bytes\n"
            + "1 match for a 1-byte hex pattern\n"
            + "offset 90: aa",
            output.Text);
    }

    [Fact]
    public async Task AnswersNoMatches()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("plain.txt", "plain text");

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall(
                "1",
                "binsearch",
                JsonSerializer.Serialize(new { path, pattern = "zz", encoding = "ascii" })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("No matches.", output.Text);
    }

    [Fact]
    public async Task RejectsAnOddNumberOfHexDigits()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("magic.bin", [0x89]);

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall("1", "binsearch", JsonSerializer.Serialize(new { path, pattern = "895" })));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("The hex pattern must contain an even number of hex digits.", output.Text);
    }

    [Fact]
    public async Task RejectsInvalidHexCharacters()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("magic.bin", [0x89]);

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall("1", "binsearch", JsonSerializer.Serialize(new { path, pattern = "89 5G" })));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal(
            "The hex pattern must contain only hex digits and whitespace, for example '89 50 4E 47'.",
            output.Text);
    }

    [Fact]
    public async Task RejectsNonAsciiTextInAsciiEncoding()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("plain.txt", "plain text");

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall(
                "1",
                "binsearch",
                JsonSerializer.Serialize(new { path, pattern = "héllo", encoding = "ascii" })));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal(
            "The ascii pattern must contain only ASCII characters; "
            + "use the utf8 or utf16le encoding for other text.",
            output.Text);
    }

    [Fact]
    public async Task RejectsAZeroLengthWindow()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("magic.bin", [0x89]);

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall("1", "binsearch", JsonSerializer.Serialize(new { path, pattern = "89", length = 0 })));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Argument 'length' must be a positive integer.", output.Text);
    }

    [Fact]
    public async Task TextPatternsEncodeThroughUtf8()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("text.txt", "café");

        var output = await new BinarySearchTool().InvokeAsync(
            new ToolCall(
                "1",
                "binsearch",
                JsonSerializer.Serialize(new { path, pattern = "café", encoding = "utf8" })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("1 match for a 5-byte utf8 pattern", output.Text, StringComparison.Ordinal);
        Assert.Contains("offset 0: 63 61 66 c3 a9", output.Text, StringComparison.Ordinal);
    }
}
