using System.Diagnostics;

namespace FsModManager.Core.Savegames;

/// <summary>Default <see cref="IGameProcessChecker"/> backed by the running process list.</summary>
public sealed class GameProcessChecker : IGameProcessChecker
{
    private const string ProcessName = "FarmingSimulator2025";

    public bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName(ProcessName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
