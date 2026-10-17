using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Tools.Binaries.Tools;

namespace CrystalCode.Tools.Binaries.Tests.Tools;

public sealed class HexDumpToolTests
{
    [Fact]
    public void ExposesTheHexdumpDefinition()
    {
        var tool = new HexDumpTool();

        Assert.Equal("hexdump", tool.Definition.Name);
        Assert.NotNull(tool.Definition.Description);
        var input = Assert.IsType<JsonToolInput>(tool.Definition.Input);
        Assert.Equal(ToolInputKind.Json, input.Kind);
        using var schema = JsonDocument.Parse(input.Schema.GetRawText());
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            "path",
            schema.RootElement.GetProperty("required").EnumerateArray().Single().GetString());
    }

    [Fact]
    public async Task DumpsOneFullRow()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("row.bin", "Hello World!1234");

        var output = await new HexDumpTool().InvokeAsync(
            new ToolCall("1", "hexdump", JsonSerializer.Serialize(new { path }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 16 bytes\n"
            + "00000000  48 65 6c 6c 6f 20 57 6f  72 6c 64 21 31 32 33 34  "
            + "|Hello World!1234|",
            output.Text);
    }

    [Fact]
    public async Task ReplacesNonPrintableBytesInTheAsciiColumn()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("raw.bin", [0x00, 0x01, 0x41]);

        var output = await new HexDumpTool().InvokeAsync(
            new ToolCall("1", "hexdump", JsonSerializer.Serialize(new { path }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("00 01 41", output.Text, StringComparison.Ordinal);
        Assert.Contains("|..A|", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PagesAWindowAndNotesWhereToContinue()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("big.bin", new byte[3000]);

        var output = await new HexDumpTool().InvokeAsync(
            new ToolCall(
                "1",
                "hexdump",
                JsonSerializer.Serialize(new { path, offset = 2000, length = 100 }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains($"{path}: 3000 bytes", output.Text, StringComparison.Ordinal);
        Assert.Contains("000007d0", output.Text, StringComparison.Ordinal);
        Assert.Contains(
            "[showing 100 of 3000 bytes; continue with offset 2100]",
            output.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsAnEmptyFile()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("empty.bin", []);

        var output = await new HexDumpTool().InvokeAsync(
            new ToolCall("1", "hexdump", JsonSerializer.Serialize(new { path }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal($"{path}: 0 bytes (empty file)", output.Text);
    }

    [Fact]
    public async Task RejectsAnOffsetPastTheEnd()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("ten.bin", "0123456789");

        var output = await new HexDumpTool().InvokeAsync(
            new ToolCall("1", "hexdump", JsonSerializer.Serialize(new { path, offset = 10 }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal($"{path} has 10 bytes; offset 10 is past the end.", output.Text);
    }

    [Fact]
    public async Task RejectsADirectory()
    {
        using var files = new TemporaryFiles();

        var output = await new HexDumpTool().InvokeAsync(
            new ToolCall("1", "hexdump", JsonSerializer.Serialize(new { path = files.Root }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal($"The path is a directory: {files.Root}.", output.Text);
    }

    [Fact]
    public async Task RejectsAMissingFile()
    {
        using var files = new TemporaryFiles();
        var path = Path.Combine(files.Root, "missing.bin");

        var output = await new HexDumpTool().InvokeAsync(
            new ToolCall("1", "hexdump", JsonSerializer.Serialize(new { path }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal($"The file was not found: {path}.", output.Text);
    }

    [Fact]
    public async Task ReadsAFileThatAnotherHandleKeepsOpenForWriting()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("shared.bin", "hello");
        using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);

        var output = await new HexDumpTool().InvokeAsync(
            new ToolCall("1", "hexdump", JsonSerializer.Serialize(new { path }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("|hello|", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsUnknownArguments()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("row.bin", "hi");

        var output = await new HexDumpTool().InvokeAsync(
            new ToolCall("1", "hexdump", JsonSerializer.Serialize(new { path, mode = "raw" }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Tool arguments must contain only path, offset, and length.", output.Text);
    }

    [Fact]
    public async Task RejectsALengthAboveTheMaximum()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("row.bin", "hi");

        var output = await new HexDumpTool().InvokeAsync(
            new ToolCall("1", "hexdump", JsonSerializer.Serialize(new { path, length = 20000 }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Argument 'length' must be an integer between 1 and 16384.", output.Text);
    }

    [Fact]
    public async Task RejectsArgumentsThatAreNotJson()
    {
        var output = await new HexDumpTool().InvokeAsync(
            new ToolCall("1", "hexdump", "not json", ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Tool arguments are not valid JSON.", output.Text);
    }

    [Fact]
    public async Task CanceledToken_ThrowsOperationCanceledException()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("row.bin", "hi");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new HexDumpTool().InvokeAsync(
                new ToolCall("1", "hexdump", JsonSerializer.Serialize(new { path }), ToolInputKind.Json),
                cancellation.Token));
    }
}
