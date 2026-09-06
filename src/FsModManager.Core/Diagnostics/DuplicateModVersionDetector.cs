using System.Text.RegularExpressions;
using FsModManager.Core.Models;

namespace FsModManager.Core.Diagnostics;

/// <summary>
/// Information about one physical copy/version of a duplicate mod.
/// </summary>
public sealed record DuplicateModCopy(
    ModMetadata Metadata,
    string FileName,
    string VersionDisplay,
    string FileSizeDisplay,
    string LastModifiedDisplay,
    string? SourceFilePath);

/// <summary>
/// A group of multiple files detected as different copies or versions of the same mod.
/// </summary>
public sealed record DuplicateModGroup(
    string DisplayTitle,
    string NormalizedBaseName,
    IReadOnlyList<DuplicateModCopy> Copies);

/// <summary>
/// Detects duplicate or stale copies of the same mod in a library.
/// Uses InternalModName/filename base similarity cross-referenced with parsed DisplayTitle
/// to avoid false positives.
/// </summary>
public static partial class DuplicateModVersionDetector
{
    [GeneratedRegex(@"[-_\s]v?\d+(?:[._]\d+){0,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffixRegex();

    [GeneratedRegex(@"\s*\(\d+\)$", RegexOptions.IgnoreCase)]
    private static partial Regex DuplicateParenthesesRegex();

    [GeneratedRegex(@"[-_]copy\d*$", RegexOptions.IgnoreCase)]
    private static partial Regex CopyWordSuffixRegex();

    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex NonAlphanumericRegex();

    [GeneratedRegex(@"_+")]
    private static partial Regex MultipleUnderscoresRegex();

    /// <summary>
    /// Normalizes a mod filename or internal name by stripping version suffixes, copy suffixes like (1),
    /// and formatting variations.
    /// </summary>
    public static string NormalizeBaseName(string? filenameOrName)
    {
        if (string.IsNullOrWhiteSpace(filenameOrName))
        {
            return string.Empty;
        }

        var name = Path.GetFileName(filenameOrName);
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        // Apply cleanups iteratively (e.g., "FS25_SomeMod_v2 (1)" -> "FS25_SomeMod_v2" -> "FS25_SomeMod")
        var previous = string.Empty;
        while (!string.Equals(previous, name, StringComparison.Ordinal))
        {
            previous = name;
            name = DuplicateParenthesesRegex().Replace(name, string.Empty).Trim();
            name = CopyWordSuffixRegex().Replace(name, string.Empty).Trim();
            name = VersionSuffixRegex().Replace(name, string.Empty).Trim();
        }

        name = NonAlphanumericRegex().Replace(name, "_");
        name = MultipleUnderscoresRegex().Replace(name, "_").Trim('_');

        return name.ToLowerInvariant();
    }

    /// <summary>
    /// Scans a collection of mod metadata for duplicate copies or versions of the same mod.
    /// </summary>
    public static IReadOnlyList<DuplicateModGroup> DetectDuplicates(IEnumerable<ModMetadata> mods)
    {
        var modList = mods.ToList();
        if (modList.Count < 2)
        {
            return Array.Empty<DuplicateModGroup>();
        }

        // Group by (NormalizedBaseName, NormalizedTitle)
        var groups = modList
            .GroupBy(m => (
                BaseName: NormalizeBaseName(m.SourceFileName ?? m.InternalName),
                Title: (m.DisplayTitle ?? m.InternalName).Trim().ToLowerInvariant()
            ))
            .Where(g => !string.IsNullOrEmpty(g.Key.BaseName) && !string.IsNullOrEmpty(g.Key.Title))
            .Where(g => g.Count() > 1)
            .ToList();

        var result = new List<DuplicateModGroup>();

        foreach (var group in groups)
        {
            // Only flag if there are actually multiple distinct physical files
            var distinctFiles = group
                .GroupBy(m => m.SourceFileName ?? m.InternalName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (distinctFiles.Count <= 1)
            {
                continue;
            }

            var canonicalTitle = group.First().DisplayTitle;
            var canonicalBaseName = group.Key.BaseName;

            var copies = distinctFiles
                .OrderBy(m => Path.GetFileName(m.SourceFileName ?? m.InternalName), StringComparer.OrdinalIgnoreCase)
                .Select(m => new DuplicateModCopy(
                    Metadata: m,
                    FileName: Path.GetFileName(m.SourceFileName ?? m.InternalName),
                    VersionDisplay: string.IsNullOrWhiteSpace(m.Version) ? "—" : m.Version!,
                    FileSizeDisplay: FormatFileSize(m.FileSizeBytes),
                    LastModifiedDisplay: m.LastModifiedUtc == default ? "—" : m.LastModifiedUtc.ToLocalTime().ToString("g"),
                    SourceFilePath: m.SourceFileName))
                .ToList();

            result.Add(new DuplicateModGroup(
                DisplayTitle: canonicalTitle,
                NormalizedBaseName: canonicalBaseName,
                Copies: copies));
        }

        return result
            .OrderBy(g => g.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "—";
        }

        string[] sizes = ["B", "KB", "MB", "GB"];
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.#} {sizes[order]}";
    }
}
