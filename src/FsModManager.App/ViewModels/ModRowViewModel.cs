using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FsModManager.App.Services;
using FsModManager.App.Views;
using FsModManager.Core.ModScanning.Conflicts;
using FsModManager.Core.Models;

namespace FsModManager.App.ViewModels;

/// <summary>
/// One row in the mod list: parsed metadata plus the worst conflict this mod is involved in,
/// any parse warnings, and the context-menu actions (show in folder, delete, etc.) for that mod.
/// </summary>
public sealed partial class ModRowViewModel : ObservableObject
{
    private readonly ModRowActions _actions;

    public ModRowViewModel(ModMetadata metadata, IReadOnlyList<ModConflict> conflicts, ModRowActions actions)
    {
        Metadata = metadata;
        Conflicts = conflicts;
        _actions = actions;

        WorstSeverity = conflicts.Count == 0 ? null : conflicts.Min(c => (ConflictSeverity?)c.Severity);
        HasConflicts = conflicts.Count > 0;
        ConflictSummary = string.Join(Environment.NewLine, conflicts.Select(BuildConflictLine));

        HasWarnings = metadata.Warnings.Count > 0;
        WarningSummary = string.Join(Environment.NewLine, metadata.Warnings);

        Icon = ImageDecoding.TryDecodeToBitmapSource(metadata.IconImageData);
    }

    public ModMetadata Metadata { get; }

    public IReadOnlyList<ModConflict> Conflicts { get; }

    public string InternalName => Metadata.InternalName;

    /// <summary>The primary display name — a parsed &lt;title&gt; when available, else the internal name.</summary>
    public string DisplayTitle => Metadata.DisplayTitle;

    /// <summary>Kept as its own independent field — never appended to <see cref="DisplayTitle"/> or <see cref="InternalName"/>.</summary>
    public string Version => string.IsNullOrWhiteSpace(Metadata.Version) ? "—" : Metadata.Version!;

    public string Author => string.IsNullOrWhiteSpace(Metadata.Author) ? "Unknown author" : Metadata.Author!;

    public string? SourceFileName => Metadata.SourceFileName;

    /// <summary>Distinct storeItem category strings for this mod (may be empty), verbatim as declared.</summary>
    public IReadOnlyList<string> Categories => Metadata.Categories;

    public bool? MultiplayerSupported => Metadata.MultiplayerSupported;

    /// <summary>Plain-language tooltip for the multiplayer indicator icon, covering all three states.</summary>
    public string MultiplayerTooltip => MultiplayerSupported switch
    {
        true => "This mod declares multiplayer support.",
        false => "This mod declares that it does NOT support multiplayer.",
        null => "This mod's modDesc.xml doesn't specify multiplayer support either way.",
    };

    public CrossplayStatus CrossplayStatus => Metadata.CrossplayStatus;

    /// <summary>
    /// Plain-language tooltip for the crossplay indicator icon. Currently always the "Unknown"
    /// message — see the remarks on <see cref="ModMetadata.CrossplayStatus"/> for why.
    /// </summary>
    public string CrossplayTooltip => CrossplayStatus switch
    {
        CrossplayStatus.Supported => "This mod is compatible with crossplay.",
        CrossplayStatus.NotSupported => "This mod is NOT compatible with crossplay.",
        _ => "Crossplay compatibility could not be determined for this mod.",
    };

    /// <summary>Full parsed &lt;description&gt; text, or null when there's none.</summary>
    public string? Description => Metadata.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    private const int DescriptionPreviewLength = 220;

    public bool IsDescriptionTruncated => HasDescription && Description!.Length > DescriptionPreviewLength;

    [ObservableProperty]
    private bool _isDescriptionExpanded;

    /// <summary>The text to actually show: full text once expanded (or if it's short enough), else a truncated preview.</summary>
    public string DescriptionDisplay =>
        !HasDescription
            ? string.Empty
            : IsDescriptionExpanded || !IsDescriptionTruncated
                ? Description!
                : Description![..DescriptionPreviewLength].TrimEnd() + "…";

    [RelayCommand]
    private void ToggleDescriptionExpanded() => IsDescriptionExpanded = !IsDescriptionExpanded;

    /// <summary>Human-readable file size (e.g. "142 MB"), or "—" when unknown (size 0, e.g. older cached rows).</summary>
    public string FileSizeDisplay => Metadata.FileSizeBytes <= 0 ? "—" : FormatFileSize(Metadata.FileSizeBytes);

    public string LastModifiedDisplay => Metadata.LastModifiedUtc == default
        ? "—"
        : Metadata.LastModifiedUtc.ToLocalTime().ToString("d");

    public string DescVersionDisplay => string.IsNullOrWhiteSpace(Metadata.DescVersion) ? "—" : Metadata.DescVersion!;

    /// <summary>
    /// Best-effort mod content classification. Before this mod's content has been scanned, this can
    /// only ever reflect the confirmed &lt;maps&gt; signal (Map) or Unknown; once <see cref="ContentItems"/>
    /// is populated, it's refined into VehiclePack/PlaceablePack/Mixed/ScriptOnly.
    /// </summary>
    public ModKind ModKind => _hasScannedContent
        ? ModKindClassifier.Classify(Metadata.ModKind == ModKind.Map, ContentItems.Select(c => c.Detail.Kind).ToList())
        : Metadata.ModKind;

    public string ModKindDisplay => ModKind switch
    {
        ModKind.Map => "Map",
        ModKind.VehiclePack => "Vehicle Pack",
        ModKind.PlaceablePack => "Placeable Pack",
        ModKind.ScriptOnly => "Script Only",
        ModKind.Mixed => "Mixed",
        _ => "Unknown",
    };

    /// <summary>Diagnostics section label (e.g. "⚠ 2 parsing warnings") — only meaningful when <see cref="HasWarnings"/>.</summary>
    public string DiagnosticsLabel => $"⚠ {Metadata.Warnings.Count} parsing warning{(Metadata.Warnings.Count == 1 ? "" : "s")}";

    public IReadOnlyList<string> Warnings => Metadata.Warnings;

    [ObservableProperty]
    private bool _isDiagnosticsExpanded;

    [RelayCommand]
    private void ToggleDiagnosticsExpanded() => IsDiagnosticsExpanded = !IsDiagnosticsExpanded;

    private static string FormatFileSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }

    /// <summary>Decoded mod icon, or null when there's none / it failed to decode — UI shows a placeholder.</summary>
    public BitmapSource? Icon { get; }

    /// <summary>The most severe conflict involving this mod, or null when conflict-free.</summary>
    public ConflictSeverity? WorstSeverity { get; }

    public bool HasConflicts { get; }

    /// <summary>All conflict descriptions for this mod, one per line (tooltip/expand text).</summary>
    public string ConflictSummary { get; }

    public bool HasWarnings { get; }

    /// <summary>Parse warnings for this mod, one per line (tooltip text).</summary>
    public string WarningSummary { get; }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isContentLoading;

    private bool _hasScannedContent;

    /// <summary>Per-storeItem shop details for this mod, populated lazily on first expansion.</summary>
    public ObservableCollection<StoreItemRowViewModel> ContentItems { get; } = new();

    /// <summary>True once a scan has completed and found zero storeItems (script/map-only mod).</summary>
    public bool ShowNoContentMessage => _hasScannedContent && !IsContentLoading && ContentItems.Count == 0;

    [RelayCommand]
    private async Task ToggleExpandAsync()
    {
        IsExpanded = !IsExpanded;
        if (!IsExpanded || _hasScannedContent)
        {
            return;
        }

        IsContentLoading = true;
        try
        {
            var fileInfo = new ModFileInfo(
                SourceFileName ?? string.Empty,
                InternalName,
                Metadata.Version,
                Metadata.Author,
                Metadata.DescVersionParsed,
                Metadata.Warnings,
                Metadata.DisplayTitle,
                Metadata.IconImageData,
                Metadata.MultiplayerSupported,
                Metadata.Categories,
                Metadata.CrossplayStatus,
                Metadata.Description,
                Metadata.DescVersion,
                Metadata.FileSizeBytes,
                Metadata.LastModifiedUtc,
                Metadata.ModKind);

            var details = await _actions.ScanContent(fileInfo);

            ContentItems.Clear();
            foreach (var detail in details)
            {
                var objectConflicts = Conflicts.GetConflictsForObject(detail.XmlFilename);
                ContentItems.Add(new StoreItemRowViewModel(detail, objectConflicts, InternalName));
            }
        }
        catch (Exception ex)
        {
            _actions.NotifyError($"Failed to scan '{DisplayTitle}'s content: {ex.Message}");
        }
        finally
        {
            _hasScannedContent = true;
            IsContentLoading = false;
            OnPropertyChanged(nameof(ShowNoContentMessage));
            OnPropertyChanged(nameof(ModKind));
            OnPropertyChanged(nameof(ModKindDisplay));
        }
    }

    /// <summary>
    /// Builds one conflict-summary line, attributing the specific object on each side when both
    /// this mod's and the other mod's side of the conflict have one (e.g. duplicate storeItem).
    /// </summary>
    private string BuildConflictLine(ModConflict c)
    {
        var isModA = string.Equals(c.ModA, InternalName, StringComparison.OrdinalIgnoreCase);
        var otherMod = isModA ? c.ModB : c.ModA;
        var thisObject = isModA ? c.ObjectAXmlFilename : c.ObjectBXmlFilename;
        var otherObject = isModA ? c.ObjectBXmlFilename : c.ObjectAXmlFilename;

        return thisObject is not null && otherObject is not null
            ? $"[{c.Severity}] Conflicts with {otherMod}'s \"{otherObject}\" — {c.Description}"
            : $"[{c.Severity}] {c.Description}";
    }

    [RelayCommand]
    private void ShowInFolder()
    {
        if (string.IsNullOrWhiteSpace(SourceFileName) || !File.Exists(SourceFileName))
        {
            _actions.NotifyError($"Can't show '{DisplayTitle}' — its zip file no longer exists.");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{SourceFileName}\"");
        }
        catch (Exception ex)
        {
            _actions.NotifyError($"Failed to open Explorer: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ShowProblems()
    {
        if (!HasConflicts)
        {
            return;
        }

        var lines = Conflicts.Select(BuildConflictLine).ToList();
        AppDialog.ShowInfo($"Problems with {DisplayTitle}", "This mod is involved in the following conflicts:", lines);
    }

    [RelayCommand]
    private void OpenModDesc()
    {
        if (string.IsNullOrWhiteSpace(SourceFileName) || !File.Exists(SourceFileName))
        {
            _actions.NotifyError($"Can't open modDesc.xml — '{DisplayTitle}'s zip file no longer exists.");
            return;
        }

        try
        {
            using var archive = ZipFile.OpenRead(SourceFileName);
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, "modDesc.xml", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                _actions.NotifyError($"'{DisplayTitle}' has no modDesc.xml in its zip.");
                return;
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"{InternalName}-modDesc-{Guid.NewGuid():N}.xml");
            entry.ExtractToFile(tempPath, overwrite: true);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tempPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _actions.NotifyError($"Failed to open modDesc.xml: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CopyInternalModName()
    {
        try
        {
            Clipboard.SetText(InternalName);
            _actions.NotifyInfo($"Copied '{InternalName}' to the clipboard.");
        }
        catch (Exception ex)
        {
            _actions.NotifyError($"Failed to copy to clipboard: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteMod()
    {
        if (string.IsNullOrWhiteSpace(SourceFileName) || !File.Exists(SourceFileName))
        {
            _actions.NotifyError($"Can't delete '{DisplayTitle}' — its zip file no longer exists.");
            return;
        }

        var activeSavegames = FindSavegamesWhereActive();
        var message = $"Delete '{DisplayTitle}' ({Path.GetFileName(SourceFileName)})? This permanently removes the mod's zip file.";
        if (activeSavegames.Count > 0)
        {
            message += $"{Environment.NewLine}{Environment.NewLine}Warning: this mod is Active in the load order of: " +
                       $"{string.Join(", ", activeSavegames)}. Deleting it will break that save until you remove it from the load order too.";
        }

        if (!AppDialog.Confirm("Delete mod", message, "Delete"))
        {
            return;
        }

        try
        {
            File.Delete(SourceFileName);
            _actions.NotifyInfo($"Deleted '{DisplayTitle}'.");
            _actions.RequestRescan();
        }
        catch (Exception ex)
        {
            _actions.NotifyError($"Failed to delete '{DisplayTitle}': {ex.Message}");
        }
    }

    private List<string> FindSavegamesWhereActive()
    {
        var result = new List<string>();
        try
        {
            foreach (var savegame in _actions.DiscoverSavegames())
            {
                var isActive = _actions.ReadLoadOrder(savegame.FolderPath)
                    .Any(entry => entry.Active && string.Equals(entry.InternalModName, InternalName, StringComparison.OrdinalIgnoreCase));
                if (isActive)
                {
                    result.Add(savegame.DisplayName);
                }
            }
        }
        catch
        {
            // Best-effort warning only — never block delete on this check failing.
        }

        return result;
    }
}

