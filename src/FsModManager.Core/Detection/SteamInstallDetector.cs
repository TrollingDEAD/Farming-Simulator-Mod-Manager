using System.Runtime.Versioning;
using FsModManager.Core.Models;
using Microsoft.Win32;

namespace FsModManager.Core.Detection;

/// <summary>
/// Finds FS25 installations managed by Steam. Reads Steam's own install path from the
/// registry, enumerates all Steam libraries via steamapps/libraryfolders.vdf, and looks
/// for the app manifest of app id 2300320 in each library.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SteamInstallDetector : IGameInstallDetector
{
    private const string Fs25AppId = "2300320";
    private const string ExeName = "FarmingSimulator2025.exe";

    private readonly IRegistryReader _registry;

    public SteamInstallDetector()
        : this(new RegistryReader())
    {
    }

    public SteamInstallDetector(IRegistryReader registry)
    {
        _registry = registry;
    }

    public Task<IReadOnlyList<GameInstallation>> DetectAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Detect());

    private IReadOnlyList<GameInstallation> Detect()
    {
        var steamPath = GetSteamInstallPath();
        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
        {
            return [];
        }

        var results = new List<GameInstallation>();
        foreach (var library in GetLibraryPaths(steamPath))
        {
            var manifestPath = Path.Combine(library, "steamapps", $"appmanifest_{Fs25AppId}.acf");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var installDirName = ReadInstallDirName(manifestPath);
            if (string.IsNullOrWhiteSpace(installDirName))
            {
                continue;
            }

            var gameDir = Path.Combine(library, "steamapps", "common", installDirName);
            var exePath = Path.Combine(gameDir, ExeName);
            if (!File.Exists(exePath))
            {
                continue;
            }

            results.Add(new GameInstallation(
                exePath,
                gameDir,
                GameVersionReader.GetVersion(exePath) ?? "Unknown",
                GameLaunchSource.Steam));
        }

        return results;
    }

    private string? GetSteamInstallPath()
        => _registry.ReadString(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
           ?? _registry.ReadString(RegistryHive.CurrentUser, @"Software\Valve\Steam", "InstallPath")
           ?? _registry.ReadString(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath");

    private static IEnumerable<string> GetLibraryPaths(string steamPath)
    {
        // Steam's own folder is always a library; additional ones come from libraryfolders.vdf.
        yield return steamPath;

        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
        {
            yield break;
        }

        Dictionary<string, object> root;
        try
        {
            root = KeyValuesParser.Parse(File.ReadAllText(vdfPath));
        }
        catch (Exception)
        {
            yield break; // unreadable/corrupt vdf — fall back to the main library only
        }

        var folders = KeyValuesParser.GetSection(root, "libraryfolders");
        if (folders is null)
        {
            yield break;
        }

        foreach (var entry in folders)
        {
            // Modern format: "0" { "path" "D:\\SteamLibrary" ... }
            // Legacy format:  "1" "D:\\SteamLibrary"
            var path = entry.Value switch
            {
                Dictionary<string, object> section => KeyValuesParser.GetString(section, "path"),
                string legacyPath when char.IsDigit(entry.Key, 0) => legacyPath,
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path;
            }
        }
    }

    private static string? ReadInstallDirName(string manifestPath)
    {
        try
        {
            var root = KeyValuesParser.Parse(File.ReadAllText(manifestPath));
            var appState = KeyValuesParser.GetSection(root, "AppState");
            return appState is null ? null : KeyValuesParser.GetString(appState, "installdir");
        }
        catch (Exception)
        {
            return null;
        }
    }
}
