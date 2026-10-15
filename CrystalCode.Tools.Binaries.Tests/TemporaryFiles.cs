using System.Text;

namespace CrystalCode.Tools.Binaries.Tests;

/// <summary>
/// Creates and removes one temporary directory of files for a test.
/// </summary>
internal sealed class TemporaryFiles : IDisposable
{
    public TemporaryFiles()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "crystal-binaries-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Write(string name, byte[] contents)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllBytes(path, contents);
        return path;
    }

    public string WriteText(string name, string contents) =>
        Write(name, Encoding.UTF8.GetBytes(contents));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
