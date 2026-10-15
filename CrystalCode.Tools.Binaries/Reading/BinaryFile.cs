namespace CrystalCode.Tools.Binaries.Reading;

/// <summary>
/// Opens regular files for reading with consistent model-visible errors.
/// </summary>
internal static class BinaryFile
{
    /// <summary>
    /// Returns a model-visible error when the path is not a readable regular
    /// file, or null when it may be opened.
    /// </summary>
    public static string? Validate(string path)
    {
        if (Directory.Exists(path))
        {
            return $"The path is a directory: {path}.";
        }

        if (!File.Exists(path))
        {
            return $"The file was not found: {path}.";
        }

        return null;
    }

    public static FileStream Open(string path) =>
        new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous
        });

    /// <summary>
    /// Fills the buffer up to its length unless the stream ends first, then
    /// returns the number of bytes read.
    /// </summary>
    public static async ValueTask<int> ReadAtMostAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer[total..], cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
