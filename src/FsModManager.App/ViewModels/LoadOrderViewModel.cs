using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FsModManager.App.Services;
using FsModManager.App.Views;
using FsModManager.Core.Backups;
using FsModManager.Core.Launching;
using FsModManager.Core.Models;
using FsModManager.Core.Savegames;

namespace FsModManager.App.ViewModels;

/// <summary>
/// Backs the load-order view: pick a savegame, reorder/toggle its mods.xml entries,
/// save (blocked while the game runs), and launch the game.
/// </summary>
public sealed partial class LoadOrderViewModel : ObservableObject, IDisposable
{
    private readonly SavegameDiscovery _savegameDiscovery;
    private readonly ModLoadOrderReader _loadOrderReader;
    private readonly ModLoadOrderWriter _loadOrderWriter;
    private readonly IGameProcessChecker _gameProcessChecker;
    private readonly IGameLauncher _gameLauncher;
    private readonly SavegameSnapshotService _snapshotService;
    private readonly AppState _appState;
    private readonly INotificationService _notifications;
    private readonly DispatcherTimer _gameRunningTimer;

    public LoadOrderViewModel(
        SavegameDiscovery savegameDiscovery,
        ModLoadOrderReader loadOrderReader,
        ModLoadOrderWriter loadOrderWriter,
        IGameProcessChecker gameProcessChecker,
        IGameLauncher gameLauncher,
        SavegameSnapshotService snapshotService,
        AppState appState,
        INotificationService notifications)
    {
        _savegameDiscovery = savegameDiscovery;
        _loadOrderReader = loadOrderReader;
        _loadOrderWriter = loadOrderWriter;
        _gameProcessChecker = gameProcessChecker;
        _gameLauncher = gameLauncher;
        _snapshotService = snapshotService;
        _appState = appState;
        _notifications = notifications;

        _gameRunningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _gameRunningTimer.Tick += (_, _) => RefreshGameRunningState();
    }

    public ObservableCollection<SavegameInfo> Savegames { get; } = new();

    public ObservableCollection<LoadEntryViewModel> Entries { get; } = new();

    public ObservableCollection<SnapshotRowViewModel> Snapshots { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMultiplayerHint))]
    private int _activeEntryCount;

    /// <summary>Shows the multiplayer-manifest hint once a savegame with active mods is selected —
    /// the natural moment someone is preparing to play. Shown per selection, not just once ever,
    /// so it also catches the user who only discovers the feature later.</summary>
    public bool ShowMultiplayerHint => SelectedSavegame is not null && ActiveEntryCount > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowSelectPrompt))]
    [NotifyPropertyChangedFor(nameof(ShowNoEntriesMessage))]
    [NotifyPropertyChangedFor(nameof(ShowEntries))]
    [NotifyPropertyChangedFor(nameof(ShowBackupsPanel))]
    [NotifyPropertyChangedFor(nameof(ShowBackupsEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowMultiplayerHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreSnapshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenBackupsFolderCommand))]
    private SavegameInfo? _selectedSavegame;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowSelectPrompt))]
    private bool _hasScanned;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreSnapshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenBackupsFolderCommand))]
    private bool _isGameRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreSnapshotCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenBackupsFolderCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _totalBackupSizeText = string.Empty;

    private NavigationService? _navigation;

    /// <summary>Called right after construction (same scope) — late-injected so the multiplayer
    /// hint button can jump straight to the Multiplayer Sync tab without a ctor parameter.</summary>
    internal void SetNavigation(NavigationService navigation) => _navigation = navigation;

    public bool ShowEmptyState => HasScanned && Savegames.Count == 0;

    /// <summary>Savegames exist, but nothing's picked yet.</summary>
    public bool ShowSelectPrompt => HasScanned && Savegames.Count > 0 && SelectedSavegame is null;

    /// <summary>A savegame is selected and its mods.xml was read successfully but has zero entries —
    /// distinct from "broken", since an empty list alone looks identical to a stuck UI otherwise.</summary>
    public bool ShowNoEntriesMessage => SelectedSavegame is not null && Entries.Count == 0;

    public bool ShowEntries => SelectedSavegame is not null && Entries.Count > 0;

    /// <summary>The backups section is only meaningful once a savegame is selected.</summary>
    public bool ShowBackupsPanel => SelectedSavegame is not null;

    public bool HasSnapshots => Snapshots.Count > 0;

    /// <summary>Selected savegame with zero snapshots: explain the feature instead of showing a blank list.</summary>
    public bool ShowBackupsEmptyState => SelectedSavegame is not null && Snapshots.Count == 0;

    public Task OnNavigatedToAsync()
    {
        RefreshSavegames();
        RefreshGameRunningState();
        _gameRunningTimer.Start();
        return Task.CompletedTask;
    }

    public void Dispose() => _gameRunningTimer.Stop();

    partial void OnSelectedSavegameChanged(SavegameInfo? value)
    {
        LoadEntries();
        RefreshSnapshots();
    }

    [RelayCommand]
    private void RefreshSavegames()
    {
        try
        {
            var found = _savegameDiscovery.FindSavegames();
            var previous = SelectedSavegame;

            Savegames.Clear();
            foreach (var savegame in found)
            {
                Savegames.Add(savegame);
            }

            SelectedSavegame = previous is not null
                ? Savegames.FirstOrDefault(s => s.FolderPath == previous.FolderPath)
                : Savegames.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _notifications.Error($"Savegame discovery failed: {ex.Message}");
        }
        finally
        {
            HasScanned = true;
        }

        // HasScanned's setter won't re-raise when it was already true — the collection-dependent
        // computed flags need an explicit nudge after every refresh.
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowSelectPrompt));
        OnPropertyChanged(nameof(ShowNoEntriesMessage));
        OnPropertyChanged(nameof(ShowEntries));
    }

    private void LoadEntries()
    {
        Entries.Clear();
        if (SelectedSavegame is null)
        {
            ActiveEntryCount = 0;
            OnPropertyChanged(nameof(ShowNoEntriesMessage));
            OnPropertyChanged(nameof(ShowEntries));
            return;
        }

        try
        {
            foreach (var entry in _loadOrderReader.ReadLoadOrder(SelectedSavegame.FolderPath))
            {
                Entries.Add(new LoadEntryViewModel(entry.InternalModName, entry.Active));
            }
        }
        catch (Exception ex)
        {
            _notifications.Error($"Failed to read mods.xml for '{SelectedSavegame.DisplayName}': {ex.Message}");
        }
        finally
        {
            // Entries.Count-dependent flags won't auto-notify from Clear()/Add() alone.
            ActiveEntryCount = Entries.Count(e => e.Active);
            OnPropertyChanged(nameof(ShowNoEntriesMessage));
            OnPropertyChanged(nameof(ShowEntries));
        }
    }

    /// <summary>Moves an entry from one position to another (drag-and-drop reordering from the view).</summary>
    public void MoveEntry(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Entries.Count || toIndex < 0 || toIndex >= Entries.Count
            || fromIndex == toIndex)
        {
            return;
        }

        Entries.Move(fromIndex, toIndex);
    }

    /// <summary>Jumps to the Multiplayer Sync tab to export/compare the mod manifest for this savegame.</summary>
    [RelayCommand]
    private void GoToMultiplayerSync() => _navigation?.NavigateTo(AppView.MultiplayerSync);

    private bool CanSave() => SelectedSavegame is not null && Entries.Count > 0 && !IsGameRunning && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (SelectedSavegame is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var entries = Entries.Select((row, index) => row.ToEntry(index)).ToList();
            await _loadOrderWriter.WriteLoadOrderAsync(SelectedSavegame, entries);
            _notifications.Info($"Load order saved for '{SelectedSavegame.DisplayName}' (savegame snapshot taken; mods.xml.bak kept as quick undo).");
            RefreshSnapshots();
        }
        catch (GameAlreadyRunningException ex)
        {
            // Game-running guard from the snapshot service.
            _notifications.Warning(ex.Message);
            RefreshGameRunningState();
        }
        catch (InvalidOperationException ex)
        {
            // Game running guard from the writer.
            _notifications.Warning(ex.Message);
            RefreshGameRunningState();
        }
        catch (Exception ex)
        {
            _notifications.Error($"Failed to save load order: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCreateBackup() => SelectedSavegame is not null && !IsGameRunning && !IsBusy;

    private bool CanOpenBackupsFolder() => SelectedSavegame is not null && !IsBusy;

    /// <summary>Opens the backups folder in Explorer (creating it first) so users can see/copy the zips directly.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenBackupsFolder))]
    private void OpenBackupsFolder()
    {
        if (SelectedSavegame is null)
        {
            return;
        }

        try
        {
            var folder = _snapshotService.GetSnapshotFolderPath(SelectedSavegame);
            Directory.CreateDirectory(folder);
            var explorerPath = Path.Combine(Environment.SystemDirectory, "explorer.exe");
            System.Diagnostics.Process.Start(explorerPath, $"\"{folder}\"");
        }
        catch (Exception ex)
        {
            _notifications.Error($"Failed to open the backups folder: {ex.Message}");
        }
    }

    /// <summary>Manual backup, taggable as such so it is exempt from automatic pruning.</summary>
    [RelayCommand(CanExecute = nameof(CanCreateBackup))]
    private async Task CreateBackupAsync()
    {
        if (SelectedSavegame is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var path = await _snapshotService.CreateSnapshotAsync(SelectedSavegame, SavegameSnapshotService.ManualReason);
            _notifications.Info($"Backup of '{SelectedSavegame.DisplayName}' created: {Path.GetFileName(path)}");
        }
        catch (GameAlreadyRunningException ex)
        {
            _notifications.Warning(ex.Message);
            RefreshGameRunningState();
        }
        catch (Exception ex)
        {
            _notifications.Error($"Failed to create backup: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }

        RefreshSnapshots();
    }

    private bool CanRestoreSnapshot(SnapshotRowViewModel? row) =>
        row is not null && SelectedSavegame is not null && !IsGameRunning && !IsBusy;

    /// <summary>
    /// Restores a snapshot over the selected savegame. Destructive-feeling enough to warrant an
    /// explicit confirmation, even though the service snapshots the current state first.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestoreSnapshot))]
    private async Task RestoreSnapshotAsync(SnapshotRowViewModel? row)
    {
        if (row is null || SelectedSavegame is null)
        {
            return;
        }

        var confirmed = AppDialog.Confirm(
            "Restore backup",
            $"Restoring this backup will replace the current state of '{SelectedSavegame.DisplayName}' " +
            $"with the snapshot from {row.CreatedText} ({row.Reason}).\n\n" +
            "First, a new backup of the current state is taken automatically (tagged \"before-restore\"), " +
            "then the snapshot's contents replace the savegame's current files. " +
            "Anything in the savegame folder that is not in the snapshot is removed.\n\n" +
            "Continue?",
            "Restore");
        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _snapshotService.RestoreSnapshotAsync(row.FilePath, SelectedSavegame);
            _notifications.Info(
                $"Backup restored to '{SelectedSavegame.DisplayName}'. The previous state was kept as a \"before-restore\" backup.");
            // The restored mods.xml likely differs from what the list currently shows.
            LoadEntries();
        }
        catch (GameAlreadyRunningException ex)
        {
            _notifications.Warning(ex.Message);
            RefreshGameRunningState();
        }
        catch (Exception ex)
        {
            _notifications.Error($"Failed to restore backup: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }

        RefreshSnapshots();
    }

    private void RefreshSnapshots()
    {
        Snapshots.Clear();
        if (SelectedSavegame is null)
        {
            TotalBackupSizeText = string.Empty;
            OnPropertyChanged(nameof(HasSnapshots));
            OnPropertyChanged(nameof(ShowBackupsEmptyState));
            return;
        }

        try
        {
            var snapshots = _snapshotService.ListSnapshots(SelectedSavegame);
            foreach (var snapshot in snapshots)
            {
                Snapshots.Add(new SnapshotRowViewModel(snapshot));
            }

            var totalBytes = snapshots.Sum(s => s.SizeBytes);
            TotalBackupSizeText = snapshots.Count == 0
                ? string.Empty
                : $"{SnapshotRowViewModel.FormatSize(totalBytes)} used by {snapshots.Count} snapshot{(snapshots.Count == 1 ? "" : "s")}";
        }
        catch (Exception ex)
        {
            TotalBackupSizeText = string.Empty;
            _notifications.Error($"Failed to list backups: {ex.Message}");
        }
        finally
        {
            OnPropertyChanged(nameof(HasSnapshots));
            OnPropertyChanged(nameof(ShowBackupsEmptyState));
        }
    }

    [RelayCommand]
    private void LaunchGame()
    {
        if (_appState.SelectedInstallation is null)
        {
            _notifications.Error("No game installation selected. Pick one on the Install Selection screen first.");
            return;
        }

        try
        {
            _gameLauncher.Launch(_appState.SelectedInstallation, SelectedSavegame);
            _notifications.Info("Farming Simulator 25 launched. Pick the save in-game — CLI save selection isn't supported by FS25.");
        }
        catch (GameAlreadyRunningException ex)
        {
            _notifications.Warning(ex.Message);
            RefreshGameRunningState();
        }
        catch (Exception ex)
        {
            _notifications.Error($"Failed to launch the game: {ex.Message}");
        }
    }

    private void RefreshGameRunningState()
    {
        try
        {
            IsGameRunning = _gameProcessChecker.IsGameRunning();
        }
        catch (Exception)
        {
            // If the process check itself fails, don't block saving on a phantom "running" state.
            IsGameRunning = false;
        }
    }
}
