using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FsModManager.App.Services;
using FsModManager.App.Views;
using FsModManager.Core.Savegames;

namespace FsModManager.App.ViewModels;

/// <summary>Backs the Settings view for local mod-management preferences, plus the About section.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService _appSettings;
    private readonly ModLoadOrderWriter _loadOrderWriter;
    private readonly ChangelogService _changelog;
    private readonly AppUpdateCoordinator _updateCoordinator;

    public SettingsViewModel(
        AppSettingsService appSettings,
        ModLoadOrderWriter loadOrderWriter,
        ChangelogService changelog,
        AppUpdateCoordinator updateCoordinator)
    {
        _appSettings = appSettings;
        _loadOrderWriter = loadOrderWriter;
        _changelog = changelog;
        _updateCoordinator = updateCoordinator;
        _automaticSnapshotKeepCount = appSettings.AutomaticSnapshotKeepCount;
    }

    /// <summary>The running app's version, e.g. "v1.0.0" - shown in the About section.</summary>
    public string VersionDisplay => $"v{AppVersionInfo.Current}";

    [RelayCommand]
    private void ViewChangelog() => ChangelogDialog.Show("Changelog", _changelog.Releases);

    /// <summary>
    /// Manual "check right now" next to the version number - the automatic startup check still
    /// happens on its own; this just doesn't make the user wait for the next launch. The
    /// coordinator reports the outcome (up to date / update banner / not an installed build).
    /// </summary>
    [RelayCommand]
    private Task CheckForUpdatesAsync() => _updateCoordinator.CheckForUpdateNowAsync();

    [ObservableProperty]
    private string _modsFolderPath = string.Empty;

    // TODO: persist this setting (e.g. to a small settings.json under %LOCALAPPDATA%\FsModManager).

    /// <summary>
    /// How many automatic snapshots to keep per savegame. Persisted on change and pushed straight
    /// into the singleton <see cref="ModLoadOrderWriter"/> so it takes effect on the very next save.
    /// </summary>
    [ObservableProperty]
    private int _automaticSnapshotKeepCount;

    partial void OnAutomaticSnapshotKeepCountChanged(int value)
    {
        _appSettings.SaveAutomaticSnapshotKeepCount(value);
        _loadOrderWriter.AutomaticSnapshotKeepCount = _appSettings.AutomaticSnapshotKeepCount;
    }
}
