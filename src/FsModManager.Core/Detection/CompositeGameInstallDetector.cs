using System.Runtime.Versioning;
using FsModManager.Core.Models;

namespace FsModManager.Core.Detection;

/// <summary>
/// Runs several <see cref="IGameInstallDetector"/>s in parallel and aggregates the results.
/// A failing detector never takes the others down, and results are de-duplicated by
/// executable path (the same install can be visible to more than one launcher).
/// </summary>
public sealed class CompositeGameInstallDetector : IGameInstallDetector
{
    private readonly IReadOnlyList<IGameInstallDetector> _detectors;

    public CompositeGameInstallDetector(IEnumerable<IGameInstallDetector> detectors)
    {
        _detectors = detectors.ToList();
    }

    /// <summary>Creates a composite with all built-in auto-detectors (Steam, Epic, GOG).</summary>
    [SupportedOSPlatform("windows")]
    public static CompositeGameInstallDetector CreateDefault()
        => new(new IGameInstallDetector[]
        {
            new SteamInstallDetector(),
            new EpicInstallDetector(),
            new GogInstallDetector(),
        });

    public async Task<IReadOnlyList<GameInstallation>> DetectAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _detectors.Select(d => DetectSafelyAsync(d, cancellationToken)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return results
            .SelectMany(r => r)
            .GroupBy(i => i.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static async Task<IReadOnlyList<GameInstallation>> DetectSafelyAsync(
        IGameInstallDetector detector,
        CancellationToken cancellationToken)
    {
        try
        {
            // Task.Run so the detectors' synchronous file/registry I/O actually runs in parallel.
            return await Task.Run(() => detector.DetectAsync(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // One broken source must not kill detection of the others.
            return [];
        }
    }
}
