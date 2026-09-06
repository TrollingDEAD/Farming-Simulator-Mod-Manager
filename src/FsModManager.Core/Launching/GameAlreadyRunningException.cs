namespace FsModManager.Core.Launching;

/// <summary>Thrown when a launch is requested while Farming Simulator 25 is already running.</summary>
public sealed class GameAlreadyRunningException : Exception
{
    public GameAlreadyRunningException()
        : base("Farming Simulator 25 is already running.")
    {
    }
}
