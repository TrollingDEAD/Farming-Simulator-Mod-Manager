namespace FsModManager.Core.Backups;

/// <summary>
/// Metadata for one savegame snapshot zip. <see cref="CreatedUtc"/> and <see cref="Reason"/> are
/// parsed from the snapshot file name (the name is the human-readable index of the backups folder).
/// </summary>
public sealed record SnapshotInfo(string FilePath, DateTime CreatedUtc, string Reason, long SizeBytes);
