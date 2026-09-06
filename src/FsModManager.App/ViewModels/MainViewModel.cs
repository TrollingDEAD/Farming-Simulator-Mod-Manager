using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FsModManager.App.Services;

namespace FsModManager.App.ViewModels;

/// <summary>
/// Shell ViewModel: owns the sidebar navigation, the current content ViewModel,
/// and the notification banner list.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly NavigationService _navigation;
    private readonly INotificationService _notifications;
    private readonly InstallSelectionViewModel _installSelection;
    private readonly ModListViewModel _modList;
    private readonly LoadOrderViewModel _loadOrder;
    private readonly MultiplayerSyncViewModel _multiplayerSync;
    private readonly ConflictsViewModel _conflicts;
    private readonly LogAnalyzerViewModel _logAnalyzer;
    private readonly DiagnosticsViewModel _diagnostics;
    private readonly CleanTestWizardViewModel _cleanTest;
    private readonly SettingsViewModel _settings;

    public MainViewModel(
        NavigationService navigation,
        INotificationService notifications,
        InstallSelectionViewModel installSelection,
        ModListViewModel modList,
        LoadOrderViewModel loadOrder,
        MultiplayerSyncViewModel multiplayerSync,
        ConflictsViewModel conflicts,
        LogAnalyzerViewModel logAnalyzer,
        DiagnosticsViewModel diagnostics,
        CleanTestWizardViewModel cleanTest,
        SettingsViewModel settings)
    {
        _navigation = navigation;
        _notifications = notifications;
        _installSelection = installSelection;
        _modList = modList;
        _loadOrder = loadOrder;
        _multiplayerSync = multiplayerSync;
        _conflicts = conflicts;
        _logAnalyzer = logAnalyzer;
        _diagnostics = diagnostics;
        _cleanTest = cleanTest;
        _settings = settings;

        _currentViewModel = _installSelection;
        _navigation.PropertyChanged += OnNavigationPropertyChanged;

        // The app opens on install selection; start detection immediately.
        _ = _installSelection.OnNavigatedToAsync();
    }

    [ObservableProperty]
    private ObservableObject _currentViewModel;

    public ObservableCollection<AppNotification> Notifications => _notifications.Notifications;

    public bool IsInstallSelectionActive => _navigation.CurrentView == AppView.InstallSelection;

    public bool IsModListActive => _navigation.CurrentView == AppView.ModList;

    public bool IsLoadOrderActive => _navigation.CurrentView == AppView.LoadOrder;

    public bool IsMultiplayerSyncActive => _navigation.CurrentView == AppView.MultiplayerSync;

    public bool IsConflictsActive => _navigation.CurrentView == AppView.Conflicts;

    public bool IsLogAnalyzerActive => _navigation.CurrentView == AppView.LogAnalyzer;

    public bool IsDiagnosticsActive => _navigation.CurrentView == AppView.Diagnostics;

    public bool IsCleanTestActive => _navigation.CurrentView == AppView.CleanTest;

    public bool IsSettingsActive => _navigation.CurrentView == AppView.Settings;

    [RelayCommand]
    private void Navigate(AppView view) => _navigation.NavigateTo(view);

    [RelayCommand]
    private void DismissNotification(AppNotification? notification)
    {
        if (notification is not null)
        {
            _notifications.Dismiss(notification);
        }
    }

    private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NavigationService.CurrentView))
        {
            return;
        }

        CurrentViewModel = _navigation.CurrentView switch
        {
            AppView.ModList => _modList,
            AppView.LoadOrder => _loadOrder,
            AppView.MultiplayerSync => _multiplayerSync,
            AppView.Conflicts => _conflicts,
            AppView.LogAnalyzer => _logAnalyzer,
            AppView.Diagnostics => _diagnostics,
            AppView.CleanTest => _cleanTest,
            AppView.Settings => _settings,
            _ => _installSelection,
        };

        OnPropertyChanged(nameof(IsInstallSelectionActive));
        OnPropertyChanged(nameof(IsModListActive));
        OnPropertyChanged(nameof(IsLoadOrderActive));
        OnPropertyChanged(nameof(IsMultiplayerSyncActive));
        OnPropertyChanged(nameof(IsConflictsActive));
        OnPropertyChanged(nameof(IsLogAnalyzerActive));
        OnPropertyChanged(nameof(IsDiagnosticsActive));
        OnPropertyChanged(nameof(IsCleanTestActive));
        OnPropertyChanged(nameof(IsSettingsActive));

        _ = _navigation.CurrentView switch
        {
            AppView.ModList => _modList.OnNavigatedToAsync(),
            AppView.LoadOrder => _loadOrder.OnNavigatedToAsync(),
            AppView.MultiplayerSync => _multiplayerSync.OnNavigatedToAsync(),
            AppView.Conflicts => _conflicts.OnNavigatedToAsync(),
            AppView.LogAnalyzer => _logAnalyzer.OnNavigatedToAsync(),
            AppView.Diagnostics => _diagnostics.OnNavigatedToAsync(),
            AppView.CleanTest => _cleanTest.OnNavigatedToAsync(),
            AppView.Settings => Task.CompletedTask,
            _ => _installSelection.OnNavigatedToAsync(),
        };
    }
}
