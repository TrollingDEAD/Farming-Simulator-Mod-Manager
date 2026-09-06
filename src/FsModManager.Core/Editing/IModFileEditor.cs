using System.Xml.Linq;

namespace FsModManager.Core.Editing;

/// <summary>
/// Safe mod-file editing infrastructure pipeline.
/// Guarantees that any modification to a mod zip file is guarded against a running game,
/// backed up to a dedicated folder before touching anything, validated on a temp copy
/// against parse errors and regressions, and atomically committed to the live file.
/// </summary>
public interface IModFileEditor
{
    /// <summary>
    /// Applies an atomic edit to a mod zip file after creating a backup and validating the result.
    /// </summary>
    Task<EditResult> ApplyEditAsync(
        string modZipPath,
        ModFileEdit edit,
        string reason = "auto-fix",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a batch of edits to the same mod zip file as ONE atomic unit: a single backup, a
    /// single validation pass (re-parse + content-scan) run once after ALL edits in the batch have
    /// been applied to the same temp copy, and a single atomic replace of the live file. If
    /// validation fails, every edit in the batch is rolled back together — none of them land.
    /// Multi-step changes (e.g. rename an entry by writing it under a new path then deleting the old
    /// one, plus updating a cross-reference elsewhere in the same zip) MUST use this instead of
    /// several separate <see cref="ApplyEditAsync"/> calls, which would each trigger their own
    /// backup/validation cycle and could leave the change half-applied if a later call failed.
    /// </summary>
    Task<EditResult> ApplyEditBatchAsync(
        string modZipPath,
        IReadOnlyList<ModFileEdit> edits,
        string reason = "auto-fix",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverts a live mod zip file to a previously created backup copy using atomic replacement.
    /// </summary>
    Task<EditResult> RevertToBackupAsync(
        string modZipPath,
        string backupPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an XML entry from inside the zip, executes the transformation function on its <see cref="XDocument"/>,
    /// and safely applies the result via <see cref="ApplyEditAsync"/>.
    /// </summary>
    Task<EditResult> ApplyXmlTransformAsync(
        string modZipPath,
        string entryPathInsideZip,
        Func<XDocument, XDocument> transform,
        string reason = "auto-fix",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all backups for a specific mod or across all mods, ordered newest first.
    /// </summary>
    IReadOnlyList<ModBackupInfo> ListBackups(string? internalModName = null, string? modsDirectory = null);

    /// <summary>
    /// Returns the folder path where backups for the given mod zip are stored.
    /// </summary>
    string GetBackupFolderPath(string modZipPath, string? internalModName = null);
}
