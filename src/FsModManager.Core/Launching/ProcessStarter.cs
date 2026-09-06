using System.Diagnostics;

namespace FsModManager.Core.Launching;

/// <summary>Default <see cref="IProcessStarter"/> backed by <see cref="Process.Start(string)"/>.</summary>
public sealed class ProcessStarter : IProcessStarter
{
    public void Start(string executablePath)
    {
        Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
    }
}
