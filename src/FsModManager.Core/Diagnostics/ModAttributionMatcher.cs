using System.Text.RegularExpressions;
using FsModManager.Core.Models;

namespace FsModManager.Core.Diagnostics;

/// <summary>
/// Attributes a Warning/Error <see cref="LogEntry"/> to a specific installed mod, without ever
/// forcing a guess (leaves <see cref="LogEntry.AttributedModInternalName"/> null when nothing matches).
/// </summary>
public static class ModAttributionMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    // A bracketed tag at (or very near) the start of the line, e.g. "[Soil Fertilizer] ..." - some
    // well-behaved mods self-tag their log output this way. Real log lines also use brackets for
    // "[ERROR]"/a "[--- 74]" thread marker, neither of which is a mod self-tag, so those are excluded.
    private static readonly Regex BracketTagRegex = new(@"\[(?<tag>[^\[\]]+)\]", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex NonModTagRegex = new(@"^(ERROR|WARNING|INFO|-+\s*\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);

    /// <summary>
    /// Returns the matched mod's <see cref="ModMetadata.InternalName"/>, or null if no known mod could
    /// be matched. Tries an explicit "[Tag]" self-tag against <see cref="ModMetadata.DisplayTitle"/>
    /// first (more reliable when present), then falls back to scanning the line for any known
    /// <see cref="ModMetadata.InternalName"/> appearing as a substring (path-based matching).
    /// </summary>
    public static string? Attribute(LogEntry entry, IReadOnlyList<ModMetadata> installedMods)
    {
        foreach (Match tagMatch in BracketTagRegex.Matches(entry.RawLine))
        {
            var tag = tagMatch.Groups["tag"].Value.Trim();
            if (NonModTagRegex.IsMatch(tag))
            {
                continue;
            }

            var byTitle = installedMods.FirstOrDefault(m => string.Equals(m.DisplayTitle, tag, StringComparison.OrdinalIgnoreCase));
            if (byTitle is not null)
            {
                return byTitle.InternalName;
            }
        }

        foreach (var mod in installedMods)
        {
            if (!string.IsNullOrEmpty(mod.InternalName) &&
                entry.RawLine.Contains(mod.InternalName, StringComparison.OrdinalIgnoreCase))
            {
                return mod.InternalName;
            }
        }

        return null;
    }
}
