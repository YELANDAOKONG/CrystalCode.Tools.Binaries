using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Tools.Binaries.Tools;

namespace CrystalCode.Tools.Binaries.Tests.Tools;

public sealed class HashToolTests
{
    private const string AbcSha256 =
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void ExposesTheHashDefinition()
    {
        var tool = new HashTool();

        Assert.Equal("hash", tool.Definition.Name);
        Assert.NotNull(tool.Definition.Description);
        using var schema = JsonDocument.Parse(tool.Definition.InputSchema.GetRawText());
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            "path",
            schema.RootElement.GetProperty("required").EnumerateArray().Single().GetString());
    }

    [Fact]
    public async Task ComputesTheDefaultSha256()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("abc.txt", "abc");

        var output = await new HashTool().InvokeAsync(
            new ToolCall("1", "hash", JsonSerializer.Serialize(new { path })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal($"{path}: 3 bytes\nsha256: {AbcSha256}", output.Text);
    }

    [Fact]
    public async Task ComputesMd5WhenAsked()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("abc.txt", "abc");

        var output = await new HashTool().InvokeAsync(
            new ToolCall("1", "hash", JsonSerializer.Serialize(new { path, algorithm = "md5" })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 3 bytes\nmd5: 900150983cd24fb0d6963f7d28e17f72",
            output.Text);
    }

    [Fact]
    public async Task MatchesAnExpectedDigestCaseInsensitively()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("abc.txt", "abc");

        var output = await new HashTool().InvokeAsync(
            new ToolCall(
                "1",
                "hash",
                JsonSerializer.Serialize(new { path, expected = AbcSha256.ToUpperInvariant() })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 3 bytes\n"
            + $"sha256: {AbcSha256}\n"
            + $"expected: {AbcSha256.ToUpperInvariant()}\n"
            + "match: yes",
            output.Text);
    }

    [Fact]
    public async Task ReportsAMismatchWithoutFailing()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("abc.txt", "abc");

        var output = await new HashTool().InvokeAsync(
            new ToolCall(
                "1",
                "hash",
                JsonSerializer.Serialize(new { path, expected = new string('0', 64) })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains($"expected: {new string('0', 64)}", output.Text, StringComparison.Ordinal);
        Assert.Contains("match: no", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HashesAnEmptyFile()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("empty.bin", []);

        var output = await new HashTool().InvokeAsync(
            new ToolCall("1", "hash", JsonSerializer.Serialize(new { path })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(
            $"{path}: 0 bytes\n"
            + "sha256: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            output.Text);
    }

    [Fact]
    public async Task RejectsAMissingFile()
    {
        using var files = new TemporaryFiles();
        var path = Path.Combine(files.Root, "missing.bin");

        var output = await new HashTool().InvokeAsync(
            new ToolCall("1", "hash", JsonSerializer.Serialize(new { path })));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal($"The file was not found: {path}.", output.Text);
    }

    [Fact]
    public async Task RejectsAnUnknownAlgorithm()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("abc.txt", "abc");

        var output = await new HashTool().InvokeAsync(
            new ToolCall("1", "hash", JsonSerializer.Serialize(new { path, algorithm = "crc32" })));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal(
            "Argument 'algorithm' must be 'sha256', 'sha512', 'sha1', or 'md5'.",
            output.Text);
    }
}
