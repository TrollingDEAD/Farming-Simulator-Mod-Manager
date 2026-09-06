using FsModManager.Core.Data;
using FsModManager.Core.Data.Entities;
using FsModManager.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FsModManager.Core.ModScanning.Repository;

/// <inheritdoc cref="IModRepository"/>
public sealed class ModRepository : IModRepository
{
    private readonly FsModManagerDbContext _dbContext;

    public ModRepository(FsModManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<InstalledMod?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _dbContext.InstalledMods.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<InstalledMod>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.InstalledMods.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<InstalledMod> AddAsync(InstalledMod mod, CancellationToken cancellationToken = default)
    {
        _dbContext.InstalledMods.Add(mod);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return mod;
    }

    public async Task UpdateAsync(InstalledMod mod, CancellationToken cancellationToken = default)
    {
        _dbContext.InstalledMods.Update(mod);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var mod = await _dbContext.InstalledMods.FindAsync([id], cancellationToken);
        if (mod is not null)
        {
            _dbContext.InstalledMods.Remove(mod);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ModConflict>> DetectConflictsAsync(CancellationToken cancellationToken = default)
    {
        var mods = await _dbContext.InstalledMods.AsNoTracking().ToListAsync(cancellationToken);
        var detectedConflicts = new List<DetectedConflict>();

        detectedConflicts.AddRange(FindDuplicateInternalNames(mods));
        detectedConflicts.AddRange(FindDuplicateStoreItemIds(mods));

        // Replace the persisted set of conflicts with the freshly detected set.
        var existing = await _dbContext.ConflictRecords.ToListAsync(cancellationToken);
        _dbContext.ConflictRecords.RemoveRange(existing);

        foreach (var conflict in detectedConflicts)
        {
            _dbContext.ConflictRecords.Add(new ConflictRecord
            {
                ModAId = conflict.ModAId,
                ModBId = conflict.ModBId,
                Severity = conflict.Conflict.Severity,
                Description = conflict.Description,
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return detectedConflicts.Select(conflict => conflict.Conflict).ToList();
    }

    private static IEnumerable<DetectedConflict> FindDuplicateInternalNames(IReadOnlyList<InstalledMod> mods)
    {
        var duplicateGroups = mods
            .GroupBy(m => m.InternalName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            foreach (var (modA, modB) in DistinctPairs(group.ToList()))
            {
                yield return CreateConflict(
                    modA,
                    modB,
                    ConflictSeverity.Critical,
                    $"Both mods share the internal name \"{modA.InternalName}\".");
            }
        }
    }

    private static IEnumerable<DetectedConflict> FindDuplicateStoreItemIds(IReadOnlyList<InstalledMod> mods)
    {
        var storeItemToMods = mods
            .SelectMany(mod => mod.StoreItemIds.Select(storeItemId => (storeItemId, mod)))
            .GroupBy(x => x.storeItemId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.mod.Id).Distinct().Count() > 1);

        foreach (var group in storeItemToMods)
        {
            var distinctMods = group.Select(x => x.mod).DistinctBy(m => m.Id).ToList();
            foreach (var (modA, modB) in DistinctPairs(distinctMods))
            {
                yield return CreateConflict(
                    modA,
                    modB,
                    ConflictSeverity.Likely,
                    $"Both mods declare storeItem \"{group.Key}\".");
            }
        }
    }

    private static IEnumerable<(InstalledMod, InstalledMod)> DistinctPairs(IReadOnlyList<InstalledMod> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                yield return (items[i], items[j]);
            }
        }
    }

    private static DetectedConflict CreateConflict(
        InstalledMod modA,
        InstalledMod modB,
        ConflictSeverity severity,
        string description) =>
        new(
            modA.Id,
            modB.Id,
            new ModConflict(modA.InternalName, null, modB.InternalName, null, severity, description));

    private sealed record DetectedConflict(int ModAId, int ModBId, ModConflict Conflict)
    {
        public string Description => Conflict.Description;
    }
}
