using FsModManager.Core.Models;

namespace FsModManager.Core.Data.Entities;

/// <summary>Persisted record of a detected conflict between two installed mods.</summary>
public sealed class ConflictRecord
{
    public int Id { get; set; }

    public int ModAId { get; set; }

    public InstalledMod? ModA { get; set; }

    public int ModBId { get; set; }

    public InstalledMod? ModB { get; set; }

    public ConflictSeverity Severity { get; set; }

    public required string Description { get; set; }

    public DateTimeOffset DetectedDate { get; set; } = DateTimeOffset.UtcNow;
}
