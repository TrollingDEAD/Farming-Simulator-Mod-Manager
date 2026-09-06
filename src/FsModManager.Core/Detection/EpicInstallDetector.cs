using System.Text.Json;
using FsModManager.Core.Models;

namespace FsModManager.Core.Detection;

/// <summary>
/// Finds FS25 installations managed by the Epic Games Launcher by scanning the
/// launcher's .item manifests (JSON) under ProgramData for a Farming Simulator 25 entry.
/// </summary>
public sealed class EpicInstallDetector : IGameInstallDetector
{
    private const string ExeName = "FarmingSimulator2025.exe";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _manifestFolder;

    public EpicInstallDetector()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"Epic\EpicGamesLauncher\Data\Manifests"))
    {
    }

    /// <summary>Creates a detector scanning a custom manifest folder (useful for tests).</summary>
    public EpicInstallDetector(string manifestFolder)
    {
        _manifestFolder = manifestFolder;
    }

    public Task<IReadOnlyList<GameInstallation>> DetectAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Detect());

    private IReadOnlyList<GameInstallation> Detect()
    {
        if (!Directory.Exists(_manifestFolder))
        {
            return [];
        }

        var results = new List<GameInstallation>();
        foreach (var itemFile in Directory.EnumerateFiles(_manifestFolder, "*.item"))
        {
            EpicManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<EpicManifest>(File.ReadAllText(itemFile), JsonOptions);
            }
            catch (Exception)
            {
                continue; // unreadable/corrupt manifest — skip this entry, not the whole scan
            }

            if (manifest?.DisplayName is null
                || !manifest.DisplayName.Contains("Farming Simulator 25", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(manifest.InstallLocation))
            {
                continue;
            }

            var exePath = Path.Combine(manifest.InstallLocation, ExeName);
            if (!File.Exists(exePath))
            {
                continue;
            }

            results.Add(new GameInstallation(
                exePath,
                manifest.InstallLocation,
                GameVersionReader.GetVersion(exePath) ?? "Unknown",
                GameLaunchSource.EpicGames));
        }

        return results;
    }

    private sealed record EpicManifest(string? DisplayName, string? InstallLocation);
}
