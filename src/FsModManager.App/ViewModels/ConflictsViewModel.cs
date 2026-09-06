using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FsModManager.App.Services;
using FsModManager.App.Views;
using FsModManager.Core.Editing;
using FsModManager.Core.Models;
using FsModManager.Core.Resolution;

namespace FsModManager.App.ViewModels;

/// <summary>One conflict entry in the library-wide Conflicts overview, with a "jump to mod" action
/// and, for mechanically-fixable conflict types, a "Fix" action.</summary>
public sealed partial class ConflictOverviewRowViewModel : ObservableObject
{
    private readonly Action<string, string> _goToMods;
    private readonly Func<ConflictOverviewRowViewModel, Task> _fixConflict;

    public ConflictOverviewRowViewModel(
        ModConflict conflict,
        Action<string, string> goToMods,
        Func<ConflictOverviewRowViewModel, Task> fixConflict)
    {
        Conflict = conflict;
        _goToMods = goToMods;
        _fixConflict = fixConflict;

        var classification = ConflictFixAdvisor.Classify(conflict);
        FixKind = classification.Kind;
        NotFixableReason = classification.NotFixableReason;
    }

    public ModConflict Conflict { get; }

    /// <summary>Drives the same severity-based row tinting used for mods/objects in the main mod list.</summary>
    public ConflictSeverity Severity => Conflict.Severity;

    public string ModA => Conflict.ModA;

    public string ModB => Conflict.ModB;

    public string Description => Conflict.Description;

    /// <summary>Which (if any) automatic fix applies — decided structurally, without needing I/O.</summary>
    public ConflictFixKind FixKind { get; }

    /// <summary>
    /// True for conflict types that MAY be auto-fixable (storeItem rename is always fixable once
    /// reached here; a shared-data-file merge candidate still needs its own on-click analysis to
    /// confirm the two mods' additions don't actually overlap — see <see cref="ConflictsViewModel"/>).
    /// </summary>
    public bool CanShowFixButton => FixKind != ConflictFixKind.NotFixable;

    /// <summary>Plain-language explanation shown instead of a Fix button when this conflict has no mechanical fix.</summary>
    public string? NotFixableReason { get; }

    public bool HasNotFixableReason => !string.IsNullOrEmpty(NotFixableReason);

    [ObservableProperty]
    private bool _isFixing;

    [ObservableProperty]
    private bool _isReasonExpanded;

    [RelayCommand]
    private void GoToMods() => _goToMods(ModA, ModB);

    [RelayCommand]
    private void ToggleReasonExpanded() => IsReasonExpanded = !IsReasonExpanded;

    [RelayCommand]
    private async Task Fix()
    {
        if (IsFixing)
        {
            return;
        }

        IsFixing = true;
        try
        {
            await _fixConflict(this);
        }
        finally
        {
            IsFixing = false;
        }
    }
}

/// <summary>
/// Backs the library-wide Conflicts overview tab: every <see cref="ModConflict"/> found in the last
/// mod-list scan, grouped by severity, independent of scrolling through the mod list itself.
/// Reuses <see cref="ModListViewModel"/>'s already-scanned data rather than re-scanning.
/// Also owns applying the two mechanically-fixable conflict types (duplicate storeItem filename
/// rename, non-overlapping shared-data-file merge) via the Core Resolution services.
/// </summary>
public sealed partial class ConflictsViewModel : ObservableObject
{
    private readonly ModListViewModel _modList;
    private readonly NavigationService _navigation;
    private readonly IModFileEditor _modFileEditor;
    private readonly StoreItemRenameResolver _storeItemRenameResolver;
    private readonly SharedDataFileMergeAnalyzer _mergeAnalyzer;
    private readonly MergeableSharedFileFix _mergeFix;
    private readonly INotificationService _notifications;

    public ConflictsViewModel(
        ModListViewModel modList,
        NavigationService navigation,
        IModFileEditor modFileEditor,
        StoreItemRenameResolver storeItemRenameResolver,
        SharedDataFileMergeAnalyzer mergeAnalyzer,
        MergeableSharedFileFix mergeFix,
        INotificationService notifications)
    {
        _modList = modList;
        _navigation = navigation;
        _modFileEditor = modFileEditor;
        _storeItemRenameResolver = storeItemRenameResolver;
        _mergeAnalyzer = mergeAnalyzer;
        _mergeFix = mergeFix;
        _notifications = notifications;
    }

    public ObservableCollection<ConflictOverviewRowViewModel> CriticalConflicts { get; } = new();

    public ObservableCollection<ConflictOverviewRowViewModel> LikelyConflicts { get; } = new();

    public ObservableCollection<ConflictOverviewRowViewModel> PossibleConflicts { get; } = new();

    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>Filters the conflict lists by mod name or keyword — reuses the mod list's search box style/placeholder pattern.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyStateText))]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => Rebuild();

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasConflicts;

    /// <summary>True when the scan found zero conflicts — shown instead of an empty list, so it can't
    /// be mistaken for the feature being broken.</summary>
    public bool ShowEmptyState => !HasConflicts;

    /// <summary>Distinguishes a genuinely clean scan from "your search matched nothing" in the empty state.</summary>
    public string EmptyStateText => string.IsNullOrWhiteSpace(SearchText) || _modList.AllConflicts.Count == 0
        ? "No conflicts detected"
        : "No conflicts match your search";

    public bool HasCriticalConflicts => CriticalConflicts.Count > 0;

    public bool HasLikelyConflicts => LikelyConflicts.Count > 0;

    public bool HasPossibleConflicts => PossibleConflicts.Count > 0;

    /// <summary>Runs (or reuses) the mod scan when the user lands on this tab, then builds the overview.</summary>
    public async Task OnNavigatedToAsync()
    {
        await _modList.OnNavigatedToAsync();
        Rebuild();

        // The in-page summary below is the source of truth once the user has actually seen this tab -
        // suppress the duplicate "N potential conflict(s) detected" toast on future rescans.
        _modList.HasViewedConflictsTab = true;
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        await _modList.RescanCommand.ExecuteAsync(null);
        Rebuild();
    }

    private void Rebuild()
    {
        CriticalConflicts.Clear();
        LikelyConflicts.Clear();
        PossibleConflicts.Clear();

        var matching = _modList.AllConflicts.Where(MatchesSearch).ToList();
        foreach (var conflict in matching)
        {
            var row = new ConflictOverviewRowViewModel(conflict, GoToMods, FixConflictAsync);
            var bucket = conflict.Severity switch
            {
                ConflictSeverity.Critical => CriticalConflicts,
                ConflictSeverity.Likely => LikelyConflicts,
                _ => PossibleConflicts,
            };
            bucket.Add(row);
        }

        HasConflicts = matching.Count > 0;
        if (HasConflicts)
        {
            SummaryText = $"{CriticalConflicts.Count} critical, {LikelyConflicts.Count} likely, {PossibleConflicts.Count} possible " +
                          $"conflict(s) found across {_modList.Mods.Count} mods.";
        }
        else if (string.IsNullOrWhiteSpace(SearchText) || _modList.AllConflicts.Count == 0)
        {
            SummaryText = $"No conflicts detected across {_modList.Mods.Count} mods.";
        }
        else
        {
            SummaryText = "No conflicts match your search.";
        }

        OnPropertyChanged(nameof(HasCriticalConflicts));
        OnPropertyChanged(nameof(HasLikelyConflicts));
        OnPropertyChanged(nameof(HasPossibleConflicts));
        OnPropertyChanged(nameof(EmptyStateText));
    }

    private bool MatchesSearch(ModConflict conflict)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return conflict.ModA.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               conflict.ModB.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               conflict.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void GoToMods(string modA, string modB)
    {
        _navigation.NavigateTo(AppView.ModList);
        _modList.FocusAndExpandMods(modA, modB);
    }

    /// <summary>
    /// Applies the mechanical fix for a row's conflict, if any. After a successful fix, re-runs the
    /// full scan immediately (not left for the user's next manual rescan) so the now-resolved
    /// conflict disappears from the list right away rather than lingering stale.
    /// </summary>
    private async Task FixConflictAsync(ConflictOverviewRowViewModel row)
    {
        var conflict = row.Conflict;
        var modA = _modList.Mods.FirstOrDefault(m => string.Equals(m.InternalName, conflict.ModA, StringComparison.OrdinalIgnoreCase))?.Metadata;
        var modB = _modList.Mods.FirstOrDefault(m => string.Equals(m.InternalName, conflict.ModB, StringComparison.OrdinalIgnoreCase))?.Metadata;

        if (modA is null || modB is null)
        {
            _notifications.Error("Could not locate one or both affected mods — try rescanning first.");
            return;
        }

        switch (row.FixKind)
        {
            case ConflictFixKind.StoreItemRename:
                await FixStoreItemRenameAsync(conflict, modA, modB);
                break;

            case ConflictFixKind.SharedDataFileMergeCandidate:
                await FixSharedDataFileMergeAsync(conflict, modA, modB);
                break;

            default:
                // NotFixable rows never show a Fix button, so this shouldn't be reachable.
                break;
        }
    }

    private async Task FixStoreItemRenameAsync(ModConflict conflict, ModMetadata modA, ModMetadata modB)
    {
        var plan = _storeItemRenameResolver.BuildPlan(conflict, modA, modB);
        if (plan is null)
        {
            _notifications.Error("Could not determine a rename plan for this conflict.");
            return;
        }

        var confirmed = ModEditConfirmationDialog.ShowConfirmation(
            modFileName: $"{plan.ModInternalName} ({Path.GetFileName(plan.ModZipPath)})",
            changeDescription: $"Rename \"{plan.OldEntryPath}\" to \"{plan.NewEntryPath}\" inside {plan.ModInternalName}, and update its " +
                                "modDesc.xml storeItem reference to match. The other mod's copy of this file is untouched.",
            backupLocationDescription: _modFileEditor.GetBackupFolderPath(plan.ModZipPath, plan.ModInternalName));

        if (!confirmed)
        {
            return;
        }

        var result = await _storeItemRenameResolver.ApplyAsync(conflict, modA, modB);
        if (result.Success)
        {
            _notifications.Info($"Fixed: renamed the colliding file inside {plan.ModInternalName}.");
            await RescanCommand.ExecuteAsync(null);
        }
        else
        {
            _notifications.Error($"Fix failed: {result.FailureReason}");
        }
    }

    private async Task FixSharedDataFileMergeAsync(ModConflict conflict, ModMetadata modA, ModMetadata modB)
    {
        if (string.IsNullOrWhiteSpace(conflict.SharedDataFileName) ||
            string.IsNullOrWhiteSpace(modA.SourceFileName) ||
            string.IsNullOrWhiteSpace(modB.SourceFileName))
        {
            _notifications.Error("Could not resolve the mod files for this conflict.");
            return;
        }

        var mergeResult = _mergeAnalyzer.AnalyzeFromZips(
            modA.SourceFileName, modA.InternalName, modB.SourceFileName, modB.InternalName, conflict.SharedDataFileName);

        if (!mergeResult.IsMergeable)
        {
            var details = mergeResult.ConflictingEntries.Count > 0
                ? mergeResult.ConflictingEntries
                    .Select(e => $"\"{e.Name}\" — {modA.InternalName}: {e.DefinitionFromModA} | {modB.InternalName}: {e.DefinitionFromModB}")
                    .ToList()
                : new List<string>();

            AppDialog.ShowInfo("Can't auto-fix this conflict", mergeResult.Reason, details);
            return;
        }

        var confirmed = ModEditConfirmationDialog.ShowConfirmation(
            modFileName: $"{Path.GetFileName(modA.SourceFileName)} + {Path.GetFileName(modB.SourceFileName)}",
            changeDescription: $"Merge both mods' non-overlapping \"{conflict.SharedDataFileName}\" additions into one identical file, " +
                                $"applied to BOTH mods (eliminating the conflict rather than picking a winner). {mergeResult.Reason}",
            backupLocationDescription:
                $"{_modFileEditor.GetBackupFolderPath(modA.SourceFileName, modA.InternalName)}{Environment.NewLine}" +
                $"{_modFileEditor.GetBackupFolderPath(modB.SourceFileName, modB.InternalName)}");

        if (!confirmed)
        {
            return;
        }

        var (resultA, resultB) = await _mergeFix.ApplyAsync(mergeResult, modA.SourceFileName, modB.SourceFileName);
        if (resultA.Success && resultB.Success)
        {
            _notifications.Info($"Fixed: merged \"{conflict.SharedDataFileName}\" for both mods.");
            await RescanCommand.ExecuteAsync(null);
        }
        else
        {
            _notifications.Error($"Fix failed: {resultA.FailureReason ?? resultB.FailureReason}");
        }
    }
}

