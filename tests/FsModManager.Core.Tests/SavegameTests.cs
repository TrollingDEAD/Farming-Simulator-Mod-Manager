using FsModManager.Core.Models;
using FsModManager.Core.Savegames;
using Xunit;

namespace FsModManager.Core.Tests;

public sealed class SavegameTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public SavegameTests()
    {
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, recursive: true);
        }
    }

    private string GameFolder => Path.Combine(_tempFolder, "Documents", "My Games", "FarmingSimulator2025");

    // ---- SavegameDiscovery ----

    [Fact]
    public void FindSavegames_ReadsDisplayNameFromCareerSavegame()
    {
        var savegame1 = Path.Combine(GameFolder, "savegame1");
        Directory.CreateDirectory(savegame1);
        File.WriteAllText(Path.Combine(savegame1, "careerSavegame.xml"), """
            <?xml version="1.0" encoding="utf-8"?>
            <careerSavegame>
                <settings>
                    <savegameName>My Awesome Farm</savegameName>
                </settings>
            </careerSavegame>
            """);

        var discovery = new SavegameDiscovery(_tempFolder);
        var savegames = discovery.FindSavegames();

        var savegame = Assert.Single(savegames);
        Assert.Equal(1, savegame.SlotIndex);
        Assert.Equal("My Awesome Farm", savegame.DisplayName);
        Assert.Equal(savegame1, savegame.FolderPath);
    }

    [Fact]
    public void FindSavegames_MissingCareerSavegame_FallsBackToGenericName()
    {
        Directory.CreateDirectory(Path.Combine(GameFolder, "savegame3"));

        var discovery = new SavegameDiscovery(_tempFolder);
        var savegames = discovery.FindSavegames();

        var savegame = Assert.Single(savegames);
        Assert.Equal(3, savegame.SlotIndex);
        Assert.Equal("Savegame 3", savegame.DisplayName);
    }

    [Fact]
    public void FindSavegames_NoGameFolder_ReturnsEmpty()
    {
        var discovery = new SavegameDiscovery(_tempFolder);

        Assert.Empty(discovery.FindSavegames());
    }

    [Fact]
    public void FindSavegames_MultipleSlots_OrderedBySlotIndex()
    {
        Directory.CreateDirectory(Path.Combine(GameFolder, "savegame2"));
        Directory.CreateDirectory(Path.Combine(GameFolder, "savegame10"));
        Directory.CreateDirectory(Path.Combine(GameFolder, "savegame1"));

        var discovery = new SavegameDiscovery(_tempFolder);
        var savegames = discovery.FindSavegames();

        Assert.Equal([1, 2, 10], savegames.Select(s => s.SlotIndex));
    }

    // ---- ModLoadOrderReader ----

    [Fact]
    public void ReadLoadOrder_MissingModsXml_ReturnsEmpty()
    {
        var reader = new ModLoadOrderReader();

        Assert.Empty(reader.ReadLoadOrder(_tempFolder));
    }

    [Fact]
    public void ReadLoadOrder_EmptyModsXml_ReturnsEmpty()
    {
        File.WriteAllText(Path.Combine(_tempFolder, "mods.xml"), """
            <?xml version="1.0" encoding="utf-8"?>
            <modsList>
            </modsList>
            """);

        var reader = new ModLoadOrderReader();

        Assert.Empty(reader.ReadLoadOrder(_tempFolder));
    }

    [Fact]
    public void ReadLoadOrder_ParsesNameActiveAndOrder()
    {
        File.WriteAllText(Path.Combine(_tempFolder, "mods.xml"), """
            <?xml version="1.0" encoding="utf-8"?>
            <modsList>
                <mod modName="FS25_ModA" active="true">FS25_ModA</mod>
                <mod modName="FS25_ModB" active="false">FS25_ModB</mod>
            </modsList>
            """);

        var reader = new ModLoadOrderReader();
        var entries = reader.ReadLoadOrder(_tempFolder);

        Assert.Equal(2, entries.Count);
        Assert.Equal(new ModLoadEntry("FS25_ModA", true, 0), entries[0]);
        Assert.Equal(new ModLoadEntry("FS25_ModB", false, 1), entries[1]);
    }

    // ---- ModLoadOrderWriter ----

    [Fact]
    public void WriteLoadOrder_RoundTripsThroughReader()
    {
        var entries = new List<ModLoadEntry>
        {
            new("FS25_ModB", false, 0),
            new("FS25_ModA", true, 1),
        };

        var writer = new ModLoadOrderWriter(new FakeGameProcessChecker(isRunning: false));
        writer.WriteLoadOrder(_tempFolder, entries);

        var reader = new ModLoadOrderReader();
        var roundTripped = reader.ReadLoadOrder(_tempFolder);

        Assert.Equal(entries, roundTripped);
    }

    [Fact]
    public void WriteLoadOrder_MissingModsXml_CreatesMinimalValidFile()
    {
        var entries = new List<ModLoadEntry> { new("FS25_ModA", true, 0) };
        var writer = new ModLoadOrderWriter(new FakeGameProcessChecker(isRunning: false));

        writer.WriteLoadOrder(_tempFolder, entries);

        Assert.True(File.Exists(Path.Combine(_tempFolder, "mods.xml")));
        Assert.False(File.Exists(Path.Combine(_tempFolder, "mods.xml.bak")));
    }

    [Fact]
    public void WriteLoadOrder_CreatesBackupOfOriginalFile()
    {
        var modsFile = Path.Combine(_tempFolder, "mods.xml");
        File.WriteAllText(modsFile, """
            <?xml version="1.0" encoding="utf-8"?>
            <modsList>
                <mod modName="FS25_Original" active="true">FS25_Original</mod>
            </modsList>
            """);

        var writer = new ModLoadOrderWriter(new FakeGameProcessChecker(isRunning: false));
        writer.WriteLoadOrder(_tempFolder, [new ModLoadEntry("FS25_New", true, 0)]);

        var backupFile = Path.Combine(_tempFolder, "mods.xml.bak");
        Assert.True(File.Exists(backupFile));
        Assert.Contains("FS25_Original", File.ReadAllText(backupFile));
    }

    [Fact]
    public void WriteLoadOrder_OverwritesBackupRatherThanAccumulating()
    {
        var modsFile = Path.Combine(_tempFolder, "mods.xml");
        var backupFile = Path.Combine(_tempFolder, "mods.xml.bak");
        var writer = new ModLoadOrderWriter(new FakeGameProcessChecker(isRunning: false));

        File.WriteAllText(modsFile, """
            <?xml version="1.0" encoding="utf-8"?>
            <modsList>
                <mod modName="FS25_First" active="true">FS25_First</mod>
            </modsList>
            """);
        writer.WriteLoadOrder(_tempFolder, [new ModLoadEntry("FS25_Second", true, 0)]);
        Assert.Contains("FS25_First", File.ReadAllText(backupFile));

        writer.WriteLoadOrder(_tempFolder, [new ModLoadEntry("FS25_Third", true, 0)]);

        Assert.Contains("FS25_Second", File.ReadAllText(backupFile));
        Assert.DoesNotContain("FS25_First", File.ReadAllText(backupFile));
    }

    [Fact]
    public void WriteLoadOrder_GameRunning_Throws()
    {
        var writer = new ModLoadOrderWriter(new FakeGameProcessChecker(isRunning: true));

        Assert.Throws<InvalidOperationException>(
            () => writer.WriteLoadOrder(_tempFolder, [new ModLoadEntry("FS25_ModA", true, 0)]));
    }

    private sealed class FakeGameProcessChecker(bool isRunning) : IGameProcessChecker
    {
        public bool IsGameRunning() => isRunning;
    }
}