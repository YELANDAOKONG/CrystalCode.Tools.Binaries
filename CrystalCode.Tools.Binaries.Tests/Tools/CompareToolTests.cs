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
        var input = Assert.IsType<JsonToolInput>(tool.Definition.Input);
        Assert.Equal(ToolInputKind.Json, input.Kind);
        using var schema = JsonDocument.Parse(input.Schema.GetRawText());
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            ["path", "other"],
            schema.RootElement.GetProperty("required").EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    [Fact]
    public async Task ListsRangesAndContinuesWithoutRepeatingThem()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("a.bin", [1, 2, 3, 4, 5]);
        var other = files.Write("b.bin", [1, 9, 8, 4, 7, 6]);
        var tool = new CompareTool();
        var first = await tool.InvokeAsync(new ToolCall("1", "compare",
            JsonSerializer.Serialize(new { path, other, mode = "ranges", limit = 1 }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Success, first.Status);
        Assert.Contains("offset 1: 2 differing bytes\n  first (2 bytes): 0203\n  second (2 bytes): 0908", first.Text);
        Assert.EndsWith("[showing 1 ranges; continue with offset 3]", first.Text);

        var next = await tool.InvokeAsync(new ToolCall("2", "compare",
            JsonSerializer.Serialize(new { path, other, mode = "ranges", offset = 3 }), ToolInputKind.Json));
        Assert.Contains("offset 4: 2 differing bytes\n  first (1 bytes): 05\n  second (2 bytes): 0706", next.Text);
        Assert.DoesNotContain("continue with", next.Text);
    }

    [Fact]
    public async Task MergesRangesAcrossChunksAndBoundsPreviews()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("a.bin", new byte[131073]);
        var bytes = new byte[131073];
        Array.Fill(bytes, (byte)1, 65535, 65537);
        var other = files.Write("b.bin", bytes);
        var output = await new CompareTool().InvokeAsync(new ToolCall("1", "compare",
            JsonSerializer.Serialize(new { path, other, mode = "ranges" }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("offset 65535: 65537 differing bytes", output.Text);
        Assert.Contains(" ...", output.Text);
        Assert.True(output.Text.Length < 1000);
    }

    [Theory]
    [InlineData(0, 1, "No differences")]
    [InlineData(1, 1, "offset 1: 1 differing bytes")]
    [InlineData(2, long.MaxValue, "offset 2: 2 differing bytes")]
    public async Task BoundsRangeWindows(long offset, long length, string expected)
    {
        using var files = new TemporaryFiles();
        var path = files.Write("a.bin", [1, 2]);
        var other = files.Write("b.bin", [1, 3, 4, 5]);
        var output = await new CompareTool().InvokeAsync(new ToolCall("1", "compare",
            JsonSerializer.Serialize(new { path, other, mode = "ranges", offset, length }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains(expected, output.Text);
        if (offset == 2)
        {
            Assert.Contains("first (0 bytes): <absent>", output.Text);
        }
    }

    [Theory]
    [InlineData("{\"mode\":\"ranges\",\"limit\":0}")]
    [InlineData("{\"mode\":\"ranges\",\"limit\":101}")]
    [InlineData("{\"mode\":\"ranges\",\"offset\":-1}")]
    [InlineData("{\"mode\":\"ranges\",\"length\":0}")]
    [InlineData("{\"offset\":1}")]
    public async Task RejectsInvalidRangeArguments(string arguments)
    {
        var json = "{\"path\":\"unused\",\"other\":\"unused\"," + arguments[1..];
        var output = await new CompareTool().InvokeAsync(new ToolCall("1", "compare", json, ToolInputKind.Json));
        Assert.Equal(ToolResultStatus.Failure, output.Status);
    }

    [Fact]
    public async Task ReportsIdenticalFiles()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("a.bin", "same bytes");
        var other = files.WriteText("b.bin", "same bytes");

        var output = await new CompareTool().InvokeAsync(
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other }), ToolInputKind.Json));

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
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other }), ToolInputKind.Json));

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
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other }), ToolInputKind.Json));

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
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other }), ToolInputKind.Json));

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
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other }), ToolInputKind.Json));

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
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other }), ToolInputKind.Json));

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
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other }), ToolInputKind.Json));

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
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal($"The file could not be read: {other}.", output.Text);
    }

    [Fact]
    public async Task RejectsADirectoryAsTheSecondFile()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("a.bin", "abc");

        var output = await new CompareTool().InvokeAsync(
            new ToolCall("1", "compare", JsonSerializer.Serialize(new { path, other = files.Root }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal($"The path is a directory: {files.Root}.", output.Text);
    }
}
