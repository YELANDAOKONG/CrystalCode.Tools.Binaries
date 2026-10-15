using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Tools.Binaries.Tools;

namespace CrystalCode.Tools.Binaries.Tests.Tools;

public sealed class HashToolTests
{
    private const string AbcSha256 =
        "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private const string AbcSha384 =
        "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed"
        + "8086072ba1e7cc2358baeca134c825a7";

    private const string AbcSha3_256 =
        "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532";

    private const string AbcSha3_384 =
        "ec01498288516fc926459f58e2c6ad8df9b473cb0fc08c2596da7cf0e49be4b2"
        + "98d88cea927ac7f539f1edf228376d25";

    private const string AbcSha3_512 =
        "b751850b1a57168a5693cd924b6b096e08f621827444f70d884f5d0240d2712e"
        + "10e116e9192af3c91a7ec57647e3934057340b4cf408d5a56592f8274eec53f0";

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
    public async Task ComputesSha384WhenAsked()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("abc.txt", "abc");

        var output = await new HashTool().InvokeAsync(
            new ToolCall("1", "hash", JsonSerializer.Serialize(new { path, algorithm = "sha384" })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal($"{path}: 3 bytes\nsha384: {AbcSha384}", output.Text);
    }

    [Fact]
    public async Task ComputesSha3_256WhenAsked()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("abc.txt", "abc");

        var output = await new HashTool().InvokeAsync(
            new ToolCall("1", "hash", JsonSerializer.Serialize(new { path, algorithm = "sha3-256" })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal($"{path}: 3 bytes\nsha3-256: {AbcSha3_256}", output.Text);
    }

    [Fact]
    public async Task ComputesSha3_384WhenAsked()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("abc.txt", "abc");

        var output = await new HashTool().InvokeAsync(
            new ToolCall("1", "hash", JsonSerializer.Serialize(new { path, algorithm = "sha3-384" })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal($"{path}: 3 bytes\nsha3-384: {AbcSha3_384}", output.Text);
    }

    [Fact]
    public async Task ComputesSha3_512WhenAsked()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("abc.txt", "abc");

        var output = await new HashTool().InvokeAsync(
            new ToolCall("1", "hash", JsonSerializer.Serialize(new { path, algorithm = "sha3-512" })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal($"{path}: 3 bytes\nsha3-512: {AbcSha3_512}", output.Text);
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
            "Argument 'algorithm' must be 'sha256', 'sha384', 'sha512', 'sha1', 'md5', "
            + "'sha3-256', 'sha3-384', or 'sha3-512'.",
            output.Text);
    }
}
