using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning.Conflicts;

/// <summary>
/// Flags Critical when two mods share the same InternalName: FS loads mods by that name, so the
/// second one loaded silently overwrites the first — this is the most damaging conflict class.
/// </summary>
public sealed class DuplicateInternalNameDetector : IConflictDetector
{
    public IReadOnlyList<ModConflict> Detect(IReadOnlyList<ModMetadata> mods)
    {
        var conflicts = new List<ModConflict>();

        var duplicateGroups = mods
            .GroupBy(m => m.InternalName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in duplicateGroups)
        {
            foreach (var (modA, modB) in ConflictDetectionUtil.DistinctPairs(group.ToList()))
            {
                conflicts.Add(new ModConflict(
                    modA.InternalName,
                    null,
                    modB.InternalName,
                    null,
                    ConflictSeverity.Critical,
                    $"Both mods share the internal name \"{group.Key}\" — one will silently overwrite the other."));
            }
        }

        return conflicts;
    }
}
