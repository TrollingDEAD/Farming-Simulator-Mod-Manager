using CommunityToolkit.Mvvm.ComponentModel;

namespace FsModManager.App.Services;

public enum AppView
{
    InstallSelection,
    ModList,
    LoadOrder,
    MultiplayerSync,
    Conflicts,
    LogAnalyzer,
    Diagnostics,
    CleanTest,
}

/// <summary>Shared navigation state; the shell listens to <see cref="CurrentView"/> and swaps the content.</summary>
public sealed partial class NavigationService : ObservableObject
{
    [ObservableProperty]
    private AppView _currentView = AppView.InstallSelection;

    public void NavigateTo(AppView view) => CurrentView = view;
}
