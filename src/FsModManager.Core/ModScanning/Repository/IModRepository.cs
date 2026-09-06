using FsModManager.Core.Data.Entities;
using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning.Repository;

/// <summary>CRUD access to installed mods and conflict records, plus conflict detection.</summary>
public interface IModRepository
{
    Task<InstalledMod?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InstalledMod>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<InstalledMod> AddAsync(InstalledMod mod, CancellationToken cancellationToken = default);

    Task UpdateAsync(InstalledMod mod, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects duplicate internal mod names and duplicate storeItem ids across all installed mods,
    /// persists the findings as <see cref="ConflictRecord"/> rows, and returns them.
    /// This is the primary, reliable conflict-detection mechanism (dependency resolution is out of scope).
    /// </summary>
    Task<IReadOnlyList<ModConflict>> DetectConflictsAsync(CancellationToken cancellationToken = default);
}
