namespace FsModManager.Core.Launching;

/// <summary>Seam around <see cref="System.Diagnostics.Process.Start(string)"/> so launching is testable.</summary>
public interface IProcessStarter
{
    void Start(string executablePath);
}
