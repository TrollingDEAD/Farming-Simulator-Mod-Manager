using System.Diagnostics;

namespace FsModManager.Core.Detection;

/// <summary>
/// Reads the file version of a game executable. Launcher-agnostic — every detector
/// uses this to fill <see cref="Models.GameInstallation.Version"/> regardless of source.
/// </summary>
public static class GameVersionReader
{
    /// <summary>
    /// Returns the file version of the executable, or null if the file is missing,
    /// has no version resource, or cannot be read.
    /// </summary>
    public static string? GetVersion(string executablePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return null;
            }

            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return info.FileVersion ?? info.ProductVersion;
        }
        catch (Exception)
        {
            // Unreadable binaries must not break detection.
            return null;
        }
    }
}
