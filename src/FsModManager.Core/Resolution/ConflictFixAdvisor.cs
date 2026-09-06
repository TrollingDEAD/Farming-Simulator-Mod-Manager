using System.Text.RegularExpressions;
using FsModManager.Core.Models;

namespace FsModManager.Core.Resolution;

/// <summary>The kind of automatic fix (if any) that applies to a given <see cref="ModConflict"/>.</summary>
public enum ConflictFixKind
{
    /// <summary>No automatic fix exists for this conflict type — needs a human decision.</summary>
    NotFixable,

    /// <summary>Fixable via <see cref="StoreItemRenameResolver"/> (duplicate storeItem xmlFilename).</summary>
    StoreItemRename,

    /// <summary>
    /// Potentially fixable via <see cref="SharedDataFileMergeAnalyzer"/>/<see cref="MergeableSharedFileFix"/> —
    /// whether it actually is depends on running that analysis (requires reading both mods' file
    /// content), which this classification alone does not do.
    /// </summary>
    SharedDataFileMergeCandidate,
}

/// <summary>Structural classification of whether/how a conflict can be auto-fixed, with a plain-language explanation when it can't.</summary>
/// <param name="Kind">Which (if any) automatic fix path applies.</param>
/// <param name="NotFixableReason">Populated only when <see cref="Kind"/> is <see cref="ConflictFixKind.NotFixable"/> — shown to the user instead of a "Fix" button.</param>
public sealed record ConflictFixClassification(ConflictFixKind Kind, string? NotFixableReason);

/// <summary>
/// Classifies each conflict type as mechanically auto-fixable or not, without needing to understand
/// a mod's gameplay intent. Only duplicate storeItem filename collisions (rename) and non-overlapping
/// shared-data-file additions (merge) are ever auto-fixable — everything else gets a clear,
/// conflict-specific explanation of why it can't be, instead of a silent lack of a "Fix" button.
/// </summary>
public static class ConflictFixAdvisor
{
    public static ConflictFixClassification Classify(ModConflict conflict)
    {
        if (!string.IsNullOrWhiteSpace(conflict.ObjectAXmlFilename) &&
            !string.IsNullOrWhiteSpace(conflict.ObjectBXmlFilename) &&
            string.Equals(conflict.ObjectAXmlFilename, conflict.ObjectBXmlFilename, StringComparison.OrdinalIgnoreCase))
        {
            return new ConflictFixClassification(ConflictFixKind.StoreItemRename, null);
        }

        if (!string.IsNullOrWhiteSpace(conflict.SharedDataFileName))
        {
            return new ConflictFixClassification(ConflictFixKind.SharedDataFileMergeCandidate, null);
        }

        return new ConflictFixClassification(ConflictFixKind.NotFixable, DescribeWhyNotFixable(conflict));
    }

    private static string DescribeWhyNotFixable(ModConflict conflict)
    {
        var description = conflict.Description;

        if (description.Contains("share the internal name", StringComparison.OrdinalIgnoreCase))
        {
            return "Both mods use the same internal mod name. Renaming one would change the mod's own identity " +
                   "(affecting its save-game references and downloads) — that's a decision only the user (or the " +
                   "mod's author) should make, not something this app can safely guess.";
        }

        if (Regex.IsMatch(description, @"declare a specialization named", RegexOptions.IgnoreCase))
        {
            return "Both mods declare a vehicle specialization with the same name, which crashes FS25 at load. " +
                   "Fixing this means renaming a specialization's Lua code references throughout one mod — a " +
                   "code-level change this app can't safely make without understanding that mod's scripts.";
        }

        if (Regex.IsMatch(description, @"declare a (FillType|FruitType|VehicleType) named", RegexOptions.IgnoreCase))
        {
            return "Both mods declare a custom type with the same name. Their actual definitions likely differ " +
                   "(e.g. different crop prices or growth stages), so picking one automatically could silently " +
                   "apply the wrong values — this needs a manual decision.";
        }

        if (description.Contains("global Lua function named", StringComparison.OrdinalIgnoreCase))
        {
            return "Both mods define a Lua function with the same name. Automatically fixing this would require " +
                   "understanding what each function actually does, which this app cannot determine — one mod's " +
                   "author needs to rename theirs.";
        }

        return "This conflict type doesn't have a mechanical fix — its resolution depends on understanding what " +
               "the mods are actually trying to do, so it needs a manual decision.";
    }
}
