using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FsModManager.App.Services;
using FsModManager.Core.Models;
using FsModManager.Core.Multiplayer;
using FsModManager.Core.Savegames;
using Microsoft.Win32;

namespace FsModManager.App.ViewModels;

/// <summary>One manifest entry row (missing/extra lists): name, version, size.</summary>
public sealed class ManifestEntryRowViewModel
{
    public ManifestEntryRowViewModel(ModManifestEntry entry)
    {
        Entry = entry;
    }

    public ModManifestEntry Entry { get; }

    public string DisplayTitle => Entry.DisplayTitle;

    public string InternalModName => Entry.InternalModName;

    public string VersionDisplay => FormatVersion(Entry.Version);

    public string FileSizeDisplay => FormatSize(Entry.FileSizeBytes);

    internal static string FormatVersion(string? version)
        => string.IsNullOrWhiteSpace(version) ? "version unknown" : $"v{version}";

    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.##} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.##} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0.#} KB",
        _ => $"{bytes} B",
    };
}

/// <summary>Both sides of a same-name/different-version pair.</summary>
public sealed class ManifestVersionMismatchRowViewModel
{
    private readonly ManifestVersionMismatch _mismatch;

    public ManifestVersionMismatchRowViewModel(ManifestVersionMismatch mismatch)
    {
        _mismatch = mismatch;
    }

    public string DisplayTitle => _mismatch.Mine.DisplayTitle;

    public string InternalModName => _mismatch.Mine.InternalModName;

    public string MyVersionDisplay => ManifestEntryRowViewModel.FormatVersion(_mismatch.Mine.Version);

    public string TheirVersionDisplay => ManifestEntryRowViewModel.FormatVersion(_mismatch.Theirs.Version);
}

/// <summary>Both sides of a same-name/same-version/different-content pair.</summary>
public sealed class ManifestHashMismatchRowViewModel
{
    private readonly ManifestHashMismatch _mismatch;

    public ManifestHashMismatchRowViewModel(ManifestHashMismatch mismatch)
    {
        _mismatch = mismatch;
    }

    public string DisplayTitle => _mismatch.Mine.DisplayTitle;

    public string InternalModName => _mismatch.Mine.InternalModName;

    public string VersionDisplay => ManifestEntryRowViewModel.FormatVersion(_mismatch.Mine.Version);

    public string MyShortHash => Shorten(_mismatch.Mine.ContentHash);

    public string TheirShortHash => Shorten(_mismatch.Theirs.ContentHash);

    private static string Shorten(string hash) => hash.Length <= 8 ? hash : hash[..8] + "…";
}

/// <summary>
/// Backs the Multiplayer Sync view: exports a mod manifest (name + version + SHA-256 content hash
/// per mod, either for a savegame's active mods or the whole folder) and diffs it against a
/// manifest file a friend or server host sent. Reuses <see cref="ModListViewModel"/>'s scan —
/// and through it the scan-time content hashes — instead of re-scanning or re-hashing eagerly.
/// </summary>
public sealed partial class MultiplayerSyncViewModel : ObservableObject
{
    private readonly ModListViewModel _modList;
    private readonly SavegameDiscovery _savegameDiscovery;
    private readonly ManifestGenerator _generator;
    private readonly ManifestSerializer _serializer;
    private readonly ManifestComparer _comparer;
    private readonly AppState _appState;
    private readonly INotificationService _notifications;

    public MultiplayerSyncViewModel(
        ModListViewModel modList,
        SavegameDiscovery savegameDiscovery,
        ManifestGenerator generator,
        ManifestSerializer serializer,
        ManifestComparer comparer,
        AppState appState,
        INotificationService notifications)
    {
        _modList = modList;
        _savegameDiscovery = savegameDiscovery;
        _generator = generator;
        _serializer = serializer;
        _comparer = comparer;
        _appState = appState;
        _notifications = notifications;
    }

    public ObservableCollection<SavegameInfo> Savegames { get; } = new();

    public ObservableCollection<ManifestEntryRowViewModel> MissingOnMine { get; } = new();

    public ObservableCollection<ManifestEntryRowViewModel> ExtraOnMine { get; } = new();

    public ObservableCollection<ManifestVersionMismatchRowViewModel> VersionMismatches { get; } = new();

    public ObservableCollection<ManifestHashMismatchRowViewModel> HashMismatches { get; } = new();

    [ObservableProperty]
    private SavegameInfo? _selectedSavegame;

    /// <summary>When true the manifest covers every mod in the folder, ignoring savegame selection.</summary>
    [ObservableProperty]
    private bool _includeAllMods;

    [ObservableProperty]
    private string _manifestLabel = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowComparePrompt))]
    [NotifyPropertyChangedFor(nameof(ShowAllMatchState))]
    [NotifyPropertyChangedFor(nameof(ShowResults))]
    private bool _hasCompared;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAllMatchState))]
    [NotifyPropertyChangedFor(nameof(ShowResults))]
    private bool _lastComparisonIsMatch;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _comparedAgainstText = string.Empty;

    [ObservableProperty]
    private string _matchingCountText = string.Empty;

    /// <summary>No comparison has run yet — show the explainer instead of a blank results area.</summary>
    public bool ShowComparePrompt => !HasCompared;

    public bool ShowAllMatchState => HasCompared && LastComparisonIsMatch;

    public bool ShowResults => HasCompared && !LastComparisonIsMatch;

    public bool HasSavegames => Savegames.Count > 0;

    public bool HasMissingOnMine => MissingOnMine.Count > 0;

    public bool HasExtraOnMine => ExtraOnMine.Count > 0;

    public bool HasVersionMismatches => VersionMismatches.Count > 0;

    public bool HasHashMismatches => HashMismatches.Count > 0;

    /// <summary>Refreshes the savegame list and piggybacks on the mod list's first scan.</summary>
    public async Task OnNavigatedToAsync()
    {
        RefreshSavegames();
        await _modList.OnNavigatedToAsync();
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

        OnPropertyChanged(nameof(HasSavegames));
    }

    [RelayCommand]
    private async Task ExportManifestAsync()
    {
        if (!await TryPrepareAsync(requireSavegame: true))
        {
            return;
        }

        var label = string.IsNullOrWhiteSpace(ManifestLabel) ? null : ManifestLabel.Trim();

        IsBusy = true;
        try
        {
            var manifest = await BuildMyManifestAsync(label);

            // A savegame-scoped manifest is empty whenever mods.xml is missing or has nothing
            // active — saving that silently would make the user believe they exported their
            // mod list when they exported nothing at all.
            if (manifest.Entries.Count == 0)
            {
                _notifications.Warning(IncludeAllMods
                    ? "The manifest would be empty — no mods were found in the mods folder."
                    : $"The manifest would be empty — no mods are marked active in '{SelectedSavegame?.DisplayName}' " +
                      "(or it has no mods.xml yet). Save the game once with mods enabled, or use 'All mods in the mods folder'.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export mod manifest",
                Filter = "Mod manifest (*.json)|*.json",
                FileName = SuggestFileName(label),
                DefaultExt = ".json",
                AddExtension = true,
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            await _serializer.SaveAsync(manifest, dialog.FileName);
            _notifications.Info(
                $"Manifest with {manifest.Entries.Count} mod(s) saved to '{Path.GetFileName(dialog.FileName)}'. " +
                "Send it to your friend or server host so they can compare against it.");
        }
        catch (Exception ex)
        {
            _notifications.Error($"Manifest export failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        if (!await TryPrepareAsync(requireSavegame: true))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose a manifest to compare against",
            Filter = "Mod manifest (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            ModManifest theirs;
            try
            {
                theirs = await _serializer.LoadAsync(dialog.FileName);
            }
            catch (InvalidDataException ex)
            {
                _notifications.Error(ex.Message);
                return;
            }
            catch (IOException ex)
            {
                _notifications.Error($"Could not read the manifest file: {ex.Message}");
                return;
            }

            var mine = await BuildMyManifestAsync(label: null);
            if (mine.Entries.Count == 0)
            {
                _notifications.Warning(
                    "Your side of the comparison is empty — no mods are active in the selected savegame " +
                    "(or it has no mods.xml yet). Comparing an empty list would report everything as missing.");
                return;
            }

            ShowComparison(_comparer.Compare(mine, theirs), theirs, dialog.FileName);
        }
        catch (Exception ex)
        {
            _notifications.Error($"Comparison failed: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Copies a plain-text summary of the diff — handy for pasting into Discord.</summary>
    [RelayCommand]
    private void CopyResults()
    {
        if (!HasCompared)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"FS25 mod list comparison — {DateTime.Now:g}");
        if (!string.IsNullOrWhiteSpace(ComparedAgainstText))
        {
            sb.AppendLine(ComparedAgainstText);
        }

        AppendSection(sb, "MISSING ON MY SIDE", MissingOnMine.Select(r =>
            $"  - {r.DisplayTitle} ({r.InternalModName}) {r.VersionDisplay}"));
        AppendSection(sb, "EXTRA ON MY SIDE", ExtraOnMine.Select(r =>
            $"  - {r.DisplayTitle} ({r.InternalModName}) {r.VersionDisplay}"));
        AppendSection(sb, "VERSION MISMATCHES", VersionMismatches.Select(r =>
            $"  - {r.DisplayTitle} ({r.InternalModName}): mine {r.MyVersionDisplay}, theirs {r.TheirVersionDisplay}"));
        AppendSection(sb, "SAME VERSION, DIFFERENT CONTENT", HashMismatches.Select(r =>
            $"  - {r.DisplayTitle} ({r.InternalModName}) {r.VersionDisplay}: mine {r.MyShortHash}, theirs {r.TheirShortHash}"));

        sb.AppendLine(MatchingCountText);

        Clipboard.SetText(sb.ToString());
        _notifications.Info("Comparison copied to the clipboard.");
    }

    private static void AppendSection(StringBuilder sb, string title, IEnumerable<string> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine($"{title} ({list.Count}):");
        foreach (var line in list)
        {
            sb.AppendLine(line);
        }
    }

    /// <summary>
    /// Shared guard for export/compare: an installation must be selected, a scan must have found
    /// mods, and the savegame scope needs a savegame picked.
    /// </summary>
    private async Task<bool> TryPrepareAsync(bool requireSavegame)
    {
        if (IsBusy)
        {
            return false;
        }

        if (_appState.SelectedInstallation is null)
        {
            _notifications.Warning("No game installation selected yet — pick one on the Installation screen first.");
            return false;
        }

        await _modList.OnNavigatedToAsync();
        if (_modList.Mods.Count == 0)
        {
            _notifications.Warning("No mods found. Rescan the mod list (Mods tab) first.");
            return false;
        }

        if (requireSavegame && !IncludeAllMods && SelectedSavegame is null)
        {
            _notifications.Warning(HasSavegames
                ? "Pick a savegame for the manifest to reflect, or tick 'All mods in the mods folder'."
                : "No savegames found — tick 'All mods in the mods folder' to export everything instead.");
            return false;
        }

        return true;
    }

    private async Task<ModManifest> BuildMyManifestAsync(string? label)
    {
        var mods = _modList.Mods.Select(r => r.Metadata).ToList();
        return IncludeAllMods || SelectedSavegame is null
            ? await _generator.GenerateAsync(mods, label)
            : await _generator.GenerateActiveAsync(mods, SelectedSavegame.FolderPath, label);
    }

    private void ShowComparison(ManifestComparisonResult result, ModManifest theirs, string theirFilePath)
    {
        MissingOnMine.Clear();
        foreach (var entry in result.MissingOnMine)
        {
            MissingOnMine.Add(new ManifestEntryRowViewModel(entry));
        }

        ExtraOnMine.Clear();
        foreach (var entry in result.MissingOnTheirs)
        {
            ExtraOnMine.Add(new ManifestEntryRowViewModel(entry));
        }

        VersionMismatches.Clear();
        foreach (var mismatch in result.VersionMismatches)
        {
            VersionMismatches.Add(new ManifestVersionMismatchRowViewModel(mismatch));
        }

        HashMismatches.Clear();
        foreach (var mismatch in result.HashMismatches)
        {
            HashMismatches.Add(new ManifestHashMismatchRowViewModel(mismatch));
        }

        var theirName = !string.IsNullOrWhiteSpace(theirs.Label)
            ? $"\"{theirs.Label}\""
            : $"'{Path.GetFileName(theirFilePath)}'";
        ComparedAgainstText =
            $"Compared against {theirName} — generated {theirs.GeneratedUtc.ToLocalTime():g}, {theirs.Entries.Count} mod(s).";

        SummaryText = result.IsMatch
            ? $"All {result.Matching.Count} mod(s) match exactly — same names, same versions, same content. You're good to play together."
            : $"{result.MissingOnMine.Count} missing on your side · {result.MissingOnTheirs.Count} extra on your side · " +
              $"{result.VersionMismatches.Count} version mismatch(es) · {result.HashMismatches.Count} same-version content mismatch(es).";

        MatchingCountText = $"…and {result.Matching.Count} mod(s) match exactly.";

        LastComparisonIsMatch = result.IsMatch;
        HasCompared = true;

        // Count-dependent flags don't auto-notify from Clear()/Add() alone.
        OnPropertyChanged(nameof(HasMissingOnMine));
        OnPropertyChanged(nameof(HasExtraOnMine));
        OnPropertyChanged(nameof(HasVersionMismatches));
        OnPropertyChanged(nameof(HasHashMismatches));
    }

    private static string SuggestFileName(string? label)
    {
        var baseName = string.IsNullOrWhiteSpace(label) ? "MyMods" : label.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(invalid, '_');
        }

        return $"{baseName}_manifest.json";
    }
}
