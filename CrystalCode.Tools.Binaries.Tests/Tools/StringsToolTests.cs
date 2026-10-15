using System.Text;
using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Tools.Binaries.Tools;

namespace CrystalCode.Tools.Binaries.Tests.Tools;

public sealed class StringsToolTests
{
    [Fact]
    public void ExposesTheStringsDefinition()
    {
        var tool = new StringsTool();

        Assert.Equal("strings", tool.Definition.Name);
        Assert.NotNull(tool.Definition.Description);
        using var schema = JsonDocument.Parse(tool.Definition.InputSchema.GetRawText());
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            "path",
            schema.RootElement.GetProperty("required").EnumerateArray().Single().GetString());
    }

    [Fact]
    public async Task ExtractsAsciiRunsWithOffsets()
    {
        using var files = new TemporaryFiles();
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes(new string('A', 10)));
        bytes.AddRange([0x00, 0x00]);
        bytes.AddRange(Encoding.ASCII.GetBytes(new string('B', 3)));
        bytes.Add(0x00);
        bytes.AddRange(Encoding.ASCII.GetBytes(new string('C', 8)));
        var path = files.Write("runs.bin", [.. bytes]);

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 24 bytes\n"
            + "probe: binary\n"
            + "offset 0: AAAAAAAAAA\n"
            + "offset 16: CCCCCCCC",
            output.Text);
    }

    [Fact]
    public async Task ExtractsUtf16LeRuns()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("utf16.bin", Encoding.Unicode.GetBytes("Hello"));

        var output = await new StringsTool().InvokeAsync(
            new ToolCall(
                "1",
                "strings",
                JsonSerializer.Serialize(new { path, encoding = "utf16le", minimumLength = 4 })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 10 bytes\n"
            + "probe: likely UTF-16LE text\n"
            + "offset 0: Hello",
            output.Text);
    }

    [Fact]
    public async Task ProbesAPlainTextFile()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("plain.txt", "plain text");

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 10 bytes\n"
            + "probe: text (no BOM)\n"
            + "offset 0: plain text",
            output.Text);
    }

    [Fact]
    public async Task StopsAtTheLimitAndNotesWhereToContinue()
    {
        using var files = new TemporaryFiles();
        var bytes = new List<byte>();
        foreach (var letter in "ABCDE")
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(new string(letter, 8)));
            bytes.Add(0x00);
        }

        var path = files.Write("many.bin", [.. bytes]);

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path, limit = 2 })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 45 bytes\n"
            + "probe: binary\n"
            + "offset 0: AAAAAAAA\n"
            + "offset 9: BBBBBBBB\n"
            + "[showing 2 runs; continue with offset 17]",
            output.Text);
    }

    [Fact]
    public async Task ReportsNoRuns()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("blank.bin", [0x00, 0x01, 0x02, 0x00, 0x03]);

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 5 bytes\n"
            + "probe: binary\n"
            + "No printable runs found.",
            output.Text);
    }

    [Fact]
    public async Task ScansOnlyTheRequestedWindow()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("window.bin", new string('A', 10) + new string('B', 10));

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path, offset = 10 })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 20 bytes\n"
            + "probe: text (no BOM)\n"
            + "offset 10: BBBBBBBBBB",
            output.Text);
    }

    [Fact]
    public async Task ReportsAnEmptyFile()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("empty.bin", []);

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal($"{path}: 0 bytes (empty file)", output.Text);
    }

    [Fact]
    public async Task RejectsAnOffsetPastTheEnd()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("five.bin", "abcde");

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path, offset = 6 })));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal($"{path} has 5 bytes; offset 6 is past the end.", output.Text);
    }

    [Fact]
    public async Task RejectsAMinimumLengthBelowTheFloor()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("plain.txt", "plain text");

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path, minimumLength = 2 })));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Argument 'minimumLength' must be an integer between 4 and 1024.", output.Text);
    }

    [Fact]
    public async Task RejectsAnUnknownEncoding()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("plain.txt", "plain text");

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path, encoding = "utf8" })));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Argument 'encoding' must be 'ascii' or 'utf16le'.", output.Text);
    }
}
