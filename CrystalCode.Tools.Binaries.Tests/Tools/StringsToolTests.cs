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
        var input = Assert.IsType<JsonToolInput>(tool.Definition.Input);
        Assert.Equal(ToolInputKind.Json, input.Kind);
        using var schema = JsonDocument.Parse(input.Schema.GetRawText());
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            "path",
            schema.RootElement.GetProperty("required").EnumerateArray().Single().GetString());
    }

    [Fact]
    public async Task ExtractsUtf8WithByteAccurateContinuation()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("utf8.bin", Encoding.UTF8.GetBytes("中文测试\0更多文本"));
        var output = await new StringsTool().InvokeAsync(new ToolCall("1", "strings",
            JsonSerializer.Serialize(new { path, encoding = "utf8", minimumLength = 4, limit = 1 }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("offset 0: 中文测试", output.Text);
        Assert.EndsWith("[showing 1 runs; continue with offset 12]", output.Text);
        var next = await new StringsTool().InvokeAsync(new ToolCall("2", "strings",
            JsonSerializer.Serialize(new { path, encoding = "utf8", minimumLength = 4, offset = 12 }), ToolInputKind.Json));
        Assert.Contains("offset 13: 更多文本", next.Text);
    }

    [Fact]
    public async Task Utf8PreviewDoesNotSplitSurrogatePairs()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("utf8.bin", Encoding.UTF8.GetBytes(new string('A', 199) + "😀end"));
        var output = await new StringsTool().InvokeAsync(new ToolCall("1", "strings",
            JsonSerializer.Serialize(new { path, encoding = "utf8" }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.EndsWith(new string('A', 199) + "...", output.Text);
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
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path }), ToolInputKind.Json));

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
                JsonSerializer.Serialize(new { path, encoding = "utf16le", minimumLength = 4 }), ToolInputKind.Json));

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
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path }), ToolInputKind.Json));

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
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path, limit = 2 }), ToolInputKind.Json));

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
    public async Task BoundsTheResultAndNotesWhereToContinue()
    {
        using var files = new TemporaryFiles();
        var bytes = new List<byte>();
        for (var index = 0; index < 500; index++)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(new string('A', 200)));
            bytes.Add(0x00);
        }

        var path = files.Write("long-runs.bin", [.. bytes]);

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path, limit = 500 }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.True(output.Text.Length < 100_000, "The host truncates at 100,000 characters.");
        var lines = output.Text.Split('\n');
        var tokens = lines[^1].TrimStart('[').TrimEnd(']').Split(' ');
        Assert.Equal("showing", tokens[0]);
        Assert.Equal("runs;", tokens[2]);
        var shown = int.Parse(tokens[1]);
        var continueAt = long.Parse(tokens[^1]);
        Assert.InRange(shown, 1, 499);
        Assert.Equal(201L * shown - 1, continueAt);
        var runLines = lines
            .Where(line => line.StartsWith("offset ", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(shown, runLines.Length);
        Assert.StartsWith("offset 0: ", runLines[0], StringComparison.Ordinal);
        Assert.StartsWith($"offset {(shown - 1) * 201}: ", runLines[^1], StringComparison.Ordinal);

        var total = shown;
        var offset = continueAt;
        while (true)
        {
            var page = await new StringsTool().InvokeAsync(
                new ToolCall(
                    "1",
                    "strings",
                    JsonSerializer.Serialize(new { path, limit = 500, offset }), ToolInputKind.Json));
            Assert.Equal(ToolResultStatus.Success, page.Status);
            var pageLines = page.Text.Split('\n');
            total += pageLines.Count(line => line.StartsWith("offset ", StringComparison.Ordinal));
            if (!pageLines[^1].StartsWith("[showing ", StringComparison.Ordinal))
            {
                break;
            }

            offset = long.Parse(pageLines[^1].TrimStart('[').TrimEnd(']').Split(' ')[^1]);
        }

        Assert.Equal(500, total);
    }

    [Theory]
    [InlineData("ascii")]
    [InlineData("utf16le")]
    public async Task TruncatesLongRunsAndPreservesPagination(string encoding)
    {
        using var files = new TemporaryFiles();
        const int characterCount = 100_000;
        var encoder = encoding == "utf16le" ? Encoding.Unicode : Encoding.ASCII;
        var path = files.Write("long.bin", encoder.GetBytes(new string('A', characterCount) + "\0BBBB"));

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings",
                JsonSerializer.Serialize(new { path, encoding, minimumLength = 4, limit = 1 }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("offset 0: " + new string('A', 200) + "...\n", output.Text);
        Assert.EndsWith($"[showing 1 runs; continue with offset {encoder.GetByteCount(new string('A', characterCount))}]",
            output.Text);
    }

    [Fact]
    public async Task ReportsNoRuns()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("blank.bin", [0x00, 0x01, 0x02, 0x00, 0x03]);

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path }), ToolInputKind.Json));

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
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path, offset = 10 }), ToolInputKind.Json));

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
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal($"{path}: 0 bytes (empty file)", output.Text);
    }

    [Fact]
    public async Task RejectsAnOffsetPastTheEnd()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("five.bin", "abcde");

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path, offset = 6 }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal($"{path} has 5 bytes; offset 6 is past the end.", output.Text);
    }

    [Fact]
    public async Task RejectsAMinimumLengthBelowTheFloor()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("plain.txt", "plain text");

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path, minimumLength = 2 }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Argument 'minimumLength' must be an integer between 4 and 1024.", output.Text);
    }

    [Fact]
    public async Task RejectsAnUnknownEncoding()
    {
        using var files = new TemporaryFiles();
        var path = files.WriteText("plain.txt", "plain text");

        var output = await new StringsTool().InvokeAsync(
            new ToolCall("1", "strings", JsonSerializer.Serialize(new { path, encoding = "utf32" }), ToolInputKind.Json));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Argument 'encoding' must be 'ascii', 'utf16le', or 'utf8'.", output.Text);
    }
}
