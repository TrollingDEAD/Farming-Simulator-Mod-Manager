using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using FsModManager.Core.Launching;
using FsModManager.Core.Models;
using FsModManager.Core.Savegames;

namespace FsModManager.Core.Backups;

/// <summary>
/// Full-folder zip snapshots of savegames: create, list, restore, prune.
/// Snapshots live in a "FsModManagerBackups" folder that is a SIBLING of the game's savegame*
/// folders (i.e. directly inside Documents\My Games\FarmingSimulator2025), never inside the game's
/// own savegame folders — the game only recognizes folders named savegame&lt;N&gt;, so a differently
/// named sibling is never picked up as a real save, and the backups stay on the same drive and in
/// the same backup/sync scope as the saves they protect.
/// Both create and restore are blocked while the game is running (savegame files must never be
/// touched mid-session), throwing <see cref="GameAlreadyRunningException"/> like the launcher does.
/// </summary>
public sealed partial class SavegameSnapshotService
{
    /// <summary>Reason tag for user-triggered snapshots. Manual snapshots are exempt from pruning.</summary>
    public const string ManualReason = "manual";

    /// <summary>Reason tag for the automatic snapshot taken before a load-order rewrite.</summary>
    public const string BeforeLoadOrderChangeReason = "before-loadorder-change";

    /// <summary>Reason tag for the automatic safety snapshot taken before a restore overwrites the current state.</summary>
    public const string BeforeRestoreReason = "before-restore";

    /// <summary>Folder created next to the savegame* folders inside the FarmingSimulator2025 directory.</summary>
    public const string BackupsFolderName = "FsModManagerBackups";

    private readonly IGameProcessChecker _gameProcessChecker;
    private readonly string? _backupsRootOverride;

    public SavegameSnapshotService(IGameProcessChecker gameProcessChecker)
    {
        _gameProcessChecker = gameProcessChecker;
    }

    /// <summary>Creates a service storing all snapshots under an explicit root folder (used by tests).</summary>
    public SavegameSnapshotService(IGameProcessChecker gameProcessChecker, string backupsRootOverride)
    {
        _gameProcessChecker = gameProcessChecker;
        _backupsRootOverride = backupsRootOverride;
    }

    /// <summary>
    /// Zips the entire savegame folder into the backups folder and returns the snapshot path.
    /// File name: {savegameFolderName}_{yyyy-MM-dd}_{HHmmss}_{reason}.zip (local time, so a folder
    /// full of snapshots stays human-scannable in Explorer without opening a UI).
    /// </summary>
    public async Task<string> CreateSnapshotAsync(SavegameInfo savegame, string reason)
    {
        ArgumentNullException.ThrowIfNull(savegame);
        EnsureGameNotRunning();

        if (!Directory.Exists(savegame.FolderPath))
        {
            throw new DirectoryNotFoundException($"Savegame folder not found: {savegame.FolderPath}");
        }

        var snapshotFolder = GetSnapshotFolder(savegame);
        Directory.CreateDirectory(snapshotFolder);

        var slotName = Path.GetFileName(Path.TrimEndingDirectorySeparator(savegame.FolderPath));
        var safeReason = SanitizeReason(reason);
        var timestamp = DateTime.Now;

        // Millisecond precision keeps rapid successive snapshots (auto + manual back-to-back)
        // strictly ordered by file name. Bumping forward on the rare residual collision is a safe
        // backstop: the name monotonically increases, so "keep N newest" can never mistake a fresh
        // snapshot for an old one (which a recycled second-precision name would allow).
        string snapshotPath;
        while (true)
        {
            snapshotPath = Path.Combine(snapshotFolder, $"{slotName}_{FormatTimestamp(timestamp)}_{safeReason}.zip");
            if (!File.Exists(snapshotPath))
            {
                break;
            }

            timestamp = timestamp.AddMilliseconds(1);
        }

        var folderPath = savegame.FolderPath;
        await Task.Run(() =>
            ZipFile.CreateFromDirectory(folderPath, snapshotPath, CompressionLevel.Optimal, includeBaseDirectory: false));
        return snapshotPath;
    }

    /// <summary>
    /// Extracts a snapshot back over the target savegame folder. Takes a "before-restore" snapshot
    /// of the CURRENT state first, so restoring is itself never a one-way destructive action.
    /// Files present in the folder but absent from the snapshot are removed — the restore reproduces
    /// the snapshot exactly (safe, because the current state was just snapshotted).
    /// </summary>
    public async Task RestoreSnapshotAsync(string snapshotPath, SavegameInfo targetSavegame)
    {
        ArgumentNullException.ThrowIfNull(targetSavegame);
        EnsureGameNotRunning();

        if (!File.Exists(snapshotPath))
        {
            throw new FileNotFoundException($"Snapshot not found: {snapshotPath}", snapshotPath);
        }

        await CreateSnapshotAsync(targetSavegame, BeforeRestoreReason);

        var folder = targetSavegame.FolderPath;
        await Task.Run(() =>
        {
            ClearDirectory(folder);
            ZipFile.ExtractToDirectory(snapshotPath, folder, overwriteFiles: true);
        });
    }

    /// <summary>
    /// Lists snapshots for the savegame, newest first. Files that don't match the snapshot naming
    /// scheme are ignored — they are not listed here and are never touched by pruning.
    /// </summary>
    public IReadOnlyList<SnapshotInfo> ListSnapshots(SavegameInfo savegame)
    {
        ArgumentNullException.ThrowIfNull(savegame);

        var snapshotFolder = GetSnapshotFolder(savegame);
        if (!Directory.Exists(snapshotFolder))
        {
            return [];
        }

        var results = new List<SnapshotInfo>();
        foreach (var file in Directory.EnumerateFiles(snapshotFolder, "*.zip", SearchOption.TopDirectoryOnly))
        {
            if (TryParseFileName(Path.GetFileName(file), out var createdLocal, out var reason))
            {
                results.Add(new SnapshotInfo(file, createdLocal.ToUniversalTime(), reason, new FileInfo(file).Length));
            }
        }

        return results
            .OrderByDescending(s => s.CreatedUtc)
            .ThenByDescending(s => s.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Deletes all but the <paramref name="keepCount"/> most recent automatic snapshots for the
    /// savegame. Manually-triggered snapshots (reason "manual") are exempt — the user explicitly
    /// asked for those to be kept.
    /// </summary>
    public Task PruneOldSnapshotsAsync(SavegameInfo savegame, int keepCount)
    {
        ArgumentNullException.ThrowIfNull(savegame);
        if (keepCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keepCount));
        }

        var stale = ListSnapshots(savegame)
            .Where(s => !string.Equals(s.Reason, ManualReason, StringComparison.OrdinalIgnoreCase))
            .Skip(keepCount) // ListSnapshots is already newest-first
            .ToList();

        return Task.Run(() =>
        {
            foreach (var snapshot in stale)
            {
                try
                {
                    File.Delete(snapshot.FilePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Pruning is housekeeping: a locked/undeletable file must never fail a save.
                }
            }
        });
    }

    /// <summary>Total bytes consumed by all snapshots of the savegame (0 when there are none).</summary>
    public long GetTotalSizeBytes(SavegameInfo savegame) => ListSnapshots(savegame).Sum(s => s.SizeBytes);

    /// <summary>
    /// The folder where this savegame's snapshots live (may not exist yet — nothing is created
    /// until the first snapshot). Exposed so the UI can point the user at their backups directly.
    /// </summary>
    public string GetSnapshotFolderPath(SavegameInfo savegame)
    {
        ArgumentNullException.ThrowIfNull(savegame);
        return GetSnapshotFolder(savegame);
    }

    private void EnsureGameNotRunning()
    {
        if (_gameProcessChecker.IsGameRunning())
        {
            throw new GameAlreadyRunningException();
        }
    }

    private string GetSnapshotFolder(SavegameInfo savegame)
    {
        var root = _backupsRootOverride ?? ResolveDefaultBackupsRoot(savegame);
        return Path.Combine(root, Path.GetFileName(Path.TrimEndingDirectorySeparator(savegame.FolderPath)));
    }

    private static string ResolveDefaultBackupsRoot(SavegameInfo savegame)
    {
        var gameFolder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(savegame.FolderPath));
        if (string.IsNullOrEmpty(gameFolder))
        {
            throw new InvalidOperationException(
                $"Cannot determine a backups location from savegame folder '{savegame.FolderPath}'.");
        }

        return Path.Combine(gameFolder, BackupsFolderName);
    }

    private static void ClearDirectory(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    private static string SanitizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "snapshot";
        }

        var chars = reason.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray();
        return new string(chars);
    }

    private static string FormatTimestamp(DateTime timestamp) =>
        timestamp.ToString("yyyy-MM-dd_HHmmssfff", CultureInfo.InvariantCulture);

    private static readonly string[] TimestampFormats = ["yyyy-MM-dd_HHmmssfff", "yyyy-MM-dd_HHmmss"];

    private static bool TryParseFileName(string fileName, out DateTime createdLocal, out string reason)
    {
        createdLocal = default;
        reason = string.Empty;

        var match = SnapshotFileNameRegex().Match(fileName);
        if (!match.Success)
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                $"{match.Groups["date"].Value}_{match.Groups["time"].Value}",
                TimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out createdLocal))
        {
            return false;
        }

        reason = match.Groups["reason"].Value;
        return true;
    }

    // Slot name (e.g. "savegame3") contains no date-like token, so the greedy .+ prefix backtracks
    // cleanly onto the first real timestamp in the name. The time segment accepts both the current
    // millisecond-precision form and the original second-precision one.
    [GeneratedRegex(@"^.+_(?<date>\d{4}-\d{2}-\d{2})_(?<time>\d{9}|\d{6})_(?<reason>.+)\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex SnapshotFileNameRegex();
}
