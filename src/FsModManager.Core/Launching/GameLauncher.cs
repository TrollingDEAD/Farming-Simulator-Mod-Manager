using FsModManager.Core.Models;
using FsModManager.Core.Savegames;

namespace FsModManager.Core.Launching;

/// <summary>
/// Default <see cref="IGameLauncher"/>. Assumes any load-order write for <c>targetSavegame</c> has
/// already been performed by the caller via the Savegames services — this class only guards against
/// a second instance and starts the process. It does not touch mods.xml.
/// </summary>
public sealed class GameLauncher : IGameLauncher
{
    private readonly IGameProcessChecker _gameProcessChecker;
    private readonly IProcessStarter _processStarter;

    public GameLauncher(IGameProcessChecker gameProcessChecker, IProcessStarter processStarter)
    {
        _gameProcessChecker = gameProcessChecker;
        _processStarter = processStarter;
    }

    public void Launch(GameInstallation installation, SavegameInfo? targetSavegame = null)
    {
        if (_gameProcessChecker.IsGameRunning())
        {
            throw new GameAlreadyRunningException();
        }

        // NOTE: FS25 has no confirmed documented command-line argument to auto-select a savegame on
        // launch, so targetSavegame is only used by the caller to pick which mods.xml to prepare
        // beforehand; the user still picks the save in-game. Revisit if such an argument is ever confirmed.
        _processStarter.Start(installation.ExecutablePath);
    }
}
