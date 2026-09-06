using System.IO;
using System.Text.Json;
using FsModManager.Core.Savegames;

namespace FsModManager.App.Services;

/// <summary>
/// Persists small app preferences as JSON under %LOCALAPPDATA%\FsModManager\app-settings.json
/// (same folder as the database, logs, and window settings). Currently holds the savegame
/// backup retention count, which is forwarded into <see cref="ModLoadOrderWriter"/> so pruning
/// after each load-order save keeps the configured number of automatic snapshots.
/// Never throws: missing/malformed files fall back to defaults.
/// </summary>
public sealed class AppSettingsService
{
    /// <summary>Bounds for <see cref="AutomaticSnapshotKeepCount"/> — clamps nonsense values from hand-edited JSON.</summary>
    public const int MinAutomaticSnapshotKeepCount = 1;
    public const int MaxAutomaticSnapshotKeepCount = 100;

    private const int DefaultAutomaticSnapshotKeepCount = 10;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;

    private sealed record SettingsModel(int? AutomaticSnapshotKeepCount, string? LastSeenVersion);

    public AppSettingsService()
    {
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FsModManager");
        Directory.CreateDirectory(appDataFolder);
        _settingsPath = Path.Combine(appDataFolder, "app-settings.json");
    }

    /// <summary>Creates a service reading/writing an explicit settings file (used by tests).</summary>
    public AppSettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    /// <summary>How many automatic (non-manual) snapshots to keep per savegame after pruning.</summary>
    public int AutomaticSnapshotKeepCount { get; private set; } = DefaultAutomaticSnapshotKeepCount;

    /// <summary>
    /// The app version last seen on a previous launch, used to detect "the app was just updated"
    /// so a what's-new-since-your-last-version view can be shown. Null on a brand-new install.
    /// </summary>
    public string? LastSeenVersion { get; private set; }

    public void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return;
            }

            var model = JsonSerializer.Deserialize<SettingsModel>(File.ReadAllText(_settingsPath));
            if (model?.AutomaticSnapshotKeepCount is { } keepCount)
            {
                AutomaticSnapshotKeepCount = ClampKeepCount(keepCount);
            }

            LastSeenVersion = model?.LastSeenVersion;
        }
        catch (Exception)
        {
            // Malformed/unreadable settings file — keep defaults rather than failing startup.
        }
    }

    public void SaveLastSeenVersion(string version)
    {
        LastSeenVersion = version;
        try
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new SettingsModel(AutomaticSnapshotKeepCount, LastSeenVersion), JsonOptions));
        }
        catch (Exception)
        {
            // A settings write failure must never break the app — retried on the next launch.
        }
    }

    public void SaveAutomaticSnapshotKeepCount(int keepCount)
    {
        AutomaticSnapshotKeepCount = ClampKeepCount(keepCount);
        try
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new SettingsModel(AutomaticSnapshotKeepCount, LastSeenVersion), JsonOptions));
        }
        catch (Exception)
        {
            // A settings write failure must never break the app — the in-memory value still applies
            // for this session and the next save attempt will retry the write.
        }
    }

    private static int ClampKeepCount(int keepCount) =>
        Math.Clamp(keepCount, MinAutomaticSnapshotKeepCount, MaxAutomaticSnapshotKeepCount);
}
