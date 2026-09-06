using System.Security.Cryptography;

namespace FsModManager.Core.Hashing;

/// <summary>
/// The one place SHA-256 file hashes are computed (streamed, uppercase hex). Keeping a single
/// implementation guarantees that <see cref="FsModManager.Core.Models.ModMetadata.FileHash"/>,
/// download verification, and multiplayer manifest content hashes are byte-identical for the
/// same file — if any caller "optimized" its own copy differently, cross-feature comparisons
/// would silently disagree.
/// </summary>
internal static class FileHashing
{
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes);
    }
}
