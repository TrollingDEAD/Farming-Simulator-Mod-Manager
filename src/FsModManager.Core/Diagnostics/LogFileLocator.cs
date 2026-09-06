namespace FsModManager.Core.Diagnostics;

/// <summary>
/// Resolves the path to FS25's log.txt. Confirmed against a real log.txt on a dev machine:
/// the file is truncated and rewritten from scratch on every game launch (a single
/// "GIANTS Engine Runtime" header per file, file's CreationTime staying stable across many
/// LastWriteTime updates - consistent with the engine opening the same file handle and
/// truncating rather than deleting/recreating it) - so reading the whole file is always just
/// the most recent session, no rotation/append handling is needed.
/// </summary>
public static class LogFileLocator
{
    /// <summary>Always returns the expected path, even if the file doesn't exist yet (game never run).</summary>
    public static string GetLogFilePath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documents, "My Games", "FarmingSimulator2025", "log.txt");
    }

    /// <summary>True and outputs the path only when log.txt actually exists on disk.</summary>
    public static bool TryGetExistingLogFilePath(out string path)
    {
        path = GetLogFilePath();
        return File.Exists(path);
    }
}
