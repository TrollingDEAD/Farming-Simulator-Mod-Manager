using System.Runtime.Versioning;
using FsModManager.Core.Models;
using Microsoft.Win32;

namespace FsModManager.Core.Detection;

/// <summary>
/// Finds FS25 installations managed by GOG Galaxy by walking the game entries under
/// HKLM\SOFTWARE\WOW6432Node\GOG.com\Games and matching on the game name.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GogInstallDetector : IGameInstallDetector
{
    private const string GamesKeyPath = @"SOFTWARE\WOW6432Node\GOG.com\Games";
    private const string ExeName = "FarmingSimulator2025.exe";

    private readonly IRegistryReader _registry;

    public GogInstallDetector()
        : this(new RegistryReader())
    {
    }

    public GogInstallDetector(IRegistryReader registry)
    {
        _registry = registry;
    }

    public Task<IReadOnlyList<GameInstallation>> DetectAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Detect());

    private IReadOnlyList<GameInstallation> Detect()
    {
        var results = new List<GameInstallation>();

        foreach (var subKeyName in _registry.GetSubKeyNames(RegistryHive.LocalMachine, GamesKeyPath))
        {
            var gameKeyPath = $@"{GamesKeyPath}\{subKeyName}";
            var gameName = _registry.ReadString(RegistryHive.LocalMachine, gameKeyPath, "gameName")
                           ?? _registry.ReadString(RegistryHive.LocalMachine, gameKeyPath, "name");

            if (gameName is null
                || !gameName.Contains("Farming Simulator 25", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var installPath = _registry.ReadString(RegistryHive.LocalMachine, gameKeyPath, "path");
            if (string.IsNullOrWhiteSpace(installPath))
            {
                continue;
            }

            var exePath = Path.Combine(installPath, ExeName);
            if (!File.Exists(exePath))
            {
                continue;
            }

            results.Add(new GameInstallation(
                exePath,
                installPath,
                GameVersionReader.GetVersion(exePath) ?? "Unknown",
                GameLaunchSource.Gog));
        }

        return results;
    }
}
