using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning.Conflicts;

/// <summary>
/// Flags Likely when two mods declare the same custom fillType/fruitType/vehicleType name.
/// This is "Likely" rather than "Critical" because FS's behavior on collision varies by type and
/// load order, but it's still a strong sign of an incompatibility worth surfacing to the user.
/// </summary>
public sealed class DuplicateCustomTypeDetector : IConflictDetector
{
    public IReadOnlyList<ModConflict> Detect(IReadOnlyList<ModMetadata> mods)
    {
        var conflicts = new List<ModConflict>();

        var typeToMods = mods
            .SelectMany(mod => mod.CustomTypes.Select(customType => (customType, mod)))
            .GroupBy(x => (x.customType.Kind, Name: x.customType.Name), new CustomTypeKeyComparer())
            .Where(g => g.Select(x => x.mod.InternalName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

        foreach (var group in typeToMods)
        {
            var distinctMods = group.Select(x => x.mod).DistinctBy(m => m.InternalName, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var (modA, modB) in ConflictDetectionUtil.DistinctPairs(distinctMods))
            {
                conflicts.Add(new ModConflict(
                    modA.InternalName,
                    null,
                    modB.InternalName,
                    null,
                    ConflictSeverity.Likely,
                    $"Both mods declare a {group.Key.Kind} named \"{group.Key.Name}\"."));
            }
        }

        return conflicts;
    }

    private sealed class CustomTypeKeyComparer : IEqualityComparer<(ModCustomTypeKind Kind, string Name)>
    {
        public bool Equals((ModCustomTypeKind Kind, string Name) x, (ModCustomTypeKind Kind, string Name) y) =>
            x.Kind == y.Kind && string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((ModCustomTypeKind Kind, string Name) obj) =>
            HashCode.Combine(obj.Kind, obj.Name.ToUpperInvariant());
    }
}
