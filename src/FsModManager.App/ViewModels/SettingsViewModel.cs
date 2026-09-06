using CommunityToolkit.Mvvm.ComponentModel;
using FsModManager.App.Services;
using FsModManager.Core.Savegames;

namespace FsModManager.App.ViewModels;

/// <summary>Backs the Settings view for local mod-management preferences.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService _appSettings;
    private readonly ModLoadOrderWriter _loadOrderWriter;

    public SettingsViewModel(AppSettingsService appSettings, ModLoadOrderWriter loadOrderWriter)
    {
        _appSettings = appSettings;
        _loadOrderWriter = loadOrderWriter;
        _automaticSnapshotKeepCount = appSettings.AutomaticSnapshotKeepCount;
    }

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
