using System.Globalization;
using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Tools.Binaries.Tools;

namespace CrystalCode.Tools.Binaries.Tests.Tools;

public sealed class BinaryReadToolTests
{
    [Theory]
    [InlineData("u8", "ff", "255")]
    [InlineData("i8", "ff", "-1")]
    [InlineData("u16", "ffff", "65535")]
    [InlineData("i16", "0080", "-32768")]
    [InlineData("u32", "ffffffff", "4294967295")]
    [InlineData("i32", "00000080", "-2147483648")]
    [InlineData("u64", "ffffffffffffffff", "18446744073709551615")]
    [InlineData("i64", "0000000000000080", "-9223372036854775808")]
    [InlineData("f32", "0000c03f", "1.5")]
    [InlineData("f64", "000000000000f83f", "1.5")]
    [InlineData("f32", "0000807f", "Infinity")]
    [InlineData("f64", "000000000000f87f", "NaN")]
    public async Task ReadsNumericTypesInBothByteOrders(string type, string hex, string expected)
    {
        using var files = new TemporaryFiles();
        foreach (var endianness in new[] { "little", "big" })
        {
            var bytes = Convert.FromHexString(hex);
            if (endianness == "big")
            {
                Array.Reverse(bytes);
            }

            var path = files.Write("number.bin", bytes);
            var output = await new BinaryReadTool().InvokeAsync(new ToolCall("1", "binread",
                JsonSerializer.Serialize(new { path, type, endianness }), ToolInputKind.Json));

            Assert.Equal(ToolResultStatus.Success, output.Status);
            Assert.EndsWith($"offset 0: {Convert.ToHexStringLower(bytes)} = {expected}", output.Text);
        }
    }

    [Fact]
    public async Task ReadsMultipleUnalignedValuesAndUsesInvariantFormatting()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("numbers.bin", [0, 0, 0, 0xC0, 0x3F, 0, 0, 0x20, 0x40]);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var output = await new BinaryReadTool().InvokeAsync(new ToolCall("1", "binread",
                JsonSerializer.Serialize(new { path, type = "f32", offset = 1, count = 2 }), ToolInputKind.Json));
            Assert.Equal(ToolResultStatus.Success, output.Status);
            Assert.Contains("offset 1: 0000c03f = 1.5", output.Text);
            Assert.Contains("offset 5: 00002040 = 2.5", output.Text);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("{\"type\":\"u16\",\"count\":0}")]
    [InlineData("{\"type\":\"u16\",\"count\":1025}")]
    [InlineData("{\"type\":\"u16\",\"offset\":-1}")]
    [InlineData("{\"type\":\"u16\",\"endianness\":\"native\"}")]
    [InlineData("{\"type\":\"u128\"}")]
    [InlineData("{}")]
    [InlineData("{\"type\":null}")]
    public async Task RejectsInvalidArguments(string arguments)
    {
        var json = arguments == "{}" ? "{\"path\":\"unused\"}" : "{\"path\":\"unused\"," + arguments[1..];
        var output = await new BinaryReadTool().InvokeAsync(new ToolCall("1", "binread", json, ToolInputKind.Json));
        Assert.Equal(ToolResultStatus.Failure, output.Status);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(long.MaxValue, 1)]
    public async Task RejectsReadsPastEndWithoutPartialOutput(long offset, int count)
    {
        using var files = new TemporaryFiles();
        var path = files.Write("short.bin", [1, 2]);
        var output = await new BinaryReadTool().InvokeAsync(new ToolCall("1", "binread",
            JsonSerializer.Serialize(new { path, type = "u16", offset, count }), ToolInputKind.Json));
        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.DoesNotContain("offset ", output.Text);
    }

    [Fact]
    public async Task BoundsOutputAtMaximumCount()
    {
        using var files = new TemporaryFiles();
        var path = files.Write("many.bin", new byte[8192]);
        var output = await new BinaryReadTool().InvokeAsync(new ToolCall("1", "binread",
            JsonSerializer.Serialize(new { path, type = "u64", count = 1024 }), ToolInputKind.Json));
        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(1024, output.Text.Split('\n').Count(line => line.StartsWith("offset ", StringComparison.Ordinal)));
        Assert.True(output.Text.Length < 90_000);
    }

    [Fact]
    public void ExposesDefinitionAndRequiredArguments()
    {
        var definition = new BinaryReadTool().Definition;
        var input = Assert.IsType<JsonToolInput>(definition.Input);
        Assert.Equal("binread", definition.Name);
        Assert.Equal(ToolInputKind.Json, input.Kind);
        Assert.False(input.Schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["path", "type"], input.Schema.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString()!).ToArray());
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await new BinaryReadTool().InvokeAsync(new ToolCall("1", "binread", "{}", ToolInputKind.Json), cancellation.Token));
    }
}
