using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FsModManager.App.Services;
using FsModManager.App.Views;
using FsModManager.Core.Diagnostics;
using FsModManager.Core.Editing;
using FsModManager.Core.ModScanning;
using FsModManager.Core.ModScanning.Conflicts;
using FsModManager.Core.Models;
using FsModManager.Core.Savegames;

namespace FsModManager.App.ViewModels;

/// <summary>Row ViewModel for a mod with an invalid zip filename.</summary>
public sealed partial class InvalidFilenameRowViewModel : ObservableObject
{
    private readonly Func<InvalidFilenameRowViewModel, Task> _renameAction;

    public InvalidFilenameRowViewModel(
        ModMetadata metadata,
        string originalFileName,
        string reason,
        string suggestedFileName,
        Func<InvalidFilenameRowViewModel, Task> renameAction)
    {
        Metadata = metadata;
        OriginalFileName = originalFileName;
        Reason = reason;
        SuggestedFileName = suggestedFileName;
        _renameAction = renameAction;
    }

    public ModMetadata Metadata { get; }

    public string OriginalFileName { get; }

    public string DisplayTitle => Metadata.DisplayTitle;

    public string InternalName => Metadata.InternalName;

    public string Reason { get; }

    public string SuggestedFileName { get; }

    public string? SourceFilePath => Metadata.SourceFileName;

    [RelayCommand]
    private async Task RenameAsync() => await _renameAction(this);
}

/// <summary>Row ViewModel for a mod flagged as potentially outdated by descVersion.</summary>
public sealed class OutdatedModRowViewModel : ObservableObject
{
    public OutdatedModRowViewModel(OutdatedModAssessment assessment)
    {
        Assessment = assessment;
    }

    public OutdatedModAssessment Assessment { get; }

    public string InternalName => Assessment.InternalName;

    public string DisplayTitle => Assessment.DisplayTitle;

    public int ModDescVersion => Assessment.ModDescVersion;

    public int MaxLibraryDescVersion => Assessment.MaxLibraryDescVersion;

    public int VersionDifference => Assessment.VersionDifference;

    public string Message => Assessment.Message;

    public string FileName => Path.GetFileName(Assessment.SourceFileName ?? Assessment.InternalName);
}

/// <summary>
/// Row ViewModel for one physical copy in a duplicate mod group's resolution card: its own
/// reasons (win or lose), whether it's the recommended keeper, "Remove" (reuses the existing
/// delete-mod confirmation flow) and an on-demand "Compare content" expansion.
/// </summary>
public sealed partial class DuplicateCandidateRowViewModel : ObservableObject
{
    private readonly IModContentScanner _contentScanner;
    private readonly Func<DuplicateCandidateRowViewModel, Task> _removeAction;
    private bool _hasScannedContent;

    public DuplicateCandidateRowViewModel(
        DuplicateModCopy copy,
        ModFileInfo fileInfo,
        IReadOnlyList<string> reasons,
        bool isRecommended,
        IModContentScanner contentScanner,
        Func<DuplicateCandidateRowViewModel, Task> removeAction)
    {
        Copy = copy;
        FileInfo = fileInfo;
        Reasons = reasons;
        IsRecommended = isRecommended;
        _contentScanner = contentScanner;
        _removeAction = removeAction;
    }

    public DuplicateModCopy Copy { get; }

    public ModFileInfo FileInfo { get; }

    /// <summary>Human-readable reasons this candidate won (or, when signals disagree, its own advantages).</summary>
    public IReadOnlyList<string> Reasons { get; }

    public bool HasReasons => Reasons.Count > 0;

    /// <summary>True only when the group had a single confident recommendation and this is it.</summary>
    public bool IsRecommended { get; }

    public string FileName => Copy.FileName;

    public string VersionDisplay => Copy.VersionDisplay;

    public string FileSizeDisplay => Copy.FileSizeDisplay;

    public string LastModifiedDisplay => Copy.LastModifiedDisplay;

    public string? SourceFilePath => Copy.SourceFilePath;

    public string InternalName => Copy.Metadata.InternalName;

    public string DisplayTitle => Copy.Metadata.DisplayTitle;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isContentLoading;

    /// <summary>Per-storeItem shop details for this copy, populated lazily on first "Compare content" click.</summary>
    public ObservableCollection<StoreItemRowViewModel> ContentItems { get; } = new();

    public bool ShowNoContentMessage => _hasScannedContent && !IsContentLoading && ContentItems.Count == 0;

    [RelayCommand]
    private async Task ToggleCompareContentAsync()
    {
        IsExpanded = !IsExpanded;
        if (!IsExpanded || _hasScannedContent)
        {
            return;
        }

        IsContentLoading = true;
        try
        {
            var details = await _contentScanner.ScanContentAsync(FileInfo);
            ContentItems.Clear();
            foreach (var detail in details)
            {
                ContentItems.Add(new StoreItemRowViewModel(detail, Array.Empty<ModConflict>(), InternalName));
            }
        }
        finally
        {
            _hasScannedContent = true;
            IsContentLoading = false;
            OnPropertyChanged(nameof(ShowNoContentMessage));
        }
    }

    [RelayCommand]
    private async Task RemoveAsync() => await _removeAction(this);
}

/// <summary>
/// Group ViewModel for a detected duplicate/stale mod family, presented as a resolution card:
/// a confident single recommendation, or (when signals disagree) a neutral side-by-side comparison
/// with no default selection pre-highlighted.
/// </summary>
public sealed class DuplicateGroupViewModel : ObservableObject
{
    public DuplicateGroupViewModel(
        string displayTitle,
        string normalizedBaseName,
        bool isConfident,
        string? unclearReason,
        IEnumerable<DuplicateCandidateRowViewModel> candidates)
    {
        DisplayTitle = displayTitle;
        NormalizedBaseName = normalizedBaseName;
        IsConfident = isConfident;
        UnclearReason = unclearReason;
        Candidates = new ObservableCollection<DuplicateCandidateRowViewModel>(candidates);
    }

    public string DisplayTitle { get; }

    public string NormalizedBaseName { get; }

    /// <summary>True when one candidate could be confidently recommended over the rest.</summary>
    public bool IsConfident { get; }

    /// <summary>Explains why no confident pick was made, shown when <see cref="IsConfident"/> is false.</summary>
    public string? UnclearReason { get; }

    public ObservableCollection<DuplicateCandidateRowViewModel> Candidates { get; }

    public int CopyCount => Candidates.Count;
}

/// <summary>Row ViewModel for a mod backup taken by ModFileEditor.</summary>
public sealed partial class ModBackupRowViewModel : ObservableObject
{
    private readonly Func<ModBackupRowViewModel, Task> _revertAction;

    public ModBackupRowViewModel(
        ModBackupInfo backup,
        Func<ModBackupRowViewModel, Task> revertAction)
    {
        Backup = backup;
        _revertAction = revertAction;
    }

    public ModBackupInfo Backup { get; }

    public string InternalModName => Backup.InternalModName;

    public string FileName => Path.GetFileName(Backup.FilePath);

    public string FilePath => Backup.FilePath;

    public string Reason => Backup.Reason;

    public string CreatedText => Backup.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string SizeText => FormatSize(Backup.SizeBytes);

    public string? OriginalModZipPath => Backup.OriginalModZipPath;

    [RelayCommand]
    private async Task RevertAsync() => await _revertAction(this);

    internal static string FormatSize(long bytes)
    {
        const long kb = 1024;
        const long mb = 1024 * kb;
        const long gb = 1024 * mb;
        return bytes switch
        {
            >= gb => $"{bytes / (double)gb:0.0} GB",
            >= mb => $"{bytes / (double)mb:0.0} MB",
            >= kb => $"{bytes / (double)kb:0.0} KB",
            _ => $"{bytes} B",
        };
    }
}

/// <summary>
/// Backs the Diagnostics / Health Check view: presents environment checks (OneDrive sync)
/// and mod hygiene checks (invalid filenames, descVersion compatibility heuristics, duplicate mod versions)
/// in a single unified view.
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly ModListViewModel _modList;
    private readonly IGameProcessChecker _gameProcessChecker;
    private readonly SavegameDiscovery _savegameDiscovery;
    private readonly ModLoadOrderReader _loadOrderReader;
    private readonly AppState _appState;
    private readonly INotificationService _notifications;
    private readonly IModContentScanner _contentScanner;
    private readonly IModFileEditor _modFileEditor;

    public DiagnosticsViewModel(
        ModListViewModel modList,
        IGameProcessChecker gameProcessChecker,
        SavegameDiscovery savegameDiscovery,
        ModLoadOrderReader loadOrderReader,
        AppState appState,
        INotificationService notifications,
        IModContentScanner contentScanner,
        IModFileEditor modFileEditor)
    {
        _modList = modList;
        _gameProcessChecker = gameProcessChecker;
        _savegameDiscovery = savegameDiscovery;
        _loadOrderReader = loadOrderReader;
        _appState = appState;
        _notifications = notifications;
        _contentScanner = contentScanner;
        _modFileEditor = modFileEditor;
    }

    public ObservableCollection<InvalidFilenameRowViewModel> InvalidFilenames { get; } = new();

    public ObservableCollection<OutdatedModRowViewModel> OutdatedMods { get; } = new();

    public ObservableCollection<DuplicateGroupViewModel> DuplicateGroups { get; } = new();

    public ObservableCollection<ModBackupRowViewModel> ModBackups { get; } = new();

    public bool HasModBackups => ModBackups.Count > 0;

    public bool HasNoModBackups => !HasModBackups;

    [ObservableProperty]
    private string _modBackupsSummaryText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOneDriveIssue))]
    [NotifyPropertyChangedFor(nameof(IsOneDriveMissing))]
    [NotifyPropertyChangedFor(nameof(IsOneDriveSynced))]
    [NotifyPropertyChangedFor(nameof(IsOneDriveClean))]
    [NotifyPropertyChangedFor(nameof(OneDriveMessage))]
    [NotifyPropertyChangedFor(nameof(OneDriveSuggestedFix))]
    [NotifyPropertyChangedFor(nameof(TotalIssuesCount))]
    [NotifyPropertyChangedFor(nameof(IsHealthy))]
    [NotifyPropertyChangedFor(nameof(HasIssues))]
    private OneDriveCheckResult? _oneDriveResult;

    public bool HasOneDriveIssue => OneDriveResult?.Status != null && OneDriveResult.Status != OneDriveSyncStatus.NotOneDrive;

    public bool IsOneDriveMissing => OneDriveResult?.Status == OneDriveSyncStatus.OneDriveAndMissing;

    public bool IsOneDriveSynced => OneDriveResult?.Status == OneDriveSyncStatus.OneDriveButSynced;

    public bool IsOneDriveClean => OneDriveResult?.Status == OneDriveSyncStatus.NotOneDrive;

    public string OneDriveMessage => OneDriveResult?.Message ?? string.Empty;

    public string OneDriveSuggestedFix => IsOneDriveMissing
        ? "Open OneDrive settings to ensure the Documents folder is synced locally, or exclude your FarmingSimulator2025 folder from cloud sync."
        : "If you experience missing saves or sync conflicts, configure OneDrive to 'Always keep on this device' for your Documents folder or exclude FarmingSimulator2025.";

    [ObservableProperty]
    private string _healthSummaryText = string.Empty;

    [ObservableProperty]
    private int _highestDescVersion;

    public int TotalIssuesCount =>
        (HasOneDriveIssue ? 1 : 0)
        + InvalidFilenames.Count
        + OutdatedMods.Count
        + DuplicateGroups.Count;

    public bool IsHealthy => TotalIssuesCount == 0;

    public bool HasIssues => !IsHealthy;

    public bool HasInvalidFilenames => InvalidFilenames.Count > 0;

    public bool HasOutdatedMods => OutdatedMods.Count > 0;

    public bool HasDuplicates => DuplicateGroups.Count > 0;

    /// <summary>Refreshes diagnostics on navigation.</summary>
    public async Task OnNavigatedToAsync()
    {
        await _modList.OnNavigatedToAsync();
        Rebuild();
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        await _modList.RescanCommand.ExecuteAsync(null);
        Rebuild();
    }

    public void Rebuild()
    {
        OneDriveResult = EnvironmentDiagnostics.CheckDocumentsFolderSync();
        var metadataList = _modList.Mods.Select(m => m.Metadata).ToList();

        RebuildInvalidFilenames(metadataList);
        RebuildOutdatedMods(metadataList);
        RebuildDuplicateGroups(metadataList);
        RebuildModBackups();
        UpdateSummary();
        NotifyRebuildCompleted();
    }

    private void RebuildInvalidFilenames(IReadOnlyList<ModMetadata> metadataList)
    {
        InvalidFilenames.Clear();
        foreach (var mod in metadataList)
        {
            var fileName = Path.GetFileName(mod.SourceFileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            var validation = ModFilenameValidator.Validate(fileName);
            if (!validation.IsValid)
            {
                InvalidFilenames.Add(new InvalidFilenameRowViewModel(
                    mod,
                    fileName,
                    validation.Reason ?? "Contains invalid characters.",
                    validation.SuggestedFilename ?? "FS25_Mod.zip",
                    RenameModFileAsync));
            }
        }
    }

    private void RebuildOutdatedMods(IReadOnlyList<ModMetadata> metadataList)
    {
        OutdatedMods.Clear();
        HighestDescVersion = GameVersionCompatibility.GetHighestDescVersion(metadataList) ?? 0;
        var outdatedAssessments = GameVersionCompatibility.FindPotentiallyOutdatedMods(
            metadataList,
            thresholdDifference: GameVersionCompatibility.DefaultThresholdDifference,
            detectedGameVersion: _appState.SelectedInstallation?.Version);

        foreach (var assessment in outdatedAssessments)
        {
            OutdatedMods.Add(new OutdatedModRowViewModel(assessment));
        }
    }

    private void RebuildDuplicateGroups(IReadOnlyList<ModMetadata> metadataList)
    {
        DuplicateGroups.Clear();
        var duplicateGroups = DuplicateModVersionDetector.DetectDuplicates(metadataList);
        foreach (var group in duplicateGroups)
        {
            var fileInfoByCopy = group.Copies.ToDictionary(c => c, BuildModFileInfo);

            var cachedContent = new Dictionary<string, IReadOnlyList<StoreItemDetail>>();
            foreach (var zipPath in fileInfoByCopy.Values.Select(fileInfo => fileInfo.ZipPath))
            {
                if (_contentScanner.TryGetCachedContent(zipPath, out var cached) && cached is not null)
                {
                    cachedContent[zipPath] = cached;
                }
            }

            var recommendation = DuplicateModResolutionAnalyzer.Analyze(fileInfoByCopy.Values.ToList(), cachedContent);

            var candidates = group.Copies.Select(copy =>
            {
                var fileInfo = fileInfoByCopy[copy];
                var assessment = recommendation.CandidateAssessments.FirstOrDefault(a => a.Candidate.Equals(fileInfo));
                var isRecommended = recommendation.IsConfident && recommendation.RecommendedKeeper is not null &&
                                     recommendation.RecommendedKeeper.Equals(fileInfo);
                return new DuplicateCandidateRowViewModel(
                    copy,
                    fileInfo,
                    assessment?.Reasons ?? Array.Empty<string>(),
                    isRecommended,
                    _contentScanner,
                    DeleteModCopyAsync);
            });

            DuplicateGroups.Add(new DuplicateGroupViewModel(
                group.DisplayTitle,
                group.NormalizedBaseName,
                recommendation.IsConfident,
                recommendation.UnclearReason,
                candidates));
        }
    }

    private void RebuildModBackups()
    {
        ModBackups.Clear();
        var backups = _modFileEditor.ListBackups(modsDirectory: _appState.ModsFolderPath);
        foreach (var backup in backups)
        {
            ModBackups.Add(new ModBackupRowViewModel(backup, RevertModBackupAsync));
        }
        var totalBackupBytes = backups.Sum(b => b.SizeBytes);
        ModBackupsSummaryText = backups.Count == 0
            ? "No mod file backups recorded yet."
            : $"{ModBackupRowViewModel.FormatSize(totalBackupBytes)} used by {backups.Count} backup{(backups.Count == 1 ? "" : "s")}";
        OnPropertyChanged(nameof(HasModBackups));
        OnPropertyChanged(nameof(HasNoModBackups));
    }

    private void NotifyRebuildCompleted()
    {
        OnPropertyChanged(nameof(TotalIssuesCount));
        OnPropertyChanged(nameof(IsHealthy));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(HasInvalidFilenames));
        OnPropertyChanged(nameof(HasOutdatedMods));
        OnPropertyChanged(nameof(HasDuplicates));
    }

    private async Task RevertModBackupAsync(ModBackupRowViewModel row)
    {
        if (_gameProcessChecker.IsGameRunning())
        {
            _notifications.Error("Cannot revert mod files while Farming Simulator 25 is running. Please close the game first.");
            return;
        }

        var targetModPath = row.OriginalModZipPath;
        if ((string.IsNullOrWhiteSpace(targetModPath) || !File.Exists(targetModPath)) &&
            !string.IsNullOrWhiteSpace(_appState.ModsFolderPath))
        {
            targetModPath = Path.Combine(_appState.ModsFolderPath, $"{row.InternalModName}.zip");
        }

        if (string.IsNullOrWhiteSpace(targetModPath))
        {
            _notifications.Error($"Cannot determine live mod location for '{row.InternalModName}'.");
            return;
        }

        var confirmed = AppDialog.Confirm(
            "Revert Mod to Backup",
            $"Revert '{row.InternalModName}' using backup from {row.CreatedText} ({row.Reason})?\n\n" +
            "The current live mod file will be replaced with this backup copy.");

        if (!confirmed)
        {
            return;
        }

        var result = await _modFileEditor.RevertToBackupAsync(targetModPath, row.FilePath);
        if (result.Success)
        {
            _notifications.Info($"Reverted '{row.InternalModName}' to backup from {row.CreatedText}.");
            await _modList.RescanCommand.ExecuteAsync(null);
            Rebuild();
        }
        else
        {
            _notifications.Error($"Failed to revert '{row.InternalModName}': {result.FailureReason}");
        }
    }

    private void UpdateSummary()
    {
        if (IsHealthy)
        {
            HealthSummaryText = "All health checks passed — no environment or mod hygiene issues detected.";
        }
        else
        {
            var parts = new List<string>();
            if (HasOneDriveIssue)
            {
                parts.Add("OneDrive sync warning");
            }
            if (InvalidFilenames.Count > 0)
            {
                parts.Add($"{InvalidFilenames.Count} invalid filename{(InvalidFilenames.Count > 1 ? "s" : "")}");
            }
            if (OutdatedMods.Count > 0)
            {
                parts.Add($"{OutdatedMods.Count} possibly outdated mod{(OutdatedMods.Count > 1 ? "s" : "")}");
            }
            if (DuplicateGroups.Count > 0)
            {
                parts.Add($"{DuplicateGroups.Count} duplicate mod group{(DuplicateGroups.Count > 1 ? "s" : "")}");
            }

            HealthSummaryText = $"{TotalIssuesCount} hygiene / environment issue{(TotalIssuesCount > 1 ? "s" : "")} detected ({string.Join(", ", parts)}).";
        }
    }

    private async Task RenameModFileAsync(InvalidFilenameRowViewModel row)
    {
        if (_gameProcessChecker.IsGameRunning())
        {
            _notifications.Error("Cannot rename mod files while Farming Simulator 25 is running. Please close the game first.");
            return;
        }

        var sourcePath = row.SourceFilePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            _notifications.Error($"Cannot rename '{row.OriginalFileName}' — source file not found.");
            return;
        }

        var dir = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(dir))
        {
            _notifications.Error($"Cannot determine directory for '{row.OriginalFileName}'.");
            return;
        }

        var targetPath = Path.Combine(dir, row.SuggestedFileName);
        if (File.Exists(targetPath) && !string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            _notifications.Error($"Cannot rename '{row.OriginalFileName}' — target file '{row.SuggestedFileName}' already exists.");
            return;
        }

        try
        {
            File.Move(sourcePath, targetPath);
            _notifications.Info($"Renamed '{row.OriginalFileName}' to '{row.SuggestedFileName}'.");
            await _modList.RescanCommand.ExecuteAsync(null);
            Rebuild();
        }
        catch (Exception ex)
        {
            _notifications.Error($"Failed to rename mod file: {ex.Message}");
        }
    }

    private async Task DeleteModCopyAsync(DuplicateCandidateRowViewModel copyRow)
    {
        if (_gameProcessChecker.IsGameRunning())
        {
            _notifications.Error("Cannot delete mod files while Farming Simulator 25 is running. Please close the game first.");
            return;
        }

        var sourcePath = copyRow.SourceFilePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            _notifications.Error($"Cannot delete '{copyRow.FileName}' — file not found.");
            return;
        }

        var activeSavegames = FindSavegamesWhereActive(copyRow.InternalName);
        var message = $"Permanently delete '{copyRow.FileName}' ({copyRow.FileSizeDisplay})?";
        if (activeSavegames.Count > 0)
        {
            message += $"{Environment.NewLine}{Environment.NewLine}Warning: this mod is active in savegame(s): " +
                       $"{string.Join(", ", activeSavegames)}. Deleting it will affect those saves.";
        }

        if (!AppDialog.Confirm("Delete mod copy", message, "Delete"))
        {
            return;
        }

        try
        {
            File.Delete(sourcePath);
            _notifications.Info($"Deleted '{copyRow.FileName}'.");
            await _modList.RescanCommand.ExecuteAsync(null);
            Rebuild();
        }
        catch (Exception ex)
        {
            _notifications.Error($"Failed to delete '{copyRow.FileName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the <see cref="ModFileInfo"/> the resolution analyzer and content scanner need,
    /// mirroring <see cref="ModRowViewModel"/>'s own inline construction from <see cref="ModMetadata"/>.
    /// </summary>
    private static ModFileInfo BuildModFileInfo(DuplicateModCopy copy)
    {
        var metadata = copy.Metadata;
        return new ModFileInfo(
            copy.SourceFilePath ?? string.Empty,
            metadata.InternalName,
            metadata.Version,
            metadata.Author,
            metadata.DescVersionParsed,
            metadata.Warnings,
            metadata.DisplayTitle,
            metadata.IconImageData,
            metadata.MultiplayerSupported,
            metadata.Categories,
            metadata.CrossplayStatus,
            metadata.Description,
            metadata.DescVersion,
            metadata.FileSizeBytes,
            metadata.LastModifiedUtc,
            metadata.ModKind);
    }

    private List<string> FindSavegamesWhereActive(string internalName)
    {
        var result = new List<string>();
        try
        {
            foreach (var savegame in _savegameDiscovery.FindSavegames())
            {
                var isActive = _loadOrderReader.ReadLoadOrder(savegame.FolderPath)
                    .Any(entry => entry.Active && string.Equals(entry.InternalModName, internalName, StringComparison.OrdinalIgnoreCase));
                if (isActive)
                {
                    result.Add(savegame.DisplayName);
                }
            }
        }
        catch
        {
            // Best-effort check
        }

        return result;
    }
}
