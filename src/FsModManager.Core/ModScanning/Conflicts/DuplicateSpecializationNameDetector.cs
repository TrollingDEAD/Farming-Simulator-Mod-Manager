using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning.Conflicts;

/// <summary>
/// Flags Critical when two different mods declare a vehicle &lt;specialization&gt; with the same
/// name in modDesc.xml. Unlike <see cref="SharedDataFileOverrideDetector"/>'s silent Critical,
/// this is a loud, hard load failure in FS25 — the game throws a specific, recognizable error
/// rather than silently corrupting behavior.
/// </summary>
public sealed class DuplicateSpecializationNameDetector : IConflictDetector
{
    public IReadOnlyList<ModConflict> Detect(IReadOnlyList<ModMetadata> mods)
    {
        var conflicts = new List<ModConflict>();

        var nameToMods = mods
            .SelectMany(mod => mod.SpecializationNames.Select(name => (name, mod)))
            // Specialization names become Lua global variable names ("spec_<name>"), which are
            // case-sensitive, so an exact (not case-insensitive) match is used here.
            .GroupBy(x => x.name, StringComparer.Ordinal)
            .Where(g => g.Select(x => x.mod.InternalName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

        foreach (var group in nameToMods)
        {
            var distinctMods = group.Select(x => x.mod).DistinctBy(m => m.InternalName, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var (modA, modB) in ConflictDetectionUtil.DistinctPairs(distinctMods))
            {
                conflicts.Add(new ModConflict(
                    modA.InternalName,
                    null,
                    modB.InternalName,
                    null,
                    ConflictSeverity.Critical,
                    $"Both mods declare a specialization named '{group.Key}' — this will cause a load error like " +
                    $"\"The vehicle specialization {group.Key} could not be added because variable " +
                    $"spec_{group.Key} already exists!\""));
            }
        }

        return conflicts;
    }
}
