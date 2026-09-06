namespace FsModManager.Core.Models;

public sealed record GameInstallation(
    string ExecutablePath,
    string InstallDirectory,
    string Version,
    GameLaunchSource Source);

public enum GameLaunchSource
{
    Steam,
    EpicGames,
    Gog,
    GiantsLauncher,
    Unknown,
}

public sealed record ModFileInfo(
    string ZipPath,
    string InternalModName,
    string? Version,
    string? Author,
    bool DescVersionParsed,
    IReadOnlyList<string> ParseWarnings,
    string DisplayTitle,
    byte[]? IconImageData,
    bool? MultiplayerSupported = null,
    IReadOnlyList<string>? Categories = null,
    CrossplayStatus CrossplayStatus = CrossplayStatus.Unknown,
    string? Description = null,
    string? DescVersion = null,
    long FileSizeBytes = 0,
    DateTime LastModifiedUtc = default,
    ModKind ModKind = ModKind.Unknown,
    bool IsFilenameValid = true,
    string? InvalidFilenameReason = null);

public sealed record SavegameInfo(string FolderPath, string DisplayName, int SlotIndex);

public sealed record ModLoadEntry(string InternalModName, bool Active, int Order);