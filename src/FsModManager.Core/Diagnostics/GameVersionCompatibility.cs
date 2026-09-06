using FsModManager.Core.Models;

namespace FsModManager.Core.Diagnostics;

/// <summary>
/// Result of evaluating a mod's descVersion against the library-wide maximum.
/// </summary>
public sealed record OutdatedModAssessment(
    string InternalName,
    string DisplayTitle,
    int ModDescVersion,
    int MaxLibraryDescVersion,
    int VersionDifference,
    string? SourceFileName,
    string Message);

/// <summary>
/// Relative heuristic compatibility helper.
/// 
/// NOTE: This check performs a RELATIVE heuristic comparison against the highest descVersion
/// found across the user's active mod library as a proxy for the target descVersion of recent mods,
/// NOT an authoritative, definitive version-to-descVersion matrix from GIANTS Software.
/// A mod with an older descVersion may still load and function normally in-game if none of the
/// engine API changes in subsequent updates impact its functionality.
/// </summary>
public static class GameVersionCompatibility
{
    public const int DefaultThresholdDifference = 2;

    /// <summary>
    /// Gets the highest parsed descVersion integer across a collection of mods.
    /// Returns null if no mods have a parsed descVersion integer.
    /// </summary>
    public static int? GetHighestDescVersion(IEnumerable<ModMetadata> mods)
    {
        var versions = mods
            .Where(m => m.DescVersionParsed && int.TryParse(m.DescVersion, out _))
            .Select(m => int.Parse(m.DescVersion!))
            .ToList();

        return versions.Count > 0 ? versions.Max() : null;
    }

    /// <summary>
    /// Flags mods whose descVersion is meaningfully lower than the library-wide maximum.
    /// </summary>
    /// <param name="mods">The scanned mod collection.</param>
    /// <param name="thresholdDifference">How many versions behind the maximum before flagging (default 2, meaning difference &gt; 2 is flagged).</param>
    /// <param name="detectedGameVersion">Optional detected game version string from GameVersionReader for context.</param>
    public static IReadOnlyList<OutdatedModAssessment> FindPotentiallyOutdatedMods(
        IEnumerable<ModMetadata> mods,
        int thresholdDifference = DefaultThresholdDifference,
        string? detectedGameVersion = null)
    {
        var modList = mods.ToList();
        var maxDescVersion = GetHighestDescVersion(modList);
        if (maxDescVersion is null)
        {
            return Array.Empty<OutdatedModAssessment>();
        }

        var results = new List<OutdatedModAssessment>();
        foreach (var mod in modList)
        {
            if (!mod.DescVersionParsed || !int.TryParse(mod.DescVersion, out var modDescVer))
            {
                continue;
            }

            var difference = maxDescVersion.Value - modDescVer;
            if (difference > thresholdDifference)
            {
                var gameVerInfo = !string.IsNullOrWhiteSpace(detectedGameVersion)
                    ? $" (detected game version: {detectedGameVersion})"
                    : string.Empty;

                var message = $"descVersion {modDescVer} is {difference} versions behind the library maximum ({maxDescVersion.Value}){gameVerInfo}. " +
                              "This mod may have been built for an earlier game version (heuristic comparison).";

                results.Add(new OutdatedModAssessment(
                    InternalName: mod.InternalName,
                    DisplayTitle: mod.DisplayTitle,
                    ModDescVersion: modDescVer,
                    MaxLibraryDescVersion: maxDescVersion.Value,
                    VersionDifference: difference,
                    SourceFileName: mod.SourceFileName,
                    Message: message));
            }
        }

        return results
            .OrderByDescending(r => r.VersionDifference)
            .ThenBy(r => r.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
