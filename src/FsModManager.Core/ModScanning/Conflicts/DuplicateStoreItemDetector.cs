using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning.Conflicts;

/// <summary>
/// Flags Critical when two different mods declare a &lt;storeItem&gt; with the same xmlFilename:
/// FS assigns that shop slot to whichever mod loads last, silently hiding the other's item.
/// </summary>
public sealed class DuplicateStoreItemDetector : IConflictDetector
{
    public IReadOnlyList<ModConflict> Detect(IReadOnlyList<ModMetadata> mods)
    {
        var conflicts = new List<ModConflict>();

        var xmlFilenameToMods = mods
            .SelectMany(mod => mod.StoreItems
                .Where(item => !string.IsNullOrWhiteSpace(item.XmlFilename))
                .Select(item => (xmlFilename: item.XmlFilename!, mod)))
            .GroupBy(x => x.xmlFilename, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.mod.InternalName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

        foreach (var group in xmlFilenameToMods)
        {
            var distinctMods = group.Select(x => x.mod).DistinctBy(m => m.InternalName, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var (modA, modB) in ConflictDetectionUtil.DistinctPairs(distinctMods))
            {
                conflicts.Add(new ModConflict(
                    modA.InternalName,
                    group.Key,
                    modB.InternalName,
                    group.Key,
                    ConflictSeverity.Critical,
                    $"Both mods declare storeItem \"{group.Key}\" — the last-loaded mod silently wins that shop slot."));
            }
        }

        return conflicts;
    }
}
