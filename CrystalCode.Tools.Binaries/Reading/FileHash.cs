using System.Security.Cryptography;

namespace CrystalCode.Tools.Binaries.Reading;

/// <summary>
/// Streams one file through a digest algorithm.
/// </summary>
internal static class FileHash
{
    public static bool TryGetAlgorithm(string name, out HashAlgorithmName algorithm)
    {
        switch (name)
        {
            case "sha256":
                algorithm = HashAlgorithmName.SHA256;
                return true;
            case "sha384":
                algorithm = HashAlgorithmName.SHA384;
                return true;
            case "sha512":
                algorithm = HashAlgorithmName.SHA512;
                return true;
            case "sha1":
                algorithm = HashAlgorithmName.SHA1;
                return true;
            case "md5":
                algorithm = HashAlgorithmName.MD5;
                return true;
            case "sha3-256":
                algorithm = HashAlgorithmName.SHA3_256;
                return true;
            case "sha3-384":
                algorithm = HashAlgorithmName.SHA3_384;
                return true;
            case "sha3-512":
                algorithm = HashAlgorithmName.SHA3_512;
                return true;
            default:
                algorithm = default;
                return false;
        }
    }

    public static async Task<(string Hex, long Bytes)> ComputeAsync(
        Stream stream,
        HashAlgorithmName algorithm,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var hash = IncrementalHash.CreateHash(algorithm);
        var buffer = new byte[BinaryLimits.BufferBytes];
        long bytes = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, read);
            bytes += read;
        }

        return (Convert.ToHexStringLower(hash.GetHashAndReset()), bytes);
    }
}
