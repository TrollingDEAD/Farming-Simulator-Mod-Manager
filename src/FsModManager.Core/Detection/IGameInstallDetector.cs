using FsModManager.Core.Models;

namespace FsModManager.Core.Detection;

/// <summary>
/// Detects Farming Simulator 25 installations from a single source (Steam, Epic, GOG, ...).
/// Implementations must never throw for "not installed" — they return an empty list instead.
/// </summary>
public interface IGameInstallDetector
{
    /// <summary>
    /// Returns all installations found by this detector. There may be more than one
    /// (e.g. Steam AND a manual copy on the same machine) — let the UI decide which to use.
    /// </summary>
    Task<IReadOnlyList<GameInstallation>> DetectAsync(CancellationToken cancellationToken = default);
}
