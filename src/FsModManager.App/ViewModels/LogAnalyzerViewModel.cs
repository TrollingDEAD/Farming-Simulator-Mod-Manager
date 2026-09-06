using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FsModManager.App.Services;
using FsModManager.App.Views;
using FsModManager.Core.Diagnostics;
using FsModManager.Core.Editing;

namespace FsModManager.App.ViewModels;

/// <summary>One Warning/Error log line after analysis, with an expandable raw-line detail.</summary>
public sealed partial class LogEntryRowViewModel : ObservableObject
{
    public LogEntryRowViewModel(AnalyzedLogEntry entry, Func<LogEntryRowViewModel, Task>? applyFix = null)
    {
        Entry = entry;
        _applyFix = applyFix;
    }

    public AnalyzedLogEntry Entry { get; }

    public string RawLine => Entry.Entry.RawLine;

    public string Explanation => Entry.Explanation;

    public bool IsError => Entry.Entry.Level == LogLevel.Error;

    public string SeverityLabel => IsError ? "Error" : "Warning";

    public bool MatchesKnownConflict => Entry.MatchesKnownConflict;

    public bool CanFix => _applyFix is not null && Entry.Fixability is FixabilityTier.Safe or FixabilityTier.Partial;

    public bool IsInfoOnly => !CanFix;

    public string FixabilityLabel => Entry.Fixability switch
    {
        FixabilityTier.Safe => "Safe fix",
        FixabilityTier.Partial => "Partial fix",
        FixabilityTier.SuggestOnly => "Info only",
        FixabilityTier.NotModFileFixable => "Not fixable here",
        _ => "Not fixable here",
    };

    private readonly Func<LogEntryRowViewModel, Task>? _applyFix;

    [ObservableProperty]
    private bool _isRawLineExpanded;

    [RelayCommand]
    private void ToggleRawLine() => IsRawLineExpanded = !IsRawLineExpanded;

    [RelayCommand]
    private Task ApplyFixAsync() => _applyFix is null ? Task.CompletedTask : _applyFix(this);
}

/// <summary>One mod's group of attributed log entries, with a "jump to mod" action.</summary>
public sealed partial class LogModGroupViewModel : ObservableObject
{
    private readonly Action<string> _goToMod;
    private readonly Func<LogEntryRowViewModel, Task> _applyFix;

    public LogModGroupViewModel(
        ModLogGroup group,
        Action<string> goToMod,
        Func<LogEntryRowViewModel, Task> applyFix)
    {
        _goToMod = goToMod;
        _applyFix = applyFix;
        ModInternalName = group.ModInternalName;
        ModDisplayTitle = group.ModDisplayTitle;
        Entries = new ObservableCollection<LogEntryRowViewModel>(group.Entries.Select(e => new LogEntryRowViewModel(e, _applyFix)));

        // Collapsed by default once a mod has more than a handful of entries, so a session with many
        // warnings across many mods doesn't force scrolling through everything just to see which mods
        // are affected. The collapsed header alone (name + count) still needs to be useful on its own.
        _isExpanded = Entries.Count <= 3;
    }

    public string ModInternalName { get; }

    public string ModDisplayTitle { get; }

    public ObservableCollection<LogEntryRowViewModel> Entries { get; }

    public int ErrorCount => Entries.Count(e => e.IsError);

    public int WarningCount => Entries.Count(e => !e.IsError);

    /// <summary>Scannable even while collapsed, e.g. "3 warnings" or "2 errors, 3 warnings".</summary>
    public string CountLabel
    {
        get
        {
            var parts = new List<string>();
            if (ErrorCount > 0)
            {
                parts.Add(ErrorCount == 1 ? "1 error" : $"{ErrorCount} errors");
            }

            if (WarningCount > 0)
            {
                parts.Add(WarningCount == 1 ? "1 warning" : $"{WarningCount} warnings");
            }

            return string.Join(", ", parts);
        }
    }

    [ObservableProperty]
    private bool _isExpanded;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void GoToMod() => _goToMod(ModInternalName);
}

/// <summary>
/// Backs the Log Analyzer view: on demand (never auto-run at startup), parses FS25's log.txt,
/// attributes each Warning/Error to an installed mod where possible, matches it against the known
/// error pattern list, and groups the results for display.
/// </summary>
public sealed partial class LogAnalyzerViewModel : ObservableObject
{
    private readonly ModListViewModel _modList;
    private readonly NavigationService _navigation;
    private readonly LogAnalyzer _logAnalyzer;
    private readonly IModFileEditor _modFileEditor;
    private readonly L10nFixService _l10nFixService;
    private readonly INotificationService _notifications;

    public LogAnalyzerViewModel(
        ModListViewModel modList,
        NavigationService navigation,
        LogAnalyzer logAnalyzer,
        IModFileEditor modFileEditor,
        L10nFixService l10nFixService,
        INotificationService notifications)
    {
        _modList = modList;
        _navigation = navigation;
        _logAnalyzer = logAnalyzer;
        _modFileEditor = modFileEditor;
        _l10nFixService = l10nFixService;
        _notifications = notifications;

        ModGroupsView = CollectionViewSource.GetDefaultView(ModGroups);
        ModGroupsView.Filter = FilterModGroup;

        UnattributedEntriesView = CollectionViewSource.GetDefaultView(UnattributedEntries);
        UnattributedEntriesView.Filter = FilterEntry;

        NotModRelatedEntriesView = CollectionViewSource.GetDefaultView(NotModRelatedEntries);
        NotModRelatedEntriesView.Filter = FilterEntry;
    }

    private bool FilterModGroup(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var group = (LogModGroupViewModel)obj;
        return group.ModDisplayTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               group.ModInternalName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               group.Entries.Any(e => FilterEntry(e));
    }

    private bool FilterEntry(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var entry = (LogEntryRowViewModel)obj;
        return entry.Explanation.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               entry.RawLine.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    public ObservableCollection<LogModGroupViewModel> ModGroups { get; } = new();

    public ObservableCollection<LogEntryRowViewModel> UnattributedEntries { get; } = new();

    /// <summary>GraphicsUnrelated entries, kept separate from <see cref="UnattributedEntries"/> so the
    /// UI can give them a distinct "not mod-related" treatment instead of mixing them with real gaps.</summary>
    public ObservableCollection<LogEntryRowViewModel> NotModRelatedEntries { get; } = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowResults))]
    [NotifyPropertyChangedFor(nameof(ShowAnalyzePrompt))]
    private bool _hasAnalyzed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowResults))]
    private bool _logFileExists = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowResults))]
    private bool _hasIssues;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>Filters the mod groups/entries below by mod name or keyword — reuses the mod list's
    /// search box style/placeholder pattern.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        ModGroupsView.Refresh();
        UnattributedEntriesView.Refresh();
        NotModRelatedEntriesView.Refresh();
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    /// <summary>True only before the first analysis has run — must NOT overlap the results view once
    /// analysis has happened (a prior ConverterParameter="Invert" on the built-in
    /// BooleanToVisibilityConverter silently did nothing, leaving this prompt's icon and text visibly
    /// stuck on top of the results list; this plain bool avoids that converter pitfall entirely).</summary>
    public bool ShowAnalyzePrompt => !HasAnalyzed;

    /// <summary>True once analysis has run and there's nothing (or no log file) to show - a positive
    /// message, not a blank screen.</summary>
    public bool ShowEmptyState => HasAnalyzed && (!LogFileExists || !HasIssues);

    public bool ShowResults => HasAnalyzed && LogFileExists && HasIssues;

    public bool HasNotModRelatedEntries => NotModRelatedEntries.Count > 0;

    public bool HasUnattributedEntries => UnattributedEntries.Count > 0;

    public ICollectionView ModGroupsView { get; }

    public ICollectionView UnattributedEntriesView { get; }

    public ICollectionView NotModRelatedEntriesView { get; }

    /// <summary>Deliberately does nothing on navigation - reading/parsing a huge log could take a
    /// moment, so analysis only runs via the explicit "Analyze latest log" action.</summary>
    public Task OnNavigatedToAsync() => Task.CompletedTask;

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        IsBusy = true;
        try
        {
            await _modList.OnNavigatedToAsync();
            var mods = _modList.Mods.Select(m => m.Metadata).ToList();
            var conflicts = _modList.AllConflicts;

            var result = await Task.Run(() => _logAnalyzer.Analyze(null, mods, conflicts));
            ApplyResult(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyResult(LogAnalysisResult result)
    {
        ModGroups.Clear();
        UnattributedEntries.Clear();
        NotModRelatedEntries.Clear();

        LogFileExists = result.LogFileExists;

        foreach (var group in result.ModGroups)
        {
            ModGroups.Add(new LogModGroupViewModel(group, GoToMod, FixEntryAsync));
        }

        foreach (var entry in result.UnattributedEntries)
        {
            var row = new LogEntryRowViewModel(entry, FixEntryAsync);
            if (entry.Category == ErrorCategory.GraphicsUnrelated)
            {
                NotModRelatedEntries.Add(row);
            }
            else
            {
                UnattributedEntries.Add(row);
            }
        }

        HasIssues = result.TotalErrorCount + result.TotalWarningCount > 0;
        SummaryText = !result.LogFileExists
            ? "log.txt hasn't been created yet - run the game at least once, then analyze again."
            : !HasIssues
                ? "No warnings or errors found in the latest session's log.txt."
                : $"{result.TotalErrorCount} error(s), {result.TotalWarningCount} warning(s) in the latest session - " +
                  $"{result.AttributedCount} attributed to a mod, {result.UnattributedCount} not attributed.";

        HasAnalyzed = true;
        OnPropertyChanged(nameof(HasNotModRelatedEntries));
        OnPropertyChanged(nameof(HasUnattributedEntries));
    }

    private void GoToMod(string internalName)
    {
        _navigation.NavigateTo(AppView.ModList);
        _modList.FocusAndExpandMods(internalName);
    }

    private async Task FixEntryAsync(LogEntryRowViewModel row)
    {
        var modName = row.Entry.Entry.AttributedModInternalName;
        var key = row.Entry.Captures.TryGetValue("key", out var capturedKey) ? capturedKey : null;
        var mod = _modList.Mods.FirstOrDefault(item => string.Equals(item.InternalName, modName, StringComparison.OrdinalIgnoreCase))?.Metadata;
        if (mod is null || string.IsNullOrWhiteSpace(mod.SourceFileName) || string.IsNullOrWhiteSpace(key))
        {
            _notifications.Error("The affected mod file could not be located. Rescan the mod list and try again.");
            return;
        }

        var isPartial = row.Entry.Fixability == FixabilityTier.Partial;
        var description = isPartial
            ? $"Add a rough placeholder label for l10n key '{key}'. This is not the mod author's intended text - it is good enough to stop the warning, but consider it temporary."
            : $"Remove duplicate l10n entries named '{key}', keeping the first definition in the mod's language files.";
        var confirmed = ModEditConfirmationDialog.ShowConfirmation(
            Path.GetFileName(mod.SourceFileName),
            description,
            _modFileEditor.GetBackupFolderPath(mod.SourceFileName, mod.InternalName),
            confirmButtonText: isPartial ? "Add Placeholder" : "Fix Duplicate");
        if (!confirmed)
        {
            return;
        }

        if (isPartial)
        {
            var result = await _l10nFixService.AddPlaceholderAsync(mod.SourceFileName, key);
            if (result?.Success == true)
            {
                _notifications.Info("Added the temporary placeholder l10n label. Re-run analysis to verify the warning is gone.");
            }
            else
            {
                _notifications.Error($"Fix failed: {result?.FailureReason ?? "No l10n XML file was found."}");
            }
        }
        else
        {
            var results = await _l10nFixService.RemoveDuplicateAsync(mod.SourceFileName, key);
            var failure = results.FirstOrDefault(result => !result.Success);
            if (failure is null && results.Count > 0)
            {
                _notifications.Info("Removed duplicate l10n entries. Re-run analysis to verify the warning is gone.");
            }
            else
            {
                _notifications.Error($"Fix failed: {failure?.FailureReason ?? "No l10n XML file was found."}");
            }
        }
    }

    [RelayCommand]
    private void CopyReport()
    {
        Clipboard.SetText(BuildReportText());
    }

    private string BuildReportText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("FS Mod Manager - Log Analysis Report");
        sb.AppendLine(SummaryText);
        sb.AppendLine();

        foreach (var group in ModGroups)
        {
            sb.AppendLine($"== {group.ModDisplayTitle} ({group.ErrorCount} error(s), {group.WarningCount} warning(s)) ==");
            AppendEntries(sb, group.Entries);
            sb.AppendLine();
        }

        if (NotModRelatedEntries.Count > 0)
        {
            sb.AppendLine("== Not mod-related (likely GPU/driver) ==");
            AppendEntries(sb, NotModRelatedEntries);
            sb.AppendLine();
        }

        if (UnattributedEntries.Count > 0)
        {
            sb.AppendLine("== Unattributed ==");
            AppendEntries(sb, UnattributedEntries);
        }

        return sb.ToString();
    }

    private static void AppendEntries(StringBuilder sb, IEnumerable<LogEntryRowViewModel> entries)
    {
        foreach (var entry in entries)
        {
            sb.AppendLine($"- [{entry.SeverityLabel}] {entry.Explanation}");
            sb.AppendLine($"    Raw: {entry.RawLine}");
        }
    }
}
