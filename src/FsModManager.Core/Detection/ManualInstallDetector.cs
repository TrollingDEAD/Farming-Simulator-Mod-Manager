using FsModManager.Core.Models;

namespace FsModManager.Core.Detection;

/// <summary>
/// Helper for manually supplied install folders (e.g. a folder picked by the user in the UI).
/// Manual installs are not auto-detected; this only validates the folder and builds the model.
/// </summary>
public static class ManualInstallDetector
{
    private const string ExeName = "FarmingSimulator2025.exe";

    /// <summary>
    /// Returns a <see cref="GameInstallation"/> with <see cref="GameLaunchSource.Unknown"/>
    /// if <paramref name="folderPath"/> contains FarmingSimulator2025.exe; otherwise null.
    /// </summary>
    public static GameInstallation? FromManualPath(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return null;
        }

        var exePath = Path.Combine(folderPath, ExeName);
        if (!File.Exists(exePath))
        {
            return null;
        }

        return new GameInstallation(
            exePath,
            folderPath,
            GameVersionReader.GetVersion(exePath) ?? "Unknown",
            GameLaunchSource.Unknown);
    }
}
