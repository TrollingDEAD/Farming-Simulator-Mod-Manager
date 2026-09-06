namespace FsModManager.Core.Diagnostics;

/// <summary>Status of the user's Documents folder synchronization with OneDrive.</summary>
public enum OneDriveSyncStatus
{
    /// <summary>The Documents folder is local and not under any detected OneDrive path.</summary>
    NotOneDrive,

    /// <summary>
    /// The Documents folder is redirected to OneDrive, and the expected FarmingSimulator2025
    /// folder exists and is accessible.
    /// </summary>
    OneDriveButSynced,

    /// <summary>
    /// The Documents folder is redirected to OneDrive, but the expected FarmingSimulator2025
    /// folder does not exist or is not accessible.
    /// </summary>
    OneDriveAndMissing,
}

/// <summary>Result of checking Documents folder OneDrive synchronization.</summary>
public sealed record OneDriveCheckResult(
    OneDriveSyncStatus Status,
    string DocumentsPath,
    string ExpectedGameFolderPath,
    string? MatchedOneDriveRoot = null,
    string? Message = null);

/// <summary>
/// Diagnostics for environment and filesystem setup, including OneDrive cloud-sync detection.
/// </summary>
public static class EnvironmentDiagnostics
{
    private static readonly string[] OneDriveEnvironmentVariables =
    [
        "OneDrive",
        "OneDriveConsumer",
        "OneDriveCommercial",
    ];

    /// <summary>
    /// Checks whether the user's Documents folder is redirected into OneDrive.
    /// </summary>
    /// <param name="documentsPath">Optional Documents folder path (defaults to Environment.SpecialFolder.MyDocuments).</param>
    /// <param name="expectedGameFolderPath">Optional expected game folder path (defaults to Documents\My Games\FarmingSimulator2025).</param>
    /// <param name="environmentVariables">Optional environment variable lookup for testing.</param>
    /// <param name="directoryExists">Optional directory existence check function for testing.</param>
    public static OneDriveCheckResult CheckDocumentsFolderSync(
        string? documentsPath = null,
        string? expectedGameFolderPath = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        Func<string, bool>? directoryExists = null)
    {
        var resolvedDocs = documentsPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var resolvedGameFolder = expectedGameFolderPath ?? Path.Combine(resolvedDocs, "My Games", "FarmingSimulator2025");
        var checkDirectory = directoryExists ?? Directory.Exists;

        string? matchedRoot = null;

        foreach (var varName in OneDriveEnvironmentVariables)
        {
            var envVal = environmentVariables is not null
                ? (environmentVariables.TryGetValue(varName, out var v) ? v : null)
                : Environment.GetEnvironmentVariable(varName);

            if (!string.IsNullOrWhiteSpace(envVal) && IsSubpathOrEqual(resolvedDocs, envVal))
            {
                matchedRoot = envVal;
                break;
            }
        }

        if (matchedRoot is null)
        {
            return new OneDriveCheckResult(
                OneDriveSyncStatus.NotOneDrive,
                resolvedDocs,
                resolvedGameFolder,
                null,
                "Documents folder is stored locally (not synced with OneDrive).");
        }

        bool gameFolderExists;
        try
        {
            gameFolderExists = checkDirectory(resolvedGameFolder);
        }
        catch
        {
            gameFolderExists = false;
        }

        if (!gameFolderExists)
        {
            return new OneDriveCheckResult(
                OneDriveSyncStatus.OneDriveAndMissing,
                resolvedDocs,
                resolvedGameFolder,
                matchedRoot,
                $"Your Documents folder is synced to OneDrive ({matchedRoot}), but the 'FarmingSimulator2025' folder was not found or is inaccessible. OneDrive Files-On-Demand or pending sync may be preventing the game from accessing its files.");
        }

        return new OneDriveCheckResult(
            OneDriveSyncStatus.OneDriveButSynced,
            resolvedDocs,
            resolvedGameFolder,
            matchedRoot,
            $"Your Documents folder is located inside OneDrive ({matchedRoot}). If you experience savegame sync conflicts or loading issues, consider configuring OneDrive to 'Always keep on this device' for your Documents folder.");
    }

    /// <summary>Checks whether <paramref name="childPath"/> is equal to or a subfolder of <paramref name="parentPath"/>.</summary>
    private static bool IsSubpathOrEqual(string childPath, string parentPath)
    {
        if (string.IsNullOrWhiteSpace(childPath) || string.IsNullOrWhiteSpace(parentPath))
        {
            return false;
        }

        try
        {
            var normalizedChild = Path.GetFullPath(childPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedParent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (normalizedChild.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var parentWithSep = normalizedParent + Path.DirectorySeparatorChar;
            return normalizedChild.StartsWith(parentWithSep, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
