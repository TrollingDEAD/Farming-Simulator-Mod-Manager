using System.Text.Json;
using FsModManager.Core.Launching;
using FsModManager.Core.Models;
using FsModManager.Core.Savegames;

namespace FsModManager.Core.Diagnostics;

/// <summary>Resolves FS25's shader cache folder — confirmed to live directly alongside log.txt on a real install.</summary>
public static class ShaderCacheLocator
{
    public static string GetShaderCacheFolderPath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documents, "My Games", "FarmingSimulator2025", "shader_cache");
    }
}

/// <summary>
/// What to do when entering clean test mode: remove every mod, or keep a specific subset active
/// (used by the bisection flow once mods are already confirmed as the cause). Clearing the shader
/// cache is an independent, unrelated toggle — the folder is deleted outright since it is safe to
/// regenerate, unlike mod files which are never deleted by this feature.
/// </summary>
public sealed record CleanTestOptions(bool RemoveAllMods, IReadOnlyList<string>? ModsToKeep, bool ClearShaderCache);

/// <summary>
/// Records exactly what a clean test session moved and where, so it can always be undone —
/// including after an app crash. Persisted verbatim as the state file's JSON contents.
/// </summary>
/// <param name="WizardStateJson">
/// Opaque, App-owned blob (e.g. which wizard step/bisection round is in progress) so the guided
/// wizard can resume exactly where the user left off after a crash/close — Core never interprets
/// this, it just persists it alongside the file-move record it already owns.
/// </param>
public sealed record CleanTestSession(
    string SessionId,
    string ModsFolderPath,
    string HoldingFolderPath,
    IReadOnlyList<string> MovedFileNames,
    IReadOnlyList<string> KeptFileNames,
    DateTime StartedUtc,
    bool ShaderCacheCleared,
    bool IsActive,
    string? WizardStateJson = null);

/// <summary>
/// Thrown by <see cref="CleanTestModeService.ExitCleanTestModeAsync"/> when the restored mods
/// folder doesn't match what was originally recorded. The persisted session state file is
/// deliberately NOT cleared when this is thrown, so the inconsistency is never silently lost —
/// the leftover-session startup check will catch it again next time the app opens.
/// </summary>
public sealed class CleanTestRestoreVerificationException : Exception
{
    public CleanTestRestoreVerificationException(
        CleanTestSession session, IReadOnlyList<string> missingFiles, IReadOnlyList<string> leftoverInHolding)
        : base(BuildMessage(missingFiles, leftoverInHolding))
    {
        Session = session;
        MissingFiles = missingFiles;
        LeftoverInHolding = leftoverInHolding;
    }

    public CleanTestSession Session { get; }

    /// <summary>Files that were recorded as moved but could not be found/restored.</summary>
    public IReadOnlyList<string> MissingFiles { get; }

    /// <summary>Files still left in the holding folder after the restore attempt.</summary>
    public IReadOnlyList<string> LeftoverInHolding { get; }

    private static string BuildMessage(IReadOnlyList<string> missing, IReadOnlyList<string> leftover)
    {
        var parts = new List<string>();
        if (missing.Count > 0)
        {
            parts.Add($"{missing.Count} file(s) could not be restored: {string.Join(", ", missing)}");
        }

        if (leftover.Count > 0)
        {
            parts.Add($"{leftover.Count} file(s) remain in the holding folder: {string.Join(", ", leftover)}");
        }

        return "Clean test mode restore verification failed — " + string.Join("; ", parts) +
               ". Your mods folder is now in an inconsistent state and needs attention.";
    }
}

/// <summary>
/// Temporarily moves mod files out of the FS25 mods folder (never deletes them) so the user can
/// test whether a problem is caused by mods at all, then restores everything afterward. Guarded by
/// <see cref="IGameProcessChecker"/> on both directions, same as every other file-touching operation
/// in this project. State is persisted to disk so an interrupted session (app crash/close while
/// active) can always be detected and resumed on next startup rather than leaving the user's mods
/// folder silently emptied with no obvious way back.
/// </summary>
public sealed class CleanTestModeService
{
    /// <summary>Folder created as a sibling of the real mods folder — same drive, so restoring never needs a cross-drive copy.</summary>
    public const string HoldingFolderName = "clean_test_backup";

    private const string StateFileName = "clean-test-session.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IGameProcessChecker _gameProcessChecker;
    private readonly string _stateFilePath;

    public CleanTestModeService(IGameProcessChecker gameProcessChecker)
    {
        _gameProcessChecker = gameProcessChecker;
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FsModManager");
        Directory.CreateDirectory(appDataFolder);
        _stateFilePath = Path.Combine(appDataFolder, StateFileName);
    }

    /// <summary>Creates a service persisting session state to an explicit file (used by tests).</summary>
    public CleanTestModeService(IGameProcessChecker gameProcessChecker, string stateFilePathOverride)
    {
        _gameProcessChecker = gameProcessChecker;
        _stateFilePath = stateFilePathOverride;
        var folder = Path.GetDirectoryName(stateFilePathOverride);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }
    }

    /// <summary>
    /// Moves every mod file out of <paramref name="modsFolderPath"/> (except any names listed in
    /// <see cref="CleanTestOptions.ModsToKeep"/>, when <see cref="CleanTestOptions.RemoveAllMods"/>
    /// is false) into a sibling holding folder, then persists the resulting session so it survives
    /// a crash. Files are moved individually (never a single whole-tree move), because a "keep a
    /// subset" test requires per-file selection and because per-file moves let a failure on one
    /// file surface without silently skipping the rest.
    /// </summary>
    public async Task<CleanTestSession> EnterCleanTestModeAsync(string modsFolderPath, CleanTestOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(options);
        EnsureGameNotRunning();

        if (TryLoadPersistedSession() is { IsActive: true })
        {
            throw new InvalidOperationException(
                "A clean test session is already active. Restore it before starting a new one.");
        }

        if (!Directory.Exists(modsFolderPath))
        {
            throw new DirectoryNotFoundException($"Mods folder not found: {modsFolderPath}");
        }

        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(modsFolderPath));
        if (string.IsNullOrEmpty(parent))
        {
            throw new InvalidOperationException($"Cannot determine a holding location from mods folder '{modsFolderPath}'.");
        }

        var holdingFolder = Path.Combine(parent, HoldingFolderName);
        Directory.CreateDirectory(holdingFolder);

        // A non-empty holding folder means a previous session's restore never completed — never
        // move new files on top of it, that would corrupt the record of what belongs to which session.
        if (Directory.EnumerateFileSystemEntries(holdingFolder).Any())
        {
            throw new InvalidOperationException(
                $"Holding folder '{holdingFolder}' is not empty. A previous clean test session may not have " +
                "been fully restored — resolve this before starting a new one.");
        }

        var keepSet = new HashSet<string>(options.ModsToKeep ?? [], StringComparer.OrdinalIgnoreCase);

        var moved = new List<string>();
        var kept = new List<string>();
        await Task.Run(() =>
        {
            foreach (var file in Directory.EnumerateFiles(modsFolderPath, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (!options.RemoveAllMods && keepSet.Contains(name))
                {
                    kept.Add(name);
                    continue;
                }

                // File.Move on .NET falls back to copy+delete automatically when the destination is
                // on a different volume, so this is safe even if the holding folder were ever moved
                // to a different drive — same-drive siblings just get a fast rename either way.
                File.Move(file, Path.Combine(holdingFolder, name));
                moved.Add(name);
            }
        });

        var shaderCacheCleared = options.ClearShaderCache && TryDeleteShaderCache();

        var session = new CleanTestSession(
            Guid.NewGuid().ToString("N"),
            modsFolderPath,
            holdingFolder,
            moved,
            kept,
            DateTime.UtcNow,
            shaderCacheCleared,
            IsActive: true);

        await SaveSessionAsync(session);
        return session;
    }

    /// <summary>
    /// Moves everything back from the holding folder into the real mods folder, then verifies the
    /// restore by count/name before considering it complete. If anything doesn't match, this throws
    /// <see cref="CleanTestRestoreVerificationException"/> rather than silently proceeding, and
    /// leaves the persisted session state file in place so the mismatch is never lost.
    /// </summary>
    public async Task ExitCleanTestModeAsync(CleanTestSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureGameNotRunning();

        Directory.CreateDirectory(session.ModsFolderPath);

        var restored = new List<string>();
        var missing = new List<string>();
        await Task.Run(() =>
        {
            foreach (var name in session.MovedFileNames)
            {
                var source = Path.Combine(session.HoldingFolderPath, name);
                var destination = Path.Combine(session.ModsFolderPath, name);

                if (!File.Exists(source))
                {
                    missing.Add(name);
                    continue;
                }

                if (File.Exists(destination))
                {
                    // Something else already recreated this file while the test was active — moving
                    // over it could destroy data, so this must be flagged, never overwritten silently.
                    missing.Add(name);
                    continue;
                }

                File.Move(source, destination);
                restored.Add(name);
            }
        });

        var leftoverInHolding = Directory.Exists(session.HoldingFolderPath)
            ? Directory.EnumerateFileSystemEntries(session.HoldingFolderPath).Select(Path.GetFileName).Cast<string>().ToList()
            : [];

        if (missing.Count > 0 || leftoverInHolding.Count > 0)
        {
            throw new CleanTestRestoreVerificationException(session, missing, leftoverInHolding);
        }

        if (Directory.Exists(session.HoldingFolderPath))
        {
            Directory.Delete(session.HoldingFolderPath);
        }

        ClearPersistedSession();
    }

    /// <summary>
    /// Re-persists the session with an updated wizard-state blob, without moving any files. Used by
    /// the App layer so a crash mid-wizard resumes at the right step instead of just "session active".
    /// </summary>
    public async Task<CleanTestSession> SaveWizardStateAsync(CleanTestSession session, string? wizardStateJson)
    {
        ArgumentNullException.ThrowIfNull(session);
        var updated = session with { WizardStateJson = wizardStateJson };
        await SaveSessionAsync(updated);
        return updated;
    }

    /// <summary>Returns the leftover session from a prior ungraceful exit, or null if none is persisted.</summary>
    public CleanTestSession? TryLoadPersistedSession()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CleanTestSession>(File.ReadAllText(_stateFilePath), JsonOptions);
        }
        catch (Exception)
        {
            // A malformed state file must never crash startup — treat it as "nothing to restore".
            // (This does mean a corrupted file could hide a real leftover session; the holding
            // folder itself would still exist on disk for the user to find manually.)
            return null;
        }
    }

    /// <summary>
    /// Suggests roughly half of the not-yet-tested mods to add back in for the next bisection round,
    /// given the full mod list and the set already confirmed NOT to cause the problem. Classic binary
    /// search: turns "find the one bad mod among N" into a ~log2(N) round process.
    /// </summary>
    public static IReadOnlyList<ModFileInfo> SuggestNextBisectionSet(
        IReadOnlyList<ModFileInfo> allMods, IReadOnlyList<ModFileInfo> previouslyTestedGood)
    {
        ArgumentNullException.ThrowIfNull(allMods);
        ArgumentNullException.ThrowIfNull(previouslyTestedGood);

        var testedPaths = new HashSet<string>(previouslyTestedGood.Select(m => m.ZipPath), StringComparer.OrdinalIgnoreCase);
        var untested = allMods.Where(m => !testedPaths.Contains(m.ZipPath)).ToList();
        if (untested.Count == 0)
        {
            return [];
        }

        // Round up so an odd remainder still makes forward progress every round (e.g. 1 remaining -> takes 1).
        var takeCount = (untested.Count + 1) / 2;
        return untested.Take(takeCount).ToList();
    }

    private async Task SaveSessionAsync(CleanTestSession session)
    {
        await File.WriteAllTextAsync(_stateFilePath, JsonSerializer.Serialize(session, JsonOptions));
    }

    private void ClearPersistedSession()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                File.Delete(_stateFilePath);
            }
        }
        catch (Exception)
        {
            // Best-effort: a locked/undeletable state file will simply be seen again as "leftover"
            // next startup, which is the safe direction to fail in.
        }
    }

    private static bool TryDeleteShaderCache()
    {
        var path = ShaderCacheLocator.GetShaderCacheFolderPath();
        if (!Directory.Exists(path))
        {
            return false;
        }

        try
        {
            // Unlike mod files, the shader cache is regenerated automatically by the game — deleting
            // it outright is the actual forum-recommended fix, moving it would serve no safety purpose.
            Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void EnsureGameNotRunning()
    {
        if (_gameProcessChecker.IsGameRunning())
        {
            throw new GameAlreadyRunningException();
        }
    }
}
