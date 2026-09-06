namespace FsModManager.Core.Savegames;

/// <summary>Checks whether the game is currently running, so mods.xml is never written mid-session.</summary>
public interface IGameProcessChecker
{
    bool IsGameRunning();
}
