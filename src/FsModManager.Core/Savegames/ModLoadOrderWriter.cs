using System.Xml.Linq;
using FsModManager.Core.Backups;
using FsModManager.Core.Models;

namespace FsModManager.Core.Savegames;

// NOTE: Whether mods.xml order actually affects runtime mod override/conflict resolution in FS25,
// versus merely the order mods are displayed in the in-game mod list UI, is unconfirmed. Verify this
// empirically before promising users that reordering here changes gameplay conflict resolution.
/// <summary>
/// Rewrites a savegame's mods.xml with a given load order, backing up the original first.
/// When constructed with a <see cref="SavegameSnapshotService"/>, the async entry point also takes
/// a full-folder savegame snapshot before every write — the mods.xml.bak copy remains as a quick
/// undo for the very last write, while snapshots are the longer-running safety net across changes.
/// </summary>
public sealed class ModLoadOrderWriter
{
    private const int DefaultAutomaticSnapshotKeepCount = 10;

    private readonly IGameProcessChecker _gameProcessChecker;
    private readonly SavegameSnapshotService? _snapshotService;

    public ModLoadOrderWriter(
        IGameProcessChecker gameProcessChecker,
        SavegameSnapshotService? snapshotService = null,
        int automaticSnapshotKeepCount = DefaultAutomaticSnapshotKeepCount)
    {
        _gameProcessChecker = gameProcessChecker;
        _snapshotService = snapshotService;
        AutomaticSnapshotKeepCount = automaticSnapshotKeepCount;
    }

    /// <summary>
    /// How many automatic snapshots <see cref="WriteLoadOrderAsync"/> keeps per savegame after
    /// pruning. Settable so a changed user preference applies to the singleton without rebuilding
    /// the DI graph; only read at save time, so mid-session changes take effect on the next save.
    /// </summary>
    public int AutomaticSnapshotKeepCount { get; set; }

    /// <summary>
    /// Takes a full savegame snapshot (reason "before-loadorder-change") and prunes old automatic
    /// snapshots, then writes the given load order. The snapshot deliberately precedes the write:
    /// if the backup fails, the savegame is left untouched rather than modified without protection.
    /// </summary>
    public async Task WriteLoadOrderAsync(SavegameInfo savegame, IReadOnlyList<ModLoadEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(savegame);

        if (_snapshotService is not null)
        {
            await _snapshotService.CreateSnapshotAsync(savegame, SavegameSnapshotService.BeforeLoadOrderChangeReason);
            await _snapshotService.PruneOldSnapshotsAsync(savegame, AutomaticSnapshotKeepCount);
        }

        WriteLoadOrder(savegame.FolderPath, entries);
    }

    /// <summary>
    /// Backs up the existing mods.xml to mods.xml.bak (overwriting any previous backup), then writes
    /// the given entries. Preserves other structure/attributes of the original file when present.
    /// Throws <see cref="InvalidOperationException"/> if the game is currently running.
    /// </summary>
    public void WriteLoadOrder(string savegameFolderPath, IReadOnlyList<ModLoadEntry> entries)
    {
        if (_gameProcessChecker.IsGameRunning())
        {
            throw new InvalidOperationException(
                "Cannot write mods.xml while Farming Simulator 25 is running. Close the game and try again.");
        }

        var modsFile = Path.Combine(savegameFolderPath, "mods.xml");
        var backupFile = Path.Combine(savegameFolderPath, "mods.xml.bak");

        XDocument doc;
        XElement root;
        if (File.Exists(modsFile))
        {
            File.Copy(modsFile, backupFile, overwrite: true);

            doc = XDocument.Load(modsFile);
            root = doc.Root ?? new XElement("modsList");
            if (doc.Root is null)
            {
                doc = new XDocument(root);
            }

            root.Elements("mod").Remove();
        }
        else
        {
            root = new XElement("modsList");
            doc = new XDocument(new XDeclaration("1.0", "utf-8", "no"), root);
        }

        foreach (var entry in entries.OrderBy(e => e.Order))
        {
            root.Add(new XElement("mod",
                new XAttribute("modName", entry.InternalModName),
                new XAttribute("active", entry.Active ? "true" : "false"),
                entry.InternalModName));
        }

        Directory.CreateDirectory(savegameFolderPath);
        doc.Save(modsFile);
    }
}
