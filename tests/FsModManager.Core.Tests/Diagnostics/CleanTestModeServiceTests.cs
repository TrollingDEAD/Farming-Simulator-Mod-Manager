using FsModManager.Core.Diagnostics;
using FsModManager.Core.Launching;
using FsModManager.Core.Models;
using FsModManager.Core.Savegames;
using Xunit;

namespace FsModManager.Core.Tests.Diagnostics;

public sealed class CleanTestModeServiceTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public CleanTestModeServiceTests()
    {
        Directory.CreateDirectory(ModsFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, recursive: true);
        }
    }

    private string ModsFolder => Path.Combine(_tempFolder, "FarmingSimulator2025", "mods");

    private string StateFilePath => Path.Combine(_tempFolder, "state", "clean-test-session.json");

    private void SeedMods(params string[] fileNames)
    {
        foreach (var name in fileNames)
        {
            File.WriteAllText(Path.Combine(ModsFolder, name), "mod-contents");
        }
    }

    [Fact]
    public async Task EnterCleanTestMode_RemoveAll_MovesEveryFileToHoldingFolder()
    {
        SeedMods("FS25_ModA.zip", "FS25_ModB.zip", "FS25_ModC.zip");
        var service = new CleanTestModeService(new FakeGameProcessChecker(false), StateFilePath);

        var session = await service.EnterCleanTestModeAsync(ModsFolder, new CleanTestOptions(true, null, false));

        Assert.True(session.IsActive);
        Assert.Equal(3, session.MovedFileNames.Count);
        Assert.Empty(session.KeptFileNames);
        Assert.Empty(Directory.EnumerateFiles(ModsFolder));
        var holdingFiles = Directory.EnumerateFiles(session.HoldingFolderPath).Select(Path.GetFileName).ToList();
        Assert.Contains("FS25_ModA.zip", holdingFiles);
        Assert.Contains("FS25_ModB.zip", holdingFiles);
        Assert.Contains("FS25_ModC.zip", holdingFiles);
        Assert.True(File.Exists(StateFilePath));
    }

    [Fact]
    public async Task EnterCleanTestMode_KeepSubset_LeavesOnlyKeptModsInPlace()
    {
        SeedMods("FS25_ModA.zip", "FS25_ModB.zip", "FS25_ModC.zip");
        var service = new CleanTestModeService(new FakeGameProcessChecker(false), StateFilePath);

        var session = await service.EnterCleanTestModeAsync(
            ModsFolder, new CleanTestOptions(false, ["FS25_ModB.zip"], false));

        var remaining = Directory.EnumerateFiles(ModsFolder).Select(Path.GetFileName).ToList();
        Assert.Single(remaining);
        Assert.Contains("FS25_ModB.zip", remaining);
        Assert.Equal(2, session.MovedFileNames.Count);
        Assert.Single(session.KeptFileNames);
    }

    [Fact]
    public async Task ExitCleanTestMode_MovesEverythingBackAndClearsState()
    {
        SeedMods("FS25_ModA.zip", "FS25_ModB.zip");
        var service = new CleanTestModeService(new FakeGameProcessChecker(false), StateFilePath);
        var session = await service.EnterCleanTestModeAsync(ModsFolder, new CleanTestOptions(true, null, false));

        await service.ExitCleanTestModeAsync(session);

        var restored = Directory.EnumerateFiles(ModsFolder).Select(Path.GetFileName).ToList();
        Assert.Contains("FS25_ModA.zip", restored);
        Assert.Contains("FS25_ModB.zip", restored);
        Assert.False(Directory.Exists(session.HoldingFolderPath));
        Assert.False(File.Exists(StateFilePath));
    }

    [Fact]
    public async Task EnterAndExit_GuardedByGameRunning()
    {
        SeedMods("FS25_ModA.zip");
        var service = new CleanTestModeService(new FakeGameProcessChecker(true), StateFilePath);

        await Assert.ThrowsAsync<GameAlreadyRunningException>(
            () => service.EnterCleanTestModeAsync(ModsFolder, new CleanTestOptions(true, null, false)));

        var dummySession = new CleanTestSession("id", ModsFolder, ModsFolder, [], [], DateTime.UtcNow, false, true);
        await Assert.ThrowsAsync<GameAlreadyRunningException>(() => service.ExitCleanTestModeAsync(dummySession));
    }

    [Fact]
    public async Task TryLoadPersistedSession_DetectsInterruptedSession()
    {
        SeedMods("FS25_ModA.zip");
        var service = new CleanTestModeService(new FakeGameProcessChecker(false), StateFilePath);
        var session = await service.EnterCleanTestModeAsync(ModsFolder, new CleanTestOptions(true, null, false));

        // Simulate a fresh service instance after an app restart — nothing in memory, only the state file.
        var reopened = new CleanTestModeService(new FakeGameProcessChecker(false), StateFilePath);
        var leftover = reopened.TryLoadPersistedSession();

        Assert.NotNull(leftover);
        Assert.True(leftover!.IsActive);
        Assert.Equal(session.SessionId, leftover.SessionId);
        Assert.Equal(session.MovedFileNames.Count, leftover.MovedFileNames.Count);
    }

    [Fact]
    public void TryLoadPersistedSession_ReturnsNullWhenNoSessionExists()
    {
        var service = new CleanTestModeService(new FakeGameProcessChecker(false), StateFilePath);

        Assert.Null(service.TryLoadPersistedSession());
    }

    [Fact]
    public async Task ExitCleanTestMode_MissingFileDuringRestore_ThrowsAndKeepsStatePersisted()
    {
        SeedMods("FS25_ModA.zip", "FS25_ModB.zip");
        var service = new CleanTestModeService(new FakeGameProcessChecker(false), StateFilePath);
        var session = await service.EnterCleanTestModeAsync(ModsFolder, new CleanTestOptions(true, null, false));

        // Simulate a file going missing from the holding folder during the "restore" step.
        File.Delete(Path.Combine(session.HoldingFolderPath, "FS25_ModA.zip"));

        var ex = await Assert.ThrowsAsync<CleanTestRestoreVerificationException>(
            () => service.ExitCleanTestModeAsync(session));

        Assert.Contains("FS25_ModA.zip", ex.MissingFiles);
        // The mismatch must never be silently accepted — the state file stays so it's caught again next startup.
        Assert.True(File.Exists(StateFilePath));
        // The mod that WAS found should still have been restored rather than left stranded.
        Assert.Contains("FS25_ModB.zip", Directory.EnumerateFiles(ModsFolder).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(10, 5)]
    [InlineData(300, 150)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void SuggestNextBisectionSet_SuggestsRoughlyHalfOfUntested(int untestedCount, int expectedSuggested)
    {
        var allMods = Enumerable.Range(0, untestedCount)
            .Select(i => new ModFileInfo($"mod{i}.zip", $"mod{i}", null, null, true, [], $"Mod {i}", null))
            .ToList();

        var suggestion = CleanTestModeService.SuggestNextBisectionSet(allMods, []);

        Assert.Equal(expectedSuggested, suggestion.Count);
    }

    [Fact]
    public void SuggestNextBisectionSet_ExcludesAlreadyTestedGoodMods()
    {
        var allMods = Enumerable.Range(0, 4)
            .Select(i => new ModFileInfo($"mod{i}.zip", $"mod{i}", null, null, true, [], $"Mod {i}", null))
            .ToList();
        var testedGood = new[] { allMods[0], allMods[1] };

        var suggestion = CleanTestModeService.SuggestNextBisectionSet(allMods, testedGood);

        Assert.DoesNotContain(suggestion, m => testedGood.Contains(m));
        Assert.Single(suggestion);
    }

    [Fact]
    public void SuggestNextBisectionSet_NarrowsDownAcrossRounds()
    {
        var allMods = Enumerable.Range(0, 9)
            .Select(i => new ModFileInfo($"mod{i}.zip", $"mod{i}", null, null, true, [], $"Mod {i}", null))
            .ToList();

        var testedGood = new List<ModFileInfo>();
        var rounds = 0;
        while (true)
        {
            var suggestion = CleanTestModeService.SuggestNextBisectionSet(allMods, testedGood);
            if (suggestion.Count == 0)
            {
                break;
            }

            rounds++;
            testedGood.AddRange(suggestion);
            Assert.True(rounds <= 4); // 9 mods -> at most 4 rounds (5,2,1,1) to exhaust the untested set
        }

        Assert.Equal(allMods.Count, testedGood.Count);
    }

    private sealed class FakeGameProcessChecker(bool isRunning) : IGameProcessChecker
    {
        public bool IsGameRunning() => isRunning;
    }
}
