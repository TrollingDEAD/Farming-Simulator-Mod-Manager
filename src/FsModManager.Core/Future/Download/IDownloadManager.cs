using FsModManager.Core.Models;

namespace FsModManager.Core.Download;

/// <summary>
/// Downloads a mod archive to a staging folder (supporting resumable downloads via HTTP Range
/// requests), hash-verifies it, and only then moves it into the user's FS25 mods folder.
/// </summary>
public interface IDownloadManager
{
    Task<DownloadResult> DownloadModAsync(
        Uri downloadUrl,
        string destinationFileName,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken cancellationToken = default);
}
