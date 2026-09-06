using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FsModManager.App.Services;
using FsModManager.App.Views;
using FsModManager.Core.Diagnostics;
using FsModManager.Core.Launching;
using FsModManager.Core.Models;
using FsModManager.Core.Savegames;

namespace FsModManager.App.ViewModels;

/// <summary>Which step of the guided Clean Test wizard is currently showing.</summary>
public enum CleanTestWizardStep
{
    /// <summary>Explain what's about to happen, get explicit confirmation.</summary>
    Intro,

    /// <summary>Mods are moved out (or a bisection subset is active); waiting for the user to test and report.</summary>
    Active,

    /// <summary>Problem was gone with (near-)zero mods — mods confirmed as the cause.</summary>
    ConfirmedModCause,

    /// <summary>Problem persisted even with (near-)zero mods — very likely not a mod issue.</summary>
    NotModIssue,

    /// <summary>Bisecting the confirmed-bad set down to a single suspect (or small handful).</summary>
    Bisection,

    /// <summary>A single suspect mod (or small handful) has been isolated.</summary>
    BisectionComplete,
}

/// <summary>
/// Opaque blob persisted into <see cref="CleanTestSession.WizardStateJson"/> so the wizard can
/// resume exactly where the user left off after a crash/close, not just "a session is active".
/// Mods are tracked by zip file name only (matches how <see cref="CleanTestSession"/> itself
/// identifies moved files) so this never needs to carry full <see cref="ModFileInfo"/> objects.
/// </summary>
internal sealed record CleanTestWizardProgress(
    CleanTestWizardStep Step,
    int RoundNumber,
    List<string> BisectionUniverseNames,
    List<string> UniverseConfirmedGoodNames,
    List<string> PermanentlyConfirmedGoodNames,
    List<string>? SuspectNames);

/// <summary>
/// Backs the guided "Troubleshoot: Clean Test" wizard: temporarily move mods out (never delete),
/// optionally clear the shader cache, let the user test with a fresh save, and either confirm mods
/// aren't the problem or bisect down to the one responsible mod. An "exit and restore everything"
/// escape hatch is available from every step once a session is active.
/// </summary>
public sealed partial class CleanTestWizardViewModel : ObservableObject
{
    private readonly ModListViewModel _modList;
    private readonly CleanTestModeService _cleanTestMode;
    private readonly IGameProcessChecker _gameProcessChecker;
    private readonly IGameLauncher _gameLauncher;
    private readonly AppState _appState;
    private readonly INotificationService _notifications;
    private readonly NavigationService _navigation;

    private List<ModFileInfo> _bisectionUniverse = new();
    private List<ModFileInfo> _universeConfirmedGood = new();
    private List<ModFileInfo> _permanentlyConfirmedGood = new();
    private IReadOnlyList<ModFileInfo> _lastSuggestion = new List<ModFileInfo>();
    private List<ModFileInfo> _suspects = new();

    public CleanTestWizardViewModel(
        ModListViewModel modList,
        CleanTestModeService cleanTestMode,
        IGameProcessChecker gameProcessChecker,
        IGameLauncher gameLauncher,
        AppState appState,
        INotificationService notifications,
        NavigationService navigation)
    {
        _modList = modList;
        _cleanTestMode = cleanTestMode;
        _gameProcessChecker = gameProcessChecker;
        _gameLauncher = gameLauncher;
        _appState = appState;
        _notifications = notifications;
        _navigation = navigation;
    }

    [ObservableProperty]
    private CleanTestWizardStep _step = CleanTestWizardStep.Intro;

    [ObservableProperty]
    private bool _clearShaderCache = true;

    [ObservableProperty]
    private bool _isAwaitingTestResult;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private CleanTestSession? _currentSession;

    [ObservableProperty]
    private int _roundNumber;

    [ObservableProperty]
    private int _estimatedTotalRounds;

    [ObservableProperty]
    private int _untestedRemainingCount;

    [ObservableProperty]
    private string _roundProgressText = string.Empty;

    public ObservableCollection<string> SuspectNames { get; } = new();

    public bool HasActiveSession => CurrentSession is { IsActive: true };

    /// <summary>Escape hatch visibility — available any time a session is active, from any step.</summary>
    public bool ShowExitEscapeHatch => HasActiveSession;

    public async Task OnNavigatedToAsync()
    {
        await _modList.OnNavigatedToAsync();

        var leftover = _cleanTestMode.TryLoadPersistedSession();
        if (leftover is { IsActive: true })
        {
            CurrentSession = leftover;
            ResumeFromPersistedProgress(leftover);
        }
        else if (CurrentSession is null)
        {
            Step = CleanTestWizardStep.Intro;
        }
    }

    private void ResumeFromPersistedProgress(CleanTestSession session)
    {
        if (string.IsNullOrWhiteSpace(session.WizardStateJson))
        {
            // A session exists but we have no wizard-step record for it (e.g. an older session) —
            // land on Active rather than guessing a later step from ambiguous file-move data.
            Step = CleanTestWizardStep.Active;
            IsAwaitingTestResult = false;
            return;
        }

        try
        {
            var progress = JsonSerializer.Deserialize<CleanTestWizardProgress>(session.WizardStateJson);
            if (progress is null)
            {
                Step = CleanTestWizardStep.Active;
                return;
            }

            var allMods = BuildAllModFileInfos();
            _bisectionUniverse = ResolveByName(allMods, progress.BisectionUniverseNames);
            _universeConfirmedGood = ResolveByName(allMods, progress.UniverseConfirmedGoodNames);
            _permanentlyConfirmedGood = ResolveByName(allMods, progress.PermanentlyConfirmedGoodNames);
            _suspects = ResolveByName(allMods, progress.SuspectNames ?? []);
            RoundNumber = progress.RoundNumber;
            Step = progress.Step;
            IsAwaitingTestResult = false;
            UpdateRoundProgressText();

            SuspectNames.Clear();
            foreach (var suspect in _suspects)
            {
                SuspectNames.Add(suspect.DisplayTitle);
            }
        }
        catch (Exception)
        {
            // Malformed wizard-state blob must never block resuming — fall back to the safe default.
            Step = CleanTestWizardStep.Active;
        }
    }

    private static List<ModFileInfo> ResolveByName(IReadOnlyList<ModFileInfo> allMods, IReadOnlyList<string> names) =>
        allMods.Where(m => names.Contains(Path.GetFileName(m.ZipPath), StringComparer.OrdinalIgnoreCase)).ToList();

    private List<ModFileInfo> BuildAllModFileInfos() =>
        _modList.Mods
            .Where(r => !string.IsNullOrWhiteSpace(r.Metadata.SourceFileName))
            .Select(r => new ModFileInfo(
                Path.GetFileName(r.Metadata.SourceFileName!),
                r.Metadata.InternalName,
                r.Metadata.DescVersion,
                r.Metadata.Author,
                r.Metadata.DescVersionParsed,
                r.Metadata.Warnings,
                r.Metadata.DisplayTitle,
                null))
            .ToList();

    [RelayCommand]
    private async Task BeginCleanTestAsync()
    {
        if (_appState.ModsFolderPath is not { Length: > 0 } modsFolder)
        {
            _notifications.Error("No mods folder detected yet. Select an installation and let it scan first.");
            return;
        }

        IsBusy = true;
        try
        {
            var session = await _cleanTestMode.EnterCleanTestModeAsync(
                modsFolder, new CleanTestOptions(RemoveAllMods: true, ModsToKeep: null, ClearShaderCache: ClearShaderCache));
            CurrentSession = session;
            Step = CleanTestWizardStep.Active;
            IsAwaitingTestResult = false;
            await PersistWizardStateAsync();
            _notifications.Info(session.ShaderCacheCleared
                ? "All mods moved out and the shader cache was cleared. Test with a new save, not an existing one."
                : "All mods moved out. Test with a new save, not an existing one.");
        }
        catch (GameAlreadyRunningException ex)
        {
            _notifications.Warning(ex.Message);
        }
        catch (Exception ex)
        {
            _notifications.Error($"Failed to enter clean test mode: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
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
            _gameLauncher.Launch(_appState.SelectedInstallation);
            _notifications.Info("Farming Simulator 25 launched. Start a NEW save for this test — an existing save may still reference mod data.");
        }
        catch (GameAlreadyRunningException ex)
        {
            _notifications.Warning(ex.Message);
        }
        catch (Exception ex)
        {
            _notifications.Error($"Failed to launch the game: {ex.Message}");
        }
    }

    [RelayCommand]
    private void IveTestedIt() => IsAwaitingTestResult = true;

    /// <summary>User reports the problem did NOT happen with (near-)zero mods — mods are confirmed as the cause.</summary>
    [RelayCommand]
    private async Task ReportProblemGoneAsync()
    {
        if (Step == CleanTestWizardStep.Bisection)
        {
            await ReportBisectionRoundGoodAsync();
            return;
        }

        Step = CleanTestWizardStep.ConfirmedModCause;
        IsAwaitingTestResult = false;
        await PersistWizardStateAsync();
    }

    /// <summary>User reports the problem still happened — very likely not a mod issue at all.</summary>
    [RelayCommand]
    private async Task ReportProblemPersistsAsync()
    {
        if (Step == CleanTestWizardStep.Bisection)
        {
            await ReportBisectionRoundBadAsync();
            return;
        }

        Step = CleanTestWizardStep.NotModIssue;
        IsAwaitingTestResult = false;
        await PersistWizardStateAsync();
    }

    [RelayCommand]
    private void GoToLogAnalyzer() => _navigation.NavigateTo(AppView.LogAnalyzer);

    [RelayCommand]
    private async Task StartBisectionAsync()
    {
        if (CurrentSession is null)
        {
            return;
        }

        _bisectionUniverse = BuildAllModFileInfos();
        _universeConfirmedGood = new List<ModFileInfo>();
        _permanentlyConfirmedGood = new List<ModFileInfo>();
        _suspects = new List<ModFileInfo>();
        RoundNumber = 0;

        var firstSuggestion = CleanTestModeService.SuggestNextBisectionSet(_bisectionUniverse, _universeConfirmedGood);
        if (firstSuggestion.Count == 0)
        {
            _notifications.Info("No mods were moved out, so there is nothing to bisect.");
            return;
        }

        await ApplyBisectionRoundAsync(firstSuggestion);
    }

    private async Task ReportBisectionRoundGoodAsync()
    {
        _universeConfirmedGood.AddRange(_lastSuggestion);
        var next = CleanTestModeService.SuggestNextBisectionSet(_bisectionUniverse, _universeConfirmedGood);
        if (next.Count == 0)
        {
            // Every mod in the current universe tested good with no reproduction — no suspect isolated.
            _suspects = new List<ModFileInfo>();
            Step = CleanTestWizardStep.BisectionComplete;
            IsAwaitingTestResult = false;
            UpdateRoundProgressText();
            await PersistWizardStateAsync();
            return;
        }

        await ApplyBisectionRoundAsync(next);
    }

    private async Task ReportBisectionRoundBadAsync()
    {
        // The suspect is within the mods just added back — narrow the universe to that set.
        if (_lastSuggestion.Count == 1)
        {
            _suspects = new List<ModFileInfo>(_lastSuggestion);
            Step = CleanTestWizardStep.BisectionComplete;
            IsAwaitingTestResult = false;
            UpdateRoundProgressText();
            await PersistWizardStateAsync();
            return;
        }

        _permanentlyConfirmedGood.AddRange(_universeConfirmedGood);
        _bisectionUniverse = new List<ModFileInfo>(_lastSuggestion);
        _universeConfirmedGood = new List<ModFileInfo>();

        var next = CleanTestModeService.SuggestNextBisectionSet(_bisectionUniverse, _universeConfirmedGood);
        await ApplyBisectionRoundAsync(next);
    }

    private async Task ApplyBisectionRoundAsync(IReadOnlyList<ModFileInfo> suggestion)
    {
        if (CurrentSession is null || _appState.ModsFolderPath is not { Length: > 0 } modsFolder)
        {
            return;
        }

        IsBusy = true;
        try
        {
            // Restore everything first, then re-enter keeping only the mods proven good so far plus
            // this round's newly-suggested set — never leaves the mods folder in a half-known state.
            await _cleanTestMode.ExitCleanTestModeAsync(CurrentSession);

            var keepNames = _permanentlyConfirmedGood.Concat(_universeConfirmedGood).Concat(suggestion)
                .Select(m => Path.GetFileName(m.ZipPath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var session = await _cleanTestMode.EnterCleanTestModeAsync(
                modsFolder, new CleanTestOptions(RemoveAllMods: false, ModsToKeep: keepNames, ClearShaderCache: false));

            CurrentSession = session;
            _lastSuggestion = suggestion;
            RoundNumber++;
            Step = CleanTestWizardStep.Bisection;
            IsAwaitingTestResult = false;
            UpdateRoundProgressText();
            await PersistWizardStateAsync();
        }
        catch (CleanTestRestoreVerificationException ex)
        {
            ShowRestoreVerificationFailure(ex);
        }
        catch (GameAlreadyRunningException ex)
        {
            _notifications.Warning(ex.Message);
        }
        catch (Exception ex)
        {
            _notifications.Error($"Failed to start the next bisection round: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateRoundProgressText()
    {
        var remaining = _bisectionUniverse.Count - _universeConfirmedGood.Count;
        UntestedRemainingCount = Math.Max(0, remaining);
        EstimatedTotalRounds = _bisectionUniverse.Count <= 1
            ? RoundNumber
            : (int)Math.Ceiling(Math.Log2(Math.Max(2, _bisectionUniverse.Count)));

        RoundProgressText = Step == CleanTestWizardStep.BisectionComplete
            ? (_suspects.Count > 0
                ? $"Isolated {_suspects.Count} suspect mod{(_suspects.Count > 1 ? "s" : "")} after {RoundNumber} round{(RoundNumber == 1 ? "" : "s")}."
                : $"No suspect could be isolated after {RoundNumber} round{(RoundNumber == 1 ? "" : "s")} — every mod tested clean.")
            : $"Round {RoundNumber} of ~{Math.Max(1, EstimatedTotalRounds)} — {UntestedRemainingCount} mod{(UntestedRemainingCount == 1 ? "" : "s")} remaining to test.";
    }

    /// <summary>Escape hatch: exit clean test mode and restore everything, from any step.</summary>
    [RelayCommand]
    private async Task RestoreNowAsync()
    {
        if (CurrentSession is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _cleanTestMode.ExitCleanTestModeAsync(CurrentSession);
            CurrentSession = null;
            ResetWizardState();
            _notifications.Info("Clean test mode ended — all mods restored to the mods folder.");
        }
        catch (CleanTestRestoreVerificationException ex)
        {
            ShowRestoreVerificationFailure(ex);
        }
        catch (GameAlreadyRunningException ex)
        {
            _notifications.Warning(ex.Message);
        }
        catch (Exception ex)
        {
            _notifications.Error($"Failed to restore mods: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowRestoreVerificationFailure(CleanTestRestoreVerificationException ex)
    {
        var lines = new List<string>();
        lines.AddRange(ex.MissingFiles.Select(f => $"Missing: {f}"));
        lines.AddRange(ex.LeftoverInHolding.Select(f => $"Still in holding folder: {f}"));
        AppDialog.ShowInfo(
            "Restore verification failed",
            "The mods folder is now in an inconsistent state and needs your attention. " +
            "Nothing was silently accepted — check the holding folder before doing anything else:\n" +
            ex.Session.HoldingFolderPath,
            lines);
        _notifications.Error("Clean test restore verification failed — see the dialog for details.");
    }

    private void ResetWizardState()
    {
        Step = CleanTestWizardStep.Intro;
        IsAwaitingTestResult = false;
        RoundNumber = 0;
        EstimatedTotalRounds = 0;
        UntestedRemainingCount = 0;
        RoundProgressText = string.Empty;
        _bisectionUniverse.Clear();
        _universeConfirmedGood.Clear();
        _permanentlyConfirmedGood.Clear();
        _lastSuggestion = new List<ModFileInfo>();
        _suspects.Clear();
        SuspectNames.Clear();
    }

    private async Task PersistWizardStateAsync()
    {
        if (CurrentSession is null)
        {
            return;
        }

        var progress = new CleanTestWizardProgress(
            Step,
            RoundNumber,
            _bisectionUniverse.Select(m => Path.GetFileName(m.ZipPath)).ToList(),
            _universeConfirmedGood.Select(m => Path.GetFileName(m.ZipPath)).ToList(),
            _permanentlyConfirmedGood.Select(m => Path.GetFileName(m.ZipPath)).ToList(),
            _suspects.Select(m => Path.GetFileName(m.ZipPath)).ToList());

        var json = JsonSerializer.Serialize(progress);
        CurrentSession = await _cleanTestMode.SaveWizardStateAsync(CurrentSession, json);
    }

    partial void OnCurrentSessionChanged(CleanTestSession? value)
    {
        OnPropertyChanged(nameof(HasActiveSession));
        OnPropertyChanged(nameof(ShowExitEscapeHatch));
    }

    partial void OnStepChanged(CleanTestWizardStep value) => UpdateRoundProgressText();
}
