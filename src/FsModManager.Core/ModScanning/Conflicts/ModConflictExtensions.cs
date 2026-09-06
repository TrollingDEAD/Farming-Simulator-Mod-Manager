using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning.Conflicts;

/// <summary>
/// Aggregation helpers over a flat <see cref="ModConflict"/> list — used by the UI to compute a
/// mod-level "worst severity" rollup (still "highest severity for this mod", now over the richer
/// object-level-or-mod-wide data) and to look up conflicts for one specific storeItem.
/// </summary>
public static class ModConflictExtensions
{
    /// <summary>Every conflict (object-level or mod-wide) that involves the given mod.</summary>
    public static IReadOnlyList<ModConflict> GetConflictsForMod(this IReadOnlyList<ModConflict> conflicts, string modInternalName) =>
        conflicts
            .Where(c => string.Equals(c.ModA, modInternalName, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(c.ModB, modInternalName, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>The highest-severity conflict involving the given mod, or null when conflict-free.</summary>
    public static ConflictSeverity? GetAggregateSeverity(this IReadOnlyList<ModConflict> conflicts, string modInternalName)
    {
        var relevant = conflicts.GetConflictsForMod(modInternalName);
        return relevant.Count == 0 ? null : relevant.Min(c => (ConflictSeverity?)c.Severity);
    }

    /// <summary>
    /// Every conflict that attributes either side to the given storeItem xmlFilename. Only
    /// object-level conflicts (populated <see cref="ModConflict.ObjectAXmlFilename"/>/
    /// <see cref="ModConflict.ObjectBXmlFilename"/>) can ever match.
    /// </summary>
    public static IReadOnlyList<ModConflict> GetConflictsForObject(this IReadOnlyList<ModConflict> conflicts, string objectXmlFilename) =>
        conflicts
            .Where(c => string.Equals(c.ObjectAXmlFilename, objectXmlFilename, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(c.ObjectBXmlFilename, objectXmlFilename, StringComparison.OrdinalIgnoreCase))
            .ToList();
}
