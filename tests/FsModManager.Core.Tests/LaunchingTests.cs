using FsModManager.Core.Launching;
using FsModManager.Core.Models;
using FsModManager.Core.Savegames;
using Xunit;

namespace FsModManager.Core.Tests;

public sealed class LaunchingTests
{
    private sealed class FakeGameProcessChecker : IGameProcessChecker
    {
        public bool Running { get; set; }

        public bool IsGameRunning() => Running;
    }

    private sealed class FakeProcessStarter : IProcessStarter
    {
        public string? StartedExecutablePath { get; private set; }
        public int StartCallCount { get; private set; }

        public void Start(string executablePath)
        {
            StartCallCount++;
            StartedExecutablePath = executablePath;
        }
    }

    private static GameInstallation MakeInstallation(string executablePath = @"C:\Games\FS25\FarmingSimulator2025.exe") =>
        new(executablePath, @"C:\Games\FS25", "1.0.0.0", GameLaunchSource.Steam);

    [Fact]
    public void Launch_GameAlreadyRunning_ThrowsAndDoesNotStartProcess()
    {
        var processChecker = new FakeGameProcessChecker { Running = true };
        var processStarter = new FakeProcessStarter();
        var launcher = new GameLauncher(processChecker, processStarter);

        Assert.Throws<GameAlreadyRunningException>(() => launcher.Launch(MakeInstallation()));

        Assert.Equal(0, processStarter.StartCallCount);
    }

    [Fact]
    public void Launch_GameNotRunning_StartsCorrectExecutablePath()
    {
        var processChecker = new FakeGameProcessChecker { Running = false };
        var processStarter = new FakeProcessStarter();
        var launcher = new GameLauncher(processChecker, processStarter);
        var installation = MakeInstallation(@"D:\SteamLibrary\FS25\FarmingSimulator2025.exe");

        launcher.Launch(installation);

        Assert.Equal(1, processStarter.StartCallCount);
        Assert.Equal(@"D:\SteamLibrary\FS25\FarmingSimulator2025.exe", processStarter.StartedExecutablePath);
    }

    [Fact]
    public void Launch_WithTargetSavegame_GameNotRunning_StartsProcess()
    {
        var processChecker = new FakeGameProcessChecker { Running = false };
        var processStarter = new FakeProcessStarter();
        var launcher = new GameLauncher(processChecker, processStarter);
        var savegame = new SavegameInfo(@"C:\Saves\savegame1", "My Farm", 1);

        launcher.Launch(MakeInstallation(), savegame);

        Assert.Equal(1, processStarter.StartCallCount);
    }
}
