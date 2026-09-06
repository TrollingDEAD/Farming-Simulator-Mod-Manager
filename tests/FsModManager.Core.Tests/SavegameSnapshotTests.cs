using System.IO.Compression;
using FsModManager.Core.Backups;
using FsModManager.Core.Launching;
using FsModManager.Core.Models;
using FsModManager.Core.Savegames;
using Xunit;

namespace FsModManager.Core.Tests;

public sealed class SavegameSnapshotTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public SavegameSnapshotTests()
    {
        Directory.CreateDirectory(SavegameFolder);
        File.WriteAllText(Path.Combine(SavegameFolder, "careerSavegame.xml"), "<careerSavegame />");
        File.WriteAllText(Path.Combine(SavegameFolder, "mods.xml"), "<modsList />");
        Directory.CreateDirectory(Path.Combine(SavegameFolder, "sub"));
        File.WriteAllText(Path.Combine(SavegameFolder, "sub", "farms.xml"), "<farms />");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, recursive: true);
        }
    }

    private string SavegameFolder =>
        Path.Combine(_tempFolder, "Documents", "My Games", "FarmingSimulator2025", "savegame3");

    private string BackupsRoot => Path.Combine(_tempFolder, "backups");

    private SavegameInfo Savegame => new(SavegameFolder, "My Farm", 3);

    [Fact]
    public async Task CreateSnapshot_ZipContainsAllSavegameFiles()
    {
        var service = new SavegameSnapshotService(new FakeGameProcessChecker(false), BackupsRoot);

        var path = await service.CreateSnapshotAsync(Savegame, SavegameSnapshotService.ManualReason);

        Assert.True(File.Exists(path));
        // Stored under {root}\savegame3\, named {slot}_{date}_{time}_{reason}.zip — human-scannable.
        Assert.Equal(Path.Combine(BackupsRoot, "savegame3"), Path.GetDirectoryName(path));
        var fileName = Path.GetFileName(path);
        Assert.StartsWith("savegame3_", fileName);
        Assert.EndsWith("_manual.zip", fileName);
        Assert.Matches(@"^savegame3_\d{4}-\d{2}-\d{2}_\d{9}_manual\.zip$", fileName);

        using var archive = ZipFile.OpenRead(path);
        var entryNames = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        Assert.Contains("careerSavegame.xml", entryNames);
        Assert.Contains("mods.xml", entryNames);
        Assert.Contains("sub/farms.xml", entryNames);
    }

    [Fact]
    public async Task ListSnapshots_ReturnsNewestFirstWithReasonAndSize()
    {
        var service = new SavegameSnapshotService(new FakeGameProcessChecker(false), BackupsRoot);
        var first = await service.CreateSnapshotAsync(Savegame, SavegameSnapshotService.BeforeLoadOrderChangeReason);
        var second = await service.CreateSnapshotAsync(Savegame, SavegameSnapshotService.ManualReason);

        var snapshots = service.ListSnapshots(Savegame);

        Assert.Equal(2, snapshots.Count);
        Assert.Equal(second, snapshots[0].FilePath);
        Assert.Equal(first, snapshots[1].FilePath);
        Assert.Equal(SavegameSnapshotService.ManualReason, snapshots[0].Reason);
        Assert.Equal(SavegameSnapshotService.BeforeLoadOrderChangeReason, snapshots[1].Reason);
        Assert.Equal(new FileInfo(second).Length, snapshots[0].SizeBytes);
        Assert.True(snapshots[0].CreatedUtc >= snapshots[1].CreatedUtc);
    }

    [Fact]
    public async Task RestoreSnapshot_ReplacesTargetContentsExactly()
    {
        var service = new SavegameSnapshotService(new FakeGameProcessChecker(false), BackupsRoot);
        var snapshotPath = await service.CreateSnapshotAsync(Savegame, SavegameSnapshotService.ManualReason);

        // Drift the live folder: change a file, delete one, add a stray one.
        File.WriteAllText(Path.Combine(SavegameFolder, "mods.xml"), "<modsList>CORRUPTED</modsList>");
        File.Delete(Path.Combine(SavegameFolder, "careerSavegame.xml"));
        File.WriteAllText(Path.Combine(SavegameFolder, "stray.txt"), "stray");

        await service.RestoreSnapshotAsync(snapshotPath, Savegame);

        Assert.Equal("<modsList />", File.ReadAllText(Path.Combine(SavegameFolder, "mods.xml")));
        Assert.True(File.Exists(Path.Combine(SavegameFolder, "careerSavegame.xml")));
        Assert.Equal("<farms />", File.ReadAllText(Path.Combine(SavegameFolder, "sub", "farms.xml")));
        Assert.False(File.Exists(Path.Combine(SavegameFolder, "stray.txt")));
    }

    [Fact]
    public async Task RestoreSnapshot_TakesBeforeRestoreSnapshotOfCurrentStateFirst()
    {
        var service = new SavegameSnapshotService(new FakeGameProcessChecker(false), BackupsRoot);
        var snapshotPath = await service.CreateSnapshotAsync(Savegame, SavegameSnapshotService.ManualReason);

        File.WriteAllText(Path.Combine(SavegameFolder, "mods.xml"), "<modsList>CURRENT-STATE</modsList>");

        await service.RestoreSnapshotAsync(snapshotPath, Savegame);

        var beforeRestore = service.ListSnapshots(Savegame)
            .Single(s => s.Reason == SavegameSnapshotService.BeforeRestoreReason);

        // The safety snapshot must contain the pre-restore state, not the restored one.
        using var archive = ZipFile.OpenRead(beforeRestore.FilePath);
        var entry = archive.GetEntry("mods.xml");
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        Assert.Equal("<modsList>CURRENT-STATE</modsList>", reader.ReadToEnd());
    }

    [Fact]
    public async Task CreateSnapshot_GameRunning_Throws()
    {
        var service = new SavegameSnapshotService(new FakeGameProcessChecker(true), BackupsRoot);

        await Assert.ThrowsAsync<GameAlreadyRunningException>(
            () => service.CreateSnapshotAsync(Savegame, SavegameSnapshotService.ManualReason));
    }

    [Fact]
    public async Task RestoreSnapshot_GameRunning_Throws_AndLeavesFolderUntouched()
    {
        var service = new SavegameSnapshotService(new FakeGameProcessChecker(false), BackupsRoot);
        var snapshotPath = await service.CreateSnapshotAsync(Savegame, SavegameSnapshotService.ManualReason);

        var guarded = new SavegameSnapshotService(new FakeGameProcessChecker(true), BackupsRoot);
        await Assert.ThrowsAsync<GameAlreadyRunningException>(
            () => guarded.RestoreSnapshotAsync(snapshotPath, Savegame));

        Assert.Equal("<modsList />", File.ReadAllText(Path.Combine(SavegameFolder, "mods.xml")));
    }

    [Fact]
    public async Task PruneOldSnapshots_KeepsNewestAutomatic_PreservesManualAndForeignFiles()
    {
        var folder = Path.Combine(BackupsRoot, "savegame3");
        Directory.CreateDirectory(folder);

        // Handcrafted names → deterministic timestamps. ListSnapshots/Prune never open the zips.
        var auto1 = TouchZip(folder, "savegame3_2026-09-01_100000_before-loadorder-change.zip");
        var auto2 = TouchZip(folder, "savegame3_2026-09-02_100000_before-loadorder-change.zip");
        var auto3 = TouchZip(folder, "savegame3_2026-09-03_100000_before-restore.zip");
        var auto4 = TouchZip(folder, "savegame3_2026-09-05_100000_before-loadorder-change.zip");
        var manual1 = TouchZip(folder, "savegame3_2026-09-04_100000_manual.zip");
        var manual2 = TouchZip(folder, "savegame3_2026-09-06_100000_manual.zip");
        var foreign = TouchZip(folder, "not-a-snapshot.zip");

        var service = new SavegameSnapshotService(new FakeGameProcessChecker(false), BackupsRoot);
        await service.PruneOldSnapshotsAsync(Savegame, keepCount: 2);

        // Two newest automatic survive; older automatic are gone.
        Assert.False(File.Exists(auto1));
        Assert.False(File.Exists(auto2));
        Assert.True(File.Exists(auto3));
        Assert.True(File.Exists(auto4));
        // Manual snapshots are exempt regardless of age...
        Assert.True(File.Exists(manual1));
        Assert.True(File.Exists(manual2));
        // ...and files outside the naming scheme are never touched.
        Assert.True(File.Exists(foreign));
    }

    [Fact]
    public async Task WriteLoadOrderAsync_TakesSnapshotOfPreChangeStateBeforeWriting()
    {
        File.WriteAllText(Path.Combine(SavegameFolder, "mods.xml"), """
            <?xml version="1.0" encoding="utf-8"?>
            <modsList>
                <mod modName="FS25_Old" active="true">FS25_Old</mod>
            </modsList>
            """);

        var snapshots = new SavegameSnapshotService(new FakeGameProcessChecker(false), BackupsRoot);
        var writer = new ModLoadOrderWriter(new FakeGameProcessChecker(false), snapshots);

        await writer.WriteLoadOrderAsync(Savegame, [new ModLoadEntry("FS25_New", true, 0)]);

        var snapshot = Assert.Single(snapshots.ListSnapshots(Savegame));
        Assert.Equal(SavegameSnapshotService.BeforeLoadOrderChangeReason, snapshot.Reason);

        // The snapshot captured mods.xml as it was BEFORE the write...
        using (var archive = ZipFile.OpenRead(snapshot.FilePath))
        {
            var entry = archive.GetEntry("mods.xml");
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry.Open());
            Assert.Contains("FS25_Old", reader.ReadToEnd());
        }

        // ...while the live folder has the new order, with mods.xml.bak still doing quick-undo duty.
        Assert.Contains("FS25_New", File.ReadAllText(Path.Combine(SavegameFolder, "mods.xml")));
        Assert.Contains("FS25_Old", File.ReadAllText(Path.Combine(SavegameFolder, "mods.xml.bak")));
    }

    [Fact]
    public async Task WriteLoadOrderAsync_PrunesAutomaticSnapshotsToConfiguredCount()
    {
        var snapshots = new SavegameSnapshotService(new FakeGameProcessChecker(false), BackupsRoot);
        var writer = new ModLoadOrderWriter(new FakeGameProcessChecker(false), snapshots, automaticSnapshotKeepCount: 3);

        for (var i = 0; i < 6; i++)
        {
            await writer.WriteLoadOrderAsync(Savegame, [new ModLoadEntry($"FS25_Mod{i}", true, 0)]);
        }

        var remaining = snapshots.ListSnapshots(Savegame);
        Assert.Equal(3, remaining.Count);
        // Newest-first: the surviving zips contain the states before the last three writes.
        using var newest = ZipFile.OpenRead(remaining[0].FilePath);
        var entry = newest.GetEntry("mods.xml");
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        Assert.Contains("FS25_Mod4", reader.ReadToEnd());
    }

    private static string TouchZip(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        File.WriteAllText(path, "fake-zip");
        return path;
    }

    private sealed class FakeGameProcessChecker(bool isRunning) : IGameProcessChecker
    {
        public bool IsGameRunning() => isRunning;
    }
}
