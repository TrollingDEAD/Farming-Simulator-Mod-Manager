namespace FsModManager.Core.Editing;

/// <summary>
/// The result of an attempted mod file edit or restore operation.
/// </summary>
/// <param name="Success">True if the edit/restore succeeded and was atomically committed; false otherwise.</param>
/// <param name="FailureReason">Human-readable description of why the operation failed, or null on success.</param>
/// <param name="BackupPath">Path to the backup zip file created before the edit attempt (or the restored backup path).</param>
/// <param name="WarningsBefore">Mod parse/content warnings before the edit was attempted.</param>
/// <param name="WarningsAfter">Mod parse/content warnings after the edit was performed on the temp copy.</param>
public sealed record EditResult(
    bool Success,
    string? FailureReason,
    string BackupPath,
    IReadOnlyList<string> WarningsBefore,
    IReadOnlyList<string> WarningsAfter);
