using FsModManager.Core.Models;

namespace FsModManager.Core.Launching;

/// <summary>Launches the game, optionally targeting a specific savegame.</summary>
public interface IGameLauncher
{
    /// <summary>
    /// Launches <paramref name="installation"/>'s executable. Throws
    /// <see cref="GameAlreadyRunningException"/> if the game is already running.
    /// </summary>
    void Launch(GameInstallation installation, SavegameInfo? targetSavegame = null);
}
