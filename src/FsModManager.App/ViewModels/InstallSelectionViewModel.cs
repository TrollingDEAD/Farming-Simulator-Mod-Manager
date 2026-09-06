using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FsModManager.App.Services;
using FsModManager.Core.Detection;
using FsModManager.Core.Models;
using Microsoft.Win32;

namespace FsModManager.App.ViewModels;

/// <summary>
/// First-run / install-picking screen: auto-detects FS25 installations (Steam/Epic/GOG via the
/// composite detector) or lets the user browse to a folder manually, then confirms a selection.
/// </summary>
public sealed partial class InstallSelectionViewModel : ObservableObject
{
    private readonly IGameInstallDetector _detector;
    private readonly AppState _appState;
    private readonly NavigationService _navigation;
    private readonly INotificationService _notifications;

    private bool _detectionStarted;

    public InstallSelectionViewModel(
        IGameInstallDetector detector,
        AppState appState,
        NavigationService navigation,
        INotificationService notifications)
    {
        _detector = detector;
        _appState = appState;
        _navigation = navigation;
        _notifications = notifications;
    }

    public ObservableCollection<GameInstallation> Installations { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowInstallations))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private GameInstallation? _selectedInstallation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowInstallations))]
    private bool _isDetecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowInstallations))]
    private bool _hasScanned;

    public bool HasSelection => SelectedInstallation is not null;

    /// <summary>True when a scan finished and found nothing — drives the empty-state placeholder.</summary>
    public bool ShowEmptyState => HasScanned && !IsDetecting && Installations.Count == 0;

    /// <summary>True when there is at least one installation to show in the list.</summary>
    public bool ShowInstallations => !ShowEmptyState;

    /// <summary>Kicks off detection once, the first time the view is shown.</summary>
    public async Task OnNavigatedToAsync()
    {
        if (_detectionStarted)
        {
            return;
        }

        _detectionStarted = true;
        await DetectAsync();
    }

    [RelayCommand]
    private async Task DetectAsync()
    {
        IsDetecting = true;
        try
        {
            var found = await _detector.DetectAsync();

            Installations.Clear();
            foreach (var installation in found)
            {
                Installations.Add(installation);
            }

            // Keep a still-valid manual pick selected, otherwise preselect the first hit.
            if (SelectedInstallation is null || !Installations.Contains(SelectedInstallation))
            {
                SelectedInstallation = Installations.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _notifications.Error($"Installation detection failed: {ex.Message}");
        }
        finally
        {
            IsDetecting = false;
            HasScanned = true;
        }
    }

    [RelayCommand]
    private void BrowseManually()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the Farming Simulator 25 install folder",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var installation = ManualInstallDetector.FromManualPath(dialog.FolderName);
        if (installation is null)
        {
            _notifications.Warning(
                $"No FarmingSimulator2025.exe found in '{dialog.FolderName}'. Pick the game's install folder.");
            return;
        }

        var existing = Installations.FirstOrDefault(i =>
            string.Equals(i.ExecutablePath, installation.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            Installations.Add(installation);
            existing = installation;
        }

        SelectedInstallation = existing;
        HasScanned = true;
        // HasScanned may already be true (setters don't re-raise on equal values) — nudge the
        // collection-dependent computed flags explicitly.
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowInstallations));
        _notifications.Info($"Added manual installation: {installation.InstallDirectory}");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Confirm()
    {
        if (SelectedInstallation is null)
        {
            return;
        }

        _appState.SelectedInstallation = SelectedInstallation;
        _navigation.NavigateTo(AppView.ModList);
    }
}
