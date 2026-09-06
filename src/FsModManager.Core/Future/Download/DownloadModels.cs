namespace FsModManager.Core.Models;

/// <summary>Progress information reported by <see cref="FsModManager.Core.Download.IDownloadManager"/>.</summary>
public sealed record DownloadProgressInfo(long BytesReceived, long? TotalBytes)
{
    public double? ProgressPercentage =>
        TotalBytes is > 0 ? Math.Round(BytesReceived * 100d / TotalBytes.Value, 1) : null;
}

/// <summary>Outcome of a completed download + staging + hash-verify + move operation.</summary>
public sealed record DownloadResult(
    bool Success,
    string? FinalFilePath,
    string? Sha256Hash,
    string? ErrorMessage);
