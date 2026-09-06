using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning.Conflicts;

/// <summary>
/// Flags Critical when two different mods each ship their own copy of a shared base-game data
/// file (e.g. fillTypes.xml, densityHeights.xml — see <see cref="SharedDataFileDefinitions"/>).
/// FS25 applies only the last-loaded mod's copy, silently discarding the other's changes with no
/// error message — distinct from <see cref="DuplicateSpecializationNameDetector"/>'s Critical,
/// which is a loud crash instead.
/// </summary>
public sealed class SharedDataFileOverrideDetector : IConflictDetector
{
    private readonly ILoadOrderPriorityCalculator _loadOrderCalculator;

    public SharedDataFileOverrideDetector(ILoadOrderPriorityCalculator? loadOrderCalculator = null)
    {
        _loadOrderCalculator = loadOrderCalculator ?? new LoadOrderPriorityCalculator();
    }

    public IReadOnlyList<ModConflict> Detect(IReadOnlyList<ModMetadata> mods)
    {
        var conflicts = new List<ModConflict>();

        var fileNameToMods = mods
            .SelectMany(mod => mod.SharedDataFileMatches.Select(fileName => (fileName, mod)))
            .GroupBy(x => x.fileName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.mod.InternalName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

        foreach (var group in fileNameToMods)
        {
            var distinctMods = group.Select(x => x.mod).DistinctBy(m => m.InternalName, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var (modA, modB) in ConflictDetectionUtil.DistinctPairs(distinctMods))
            {
                var zipA = modA.SourceFileName ?? modA.InternalName;
                var zipB = modB.SourceFileName ?? modB.InternalName;
                var laterZip = _loadOrderCalculator.GetLaterLoadingMod(zipA, zipB);
                var earlierZip = string.Equals(laterZip, zipA, StringComparison.OrdinalIgnoreCase) ? zipB : zipA;

                var laterName = Path.GetFileName(laterZip);
                var earlierName = Path.GetFileName(earlierZip);

                conflicts.Add(new ModConflict(
                    modA.InternalName,
                    null,
                    modB.InternalName,
                    null,
                    ConflictSeverity.Critical,
                    $"Both mods ship their own \"{group.Key}\" — FS25 loads mods alphabetically and silently " +
                    $"applies only the last-loaded copy, with no error message. \"{laterName}\" loads after " +
                    $"\"{earlierName}\" and will silently override its \"{group.Key}\" changes right now.",
                    SharedDataFileName: group.Key));
            }
        }

        return conflicts;
    }
}
