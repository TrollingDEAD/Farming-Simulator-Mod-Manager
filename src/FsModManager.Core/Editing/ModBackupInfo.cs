namespace FsModManager.Core.Editing;

/// <summary>
/// Metadata describing a timestamped backup copy of a mod zip file.
/// </summary>
/// <param name="FilePath">Full path to the backup zip file on disk.</param>
/// <param name="InternalModName">The internal name of the mod this backup belongs to.</param>
/// <param name="CreatedUtc">UTC timestamp when the backup was created.</param>
/// <param name="Reason">Reason tag for the backup (e.g. "auto-fix", "manual").</param>
/// <param name="SizeBytes">Size of the backup file in bytes.</param>
/// <param name="OriginalModZipPath">Optional path to the live mod zip file if known/resolvable.</param>
public sealed record ModBackupInfo(
    string FilePath,
    string InternalModName,
    DateTime CreatedUtc,
    string Reason,
    long SizeBytes,
    string? OriginalModZipPath = null);
