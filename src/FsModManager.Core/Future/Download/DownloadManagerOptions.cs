namespace FsModManager.Core.Download;

/// <summary>Folder configuration for <see cref="IDownloadManager"/>.</summary>
public sealed class DownloadManagerOptions
{
    /// <summary>Where partially/fully downloaded files are staged before hash verification.</summary>
    public string StagingFolderPath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FsModManager", "staging");

    /// <summary>The FS25 mods folder that verified downloads are moved into.</summary>
    public string ModsFolderPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Personal),
        "My Games", "FarmingSimulator2025", "mods");
}
