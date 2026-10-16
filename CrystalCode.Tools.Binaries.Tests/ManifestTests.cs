using System.Text.Json;

using CrystalCode.Tools.Binaries.Tools;

namespace CrystalCode.Tools.Binaries.Tests;

public sealed class ManifestTests
{
    [Fact]
    public void ShipsAManifestThatMatchesTheTools()
    {
        var path = Path.Combine(FindRepositoryRoot(), "tools.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal("dotnet", root.GetProperty("runner").GetString());
        Assert.Equal("CrystalCode.Tools.Binaries.dll", root.GetProperty("assembly").GetString());
        Assert.Equal(
            [
                typeof(HexDumpTool).FullName!,
                typeof(StringsTool).FullName!,
                typeof(HashTool).FullName!,
                typeof(BinarySearchTool).FullName!,
                typeof(CompareTool).FullName!,
                typeof(BinaryReadTool).FullName!
            ],
            root.GetProperty("types").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Equal(
            ["plan", "work"],
            root.GetProperty("catalogs").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Equal("always", root.GetProperty("approval").GetString());
        Assert.True(root.GetProperty("timeoutSeconds").GetInt32() > 0);
    }

    [Fact]
    public void DeclaresThePathArgumentsOfEveryTool()
    {
        var path = Path.Combine(FindRepositoryRoot(), "tools.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var tools = document.RootElement.GetProperty("tools");

        Assert.Equal(
            ["path"],
            PathArguments(tools, "hexdump"));
        Assert.Equal(
            ["path"],
            PathArguments(tools, "strings"));
        Assert.Equal(
            ["path"],
            PathArguments(tools, "hash"));
        Assert.Equal(
            ["path"],
            PathArguments(tools, "binsearch"));
        Assert.Equal(
            ["path", "other"],
            PathArguments(tools, "compare"));
        Assert.Equal(["path"], PathArguments(tools, "binread"));
    }

    private static string[] PathArguments(JsonElement tools, string name) =>
        tools.GetProperty(name)
            .GetProperty("pathArguments")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !(File.Exists(Path.Combine(directory.FullName, "tools.json"))
                && File.Exists(Path.Combine(directory.FullName, "CrystalCode.Tools.Binaries.sln"))))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
