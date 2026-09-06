using FsModManager.Core.Backups;

namespace FsModManager.App.ViewModels;

/// <summary>
/// Presentation wrapper for one savegame snapshot: formatted local timestamp and size, plus the
/// "manual" badge flag. The raw <see cref="SnapshotInfo"/> stays available for restore commands.
/// </summary>
public sealed class SnapshotRowViewModel
{
    public SnapshotRowViewModel(SnapshotInfo snapshot)
    {
        Info = snapshot;
    }

    public SnapshotInfo Info { get; }

    public string FilePath => Info.FilePath;

    public string Reason => Info.Reason;

    /// <summary>Manual snapshots get a visible badge and are exempt from automatic pruning.</summary>
    public bool IsManual =>
        string.Equals(Info.Reason, SavegameSnapshotService.ManualReason, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reason text is only shown for automatic snapshots — manual ones show the badge instead.</summary>
    public bool ShowReasonText => !IsManual;

    public string CreatedText => Info.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string SizeText => FormatSize(Info.SizeBytes);

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
