using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Tools.Binaries.Tools;

namespace CrystalCode.Tools.Binaries.Tests.Tools;

public sealed class CompareToolTests
{
    [Fact]
    public void ExposesTheCompareDefinition()
    {
        var tool = new CompareTool();

        Assert.Equal("compare", tool.Definition.Name);
        Assert.NotNull(tool.Definition.Description);
        using var schema = JsonDocument.Parse(tool.Definition.InputSchema.GetRawText());
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["path", "other"],
            schema.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    [Fact]
    public async Task ReportsIdenticalFiles()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("a.bin", "same bytes");
        var other = files.WriteText("b.bin", "same bytes");

        var output = await new CompareTool().InvokeAsync(
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 10 bytes\n"
            + $"{other}: 10 bytes\n"
            + "identical: yes",
            output.Text);
    }

    [Fact]
    public async Task ReportsTheFirstDifference()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("a.bin", "hello world");
        var other = files.WriteText("b.bin", "hello worlD");

        var output = await new CompareTool().InvokeAsync(
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 11 bytes\n"
            + $"{other}: 11 bytes\n"
            + "identical: no\n"
            + "first difference: offset 10 (64 vs 44)\n"
            + "differing bytes: 1 of the first 11 bytes",
            output.Text);
    }

    [Fact]
    public async Task ReportsALengthDifferenceOnly()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("a.bin", "abc");
        var other = files.WriteText("b.bin", "abcde");

        var output = await new CompareTool().InvokeAsync(
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 3 bytes\n"
            + $"{other}: 5 bytes\n"
            + "identical: no\n"
            + "first difference: none in the first 3 bytes\n"
            + $"lengths differ: {other} is 2 bytes longer",
            output.Text);
    }

    [Fact]
    public async Task ReportsADifferenceAndALengthDifference()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("a.bin", "abX");
        var other = files.WriteText("b.bin", "abcde");

        var output = await new CompareTool().InvokeAsync(
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 3 bytes\n"
            + $"{other}: 5 bytes\n"
            + "identical: no\n"
            + "first difference: offset 2 (58 vs 63)\n"
            + "differing bytes: 1 of the first 3 bytes\n"
            + $"lengths differ: {other} is 2 bytes longer",
            output.Text);
    }

    [Fact]
    public async Task ReportsTwoEmptyFilesAsIdentical()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("a.bin", []);
        var other = files.Write("b.bin", []);

        var output = await new CompareTool().InvokeAsync(
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 0 bytes\n"
            + $"{other}: 0 bytes\n"
            + "identical: yes",
            output.Text);
    }

    [Fact]
    public async Task ReportsAnEmptyFileAgainstANonEmptyFile()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("a.bin", []);
        var other = files.WriteText("b.bin", "abc");

        var output = await new CompareTool().InvokeAsync(
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 0 bytes\n"
            + $"{other}: 3 bytes\n"
            + "identical: no\n"
            + $"lengths differ: {other} is 3 bytes longer",
            output.Text);
    }

    [Fact]
    public async Task RejectsAMissingSecondFile()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("a.bin", "abc");
        var other = Path.Combine(files.Root, "missing.bin");

        var output = await new CompareTool().InvokeAsync(
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other })));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal($"The file was not found: {other}.", output.Text);
    }

    [Fact]
    public async Task ReportsTheSecondFileWhenOnlyItCannotBeRead()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("a.bin", "abc");
        var other = files.WriteText("b.bin", "abc");
        using var reader = new FileStream(other, FileMode.Open, FileAccess.Read, FileShare.None);

        var output = await new CompareTool().InvokeAsync(
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other })));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal($"The file could not be read: {other}.", output.Text);
    }

    [Fact]
    public async Task RejectsADirectoryAsTheSecondFile()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("a.bin", "abc");

        var output = await new CompareTool().InvokeAsync(
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other = files.Root })));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal($"The path is a directory: {files.Root}.", output.Text);
    }
}
