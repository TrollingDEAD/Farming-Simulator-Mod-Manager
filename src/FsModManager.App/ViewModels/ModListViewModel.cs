using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FsModManager.App.Services;
using FsModManager.Core.Detection;
using FsModManager.Core.Models;
using FsModManager.Core.ModScanning;
using FsModManager.Core.ModScanning.Conflicts;
using FsModManager.Core.Savegames;

namespace FsModManager.App.ViewModels;

/// <summary>One toggle-able category filter chip, driven by the distinct categories seen in the last scan.</summary>
public sealed partial class CategoryChipViewModel : ObservableObject
{
    private readonly Action _onSelectionChanged;

    public CategoryChipViewModel(string name, int count, Action onSelectionChanged)
    {
        Name = name;
        Count = count;
        _onSelectionChanged = onSelectionChanged;
    }

    public string Name { get; }

    /// <summary>How many mods from the last scan declared this category.</summary>
    public int Count { get; }

    /// <summary>Label used in the "More categories" checklist, e.g. "tractorsMedium (14)".</summary>
    public string DisplayLabel => $"{Name} ({Count})";

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();
}

/// <summary>
/// Backs the mod-list view: scans the resolved mods folder via <see cref="IModDirectoryScanner"/>
/// and annotates each mod with conflicts from the <see cref="ConflictDetectionPipeline"/>.
/// </summary>
public sealed partial class ModListViewModel : ObservableObject
{
    private readonly IModDirectoryScanner _scanner;
    private readonly IModContentScanner _contentScanner;
    private readonly ConflictDetectionPipeline _conflictPipeline;
    private readonly ModsFolderResolver _modsFolderResolver;
    private readonly SavegameDiscovery _savegameDiscovery;
    private readonly ModLoadOrderReader _loadOrderReader;
    private readonly AppState _appState;
    private readonly INotificationService _notifications;
    private readonly ModRowActions _rowActions;

    public ModListViewModel(
        IModDirectoryScanner scanner,
        IModContentScanner contentScanner,
        ConflictDetectionPipeline conflictPipeline,
        ModsFolderResolver modsFolderResolver,
        SavegameDiscovery savegameDiscovery,
        ModLoadOrderReader loadOrderReader,
        AppState appState,
        INotificationService notifications)
    {
        _scanner = scanner;
        _contentScanner = contentScanner;
        _conflictPipeline = conflictPipeline;
        _modsFolderResolver = modsFolderResolver;
        _savegameDiscovery = savegameDiscovery;
        _loadOrderReader = loadOrderReader;
        _appState = appState;
        _notifications = notifications;

        _rowActions = new ModRowActions
        {
            DiscoverSavegames = () => _savegameDiscovery.FindSavegames(),
            ReadLoadOrder = folder => _loadOrderReader.ReadLoadOrder(folder),
            ScanContent = mod => _contentScanner.ScanContentAsync(mod),
            RequestRescan = () => _ = RescanAsync(),
            NotifyInfo = _notifications.Info,
            NotifyWarning = _notifications.Warning,
            NotifyError = _notifications.Error,
        };

        ModsView = CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter = FilterMod;

        MoreCategoryChipsView = CollectionViewSource.GetDefaultView(MoreCategoryChips);
        MoreCategoryChipsView.Filter = FilterCategoryChipByPopupSearch;
    }

    private const int QuickCategoryChipCount = 8;

    public ObservableCollection<ModRowViewModel> Mods { get; } = new();

    /// <summary>Filtered view over <see cref="Mods"/> — search text and category chips both feed the same Filter.</summary>
    public ICollectionView ModsView { get; }

    /// <summary>The full set of category chips found in the last scan (used by the shared Filter predicate).</summary>
    public ObservableCollection<CategoryChipViewModel> CategoryChips { get; } = new();

    /// <summary>The highest-frequency categories, shown as a single non-wrapping quick-access row.</summary>
    public ObservableCollection<CategoryChipViewModel> QuickCategoryChips { get; } = new();

    /// <summary>Every category not already shown in <see cref="QuickCategoryChips"/>, for the "More" popup checklist.</summary>
    public ObservableCollection<CategoryChipViewModel> MoreCategoryChips { get; } = new();

    /// <summary>Filtered view over <see cref="MoreCategoryChips"/>, driven by <see cref="CategoryPopupSearchText"/>.</summary>
    public ICollectionView MoreCategoryChipsView { get; }

    /// <summary>Selected categories that live in <see cref="MoreCategoryChips"/> — shown as compact removable tags.</summary>
    public ObservableCollection<CategoryChipViewModel> OverflowSelectedChips { get; } = new();

    public bool HasCategoryChips => CategoryChips.Count > 0;

    public bool HasMoreCategoryChips => MoreCategoryChips.Count > 0;

    public bool HasOverflowSelectedChips => OverflowSelectedChips.Count > 0;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowList))]
    private string? _emptyStateMessage;

    [ObservableProperty]
    private string _modsFolderText = string.Empty;

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => ModsView.Refresh();

    [ObservableProperty]
    private string _categoryPopupSearchText = string.Empty;

    partial void OnCategoryPopupSearchTextChanged(string value) => MoreCategoryChipsView.Refresh();

    [ObservableProperty]
    private bool _isMoreCategoriesOpen;

    /// <summary>Every conflict found across the whole library in the last scan (feeds the Conflicts overview tab).</summary>
    [ObservableProperty]
    private IReadOnlyList<ModConflict> _allConflicts = Array.Empty<ModConflict>();

    /// <summary>
    /// Set once the user has viewed the Conflicts tab this session. The "N potential conflict(s)
    /// detected" toast is only useful as a first-look attention-grabber - once the user has seen the
    /// more detailed in-page summary there, a fresh rescan shouldn't repeat the same information as a
    /// duplicate banner.
    /// </summary>
    public bool HasViewedConflictsTab { get; set; }

    /// <summary>Bound to the mod ListBox's SelectedItem so the Conflicts tab can scroll a row into view.</summary>
    [ObservableProperty]
    private ModRowViewModel? _selectedMod;

    /// <summary>
    /// Navigates from the Conflicts overview to the main mod list: clears any active search/category
    /// filter (so the target row(s) can't be hidden by it), expands every matching row, and selects
    /// the first one found so the view can scroll it into view.
    /// </summary>
    public void FocusAndExpandMods(params string[] internalNames)
    {
        SearchText = string.Empty;
        ClearCategoryFilterCommand.Execute(null);

        ModRowViewModel? primary = null;
        foreach (var internalName in internalNames)
        {
            var row = Mods.FirstOrDefault(m => string.Equals(m.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                continue;
            }

            primary ??= row;
            if (!row.IsExpanded)
            {
                row.ToggleExpandCommand.Execute(null);
            }
        }

        if (primary is not null)
        {
            ModsView.Refresh();
            SelectedMod = primary;
        }
    }

    /// <summary>True when there is an empty-state/error message to show instead of the list.</summary>
    public bool HasEmptyState => EmptyStateMessage is not null;

    public bool ShowList => !HasEmptyState;

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void ClearCategoryFilter()
    {
        foreach (var chip in CategoryChips)
        {
            chip.IsSelected = false;
        }
    }

    [RelayCommand]
    private void RemoveCategorySelection(CategoryChipViewModel chip) => chip.IsSelected = false;

    /// <summary>
    /// Combined predicate for <see cref="ModsView"/>: a row must match the search text (substring,
    /// case-insensitive, against title/internal name/author) AND, if any category chips are
    /// selected, have at least one of the selected categories (OR logic between chips).
    /// </summary>
    private bool FilterMod(object item)
    {
        if (item is not ModRowViewModel row)
        {
            return false;
        }

        var selectedCategories = CategoryChips.Where(c => c.IsSelected).Select(c => c.Name).ToList();
        if (selectedCategories.Count > 0 && !row.Categories.Any(c => selectedCategories.Contains(c, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return row.DisplayTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || row.InternalName.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || row.Author.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Text filter for the "More categories" popup checklist — independent of the main mod search box.</summary>
    private bool FilterCategoryChipByPopupSearch(object item)
    {
        if (item is not CategoryChipViewModel chip)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(CategoryPopupSearchText) || chip.Name.Contains(CategoryPopupSearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void OnCategorySelectionChanged()
    {
        ModsView.Refresh();
        RefreshOverflowSelectedChips();
    }

    private void RefreshOverflowSelectedChips()
    {
        OverflowSelectedChips.Clear();
        foreach (var chip in MoreCategoryChips.Where(c => c.IsSelected))
        {
            OverflowSelectedChips.Add(chip);
        }

        OnPropertyChanged(nameof(HasOverflowSelectedChips));
    }

    private void RebuildCategoryChips(IReadOnlyList<ModMetadata> mods)
    {
        var previouslySelected = CategoryChips.Where(c => c.IsSelected).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        CategoryChips.Clear();
        QuickCategoryChips.Clear();
        MoreCategoryChips.Clear();

        var countsByCategory = mods
            .SelectMany(m => m.Categories.Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Category: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var quick = countsByCategory.Take(QuickCategoryChipCount).ToList();
        var more = countsByCategory.Skip(QuickCategoryChipCount).OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var (category, count) in quick)
        {
            var chip = new CategoryChipViewModel(category, count, OnCategorySelectionChanged) { IsSelected = previouslySelected.Contains(category) };
            CategoryChips.Add(chip);
            QuickCategoryChips.Add(chip);
        }

        foreach (var (category, count) in more)
        {
            var chip = new CategoryChipViewModel(category, count, OnCategorySelectionChanged) { IsSelected = previouslySelected.Contains(category) };
            CategoryChips.Add(chip);
            MoreCategoryChips.Add(chip);
        }

        RefreshOverflowSelectedChips();

        OnPropertyChanged(nameof(HasCategoryChips));
        OnPropertyChanged(nameof(HasMoreCategoryChips));
    }

    /// <summary>Runs the first scan when the user lands on this view.</summary>
    public async Task OnNavigatedToAsync()
    {
        if (!HasScanned && !IsBusy)
        {
            await RescanAsync();
        }
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        EmptyStateMessage = null;

        try
        {
            if (_appState.SelectedInstallation is null)
            {
                Mods.Clear();
                CategoryChips.Clear();
                QuickCategoryChips.Clear();
                MoreCategoryChips.Clear();
                OverflowSelectedChips.Clear();
                AllConflicts = Array.Empty<ModConflict>();
                ModsFolderText = string.Empty;
                EmptyStateMessage = "No game installation selected yet. Pick one on the Install Selection screen first.";
                return;
            }

            var modsFolder = _modsFolderResolver.ResolveModsFolder();
            _appState.ModsFolderPath = modsFolder;
            ModsFolderText = modsFolder;

            _contentScanner.ClearCache();

            var mods = await _scanner.ScanAsync(modsFolder);
            var conflicts = _conflictPipeline.Detect(mods);
            AllConflicts = conflicts;

            var conflictsByMod = conflicts
                .SelectMany(c => new[] { (c.ModA, Conflict: c), (c.ModB, Conflict: c) })
                .GroupBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<ModConflict>)g.Select(x => x.Conflict).Distinct().ToList(), StringComparer.OrdinalIgnoreCase);

            Mods.Clear();
            foreach (var mod in mods.OrderBy(m => m.InternalName, StringComparer.OrdinalIgnoreCase))
            {
                conflictsByMod.TryGetValue(mod.InternalName, out var modConflicts);
                Mods.Add(new ModRowViewModel(mod, modConflicts ?? [], _rowActions));
            }

            RebuildCategoryChips(mods);
            ModsView.Refresh();

            ReportParseWarnings(mods);

            if (Mods.Count == 0)
            {
                EmptyStateMessage = $"No mods found in '{modsFolder}'. Drop some .zip mods there and hit Rescan.";
            }
            else if (conflicts.Count > 0 && !HasViewedConflictsTab)
            {
                _notifications.Warning($"{conflicts.Count} potential conflict(s) detected across {Mods.Count} mods.");
            }
        }
        catch (Exception ex)
        {
            _notifications.Error($"Mod scan failed: {ex.Message}");
            EmptyStateMessage = "Scan failed — see the notification above for details.";
        }
        finally
        {
            IsBusy = false;
            HasScanned = true;
        }
    }

    private void ReportParseWarnings(IReadOnlyList<ModMetadata> mods)
    {
        var warnings = mods
            .Where(m => m.Warnings.Count > 0)
            .SelectMany(m => m.Warnings.Select(w => $"{m.InternalName}: {w}"))
            .ToList();

        if (warnings.Count == 0)
        {
            return;
        }

        const int shown = 3;
        var summary = string.Join(Environment.NewLine, warnings.Take(shown));
        if (warnings.Count > shown)
        {
            summary += $"{Environment.NewLine}…and {warnings.Count - shown} more.";
        }

        _notifications.Warning($"{warnings.Count} mod parse warning(s):{Environment.NewLine}{summary}");
    }
}
