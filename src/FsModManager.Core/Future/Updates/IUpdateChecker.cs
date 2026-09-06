using FsModManager.Core.Data.Entities;

namespace FsModManager.Core.Updates;

/// <summary>Raised when <see cref="IUpdateChecker"/> finds a mod with a newer version available.</summary>
public sealed record ModUpdateAvailableEventArgs(InstalledMod Mod, string LatestVersion, string? DownloadUrl);

public interface IUpdateChecker
{
    event EventHandler<ModUpdateAvailableEventArgs>? NewVersionAvailable;
}
