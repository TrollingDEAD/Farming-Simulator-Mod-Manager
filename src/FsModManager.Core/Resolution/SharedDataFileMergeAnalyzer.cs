using System.IO.Compression;
using System.Xml.Linq;
using FsModManager.Core.Editing;

namespace FsModManager.Core.Resolution;

/// <summary>One shared-data-file entry (e.g. a &lt;fillType&gt;) defined differently by both mods.</summary>
public sealed record ConflictingSharedDataEntry(string Name, string DefinitionFromModA, string DefinitionFromModB);

/// <summary>
/// Result of analyzing whether two mods' copies of the same shared base-game data file (e.g.
/// fillTypes.xml/densityHeights.xml) can be safely auto-merged.
/// </summary>
/// <param name="IsMergeable">True when both mods' additions are disjoint (or identically defined where they overlap).</param>
/// <param name="MergedXml">The merged document text, only populated when <see cref="IsMergeable"/> is true.</param>
/// <param name="ConflictingEntries">Entries defined differently by both mods — populated only when NOT mergeable.</param>
/// <param name="Reason">Human-readable explanation of the outcome, always populated.</param>
/// <param name="EntryPathInModA">The actual zip entry path of the shared file inside mod A's archive.</param>
/// <param name="EntryPathInModB">The actual zip entry path of the shared file inside mod B's archive.</param>
public sealed record SharedDataFileMergeResult(
    bool IsMergeable,
    string? MergedXml,
    IReadOnlyList<ConflictingSharedDataEntry> ConflictingEntries,
    string Reason,
    string? EntryPathInModA = null,
    string? EntryPathInModB = null);

/// <summary>
/// Determines whether two mods that both ship a copy of the same shared base-game data file
/// (flagged by <see cref="FsModManager.Core.ModScanning.Conflicts.SharedDataFileOverrideDetector"/>)
/// can be safely reconciled into one merged file both mods can share, instead of one silently
/// overriding the other. Works generically for any file shaped as a collection of named entries
/// (each entry element carries a "name" attribute) — this covers both fillTypes.xml
/// (&lt;fillType name="..."/&gt;) and densityHeights.xml without file-specific code paths.
/// </summary>
public sealed class SharedDataFileMergeAnalyzer
{
    /// <summary>
    /// Locates the shared file inside each mod's zip (matched by filename only, any folder — same
    /// convention as <see cref="FsModManager.Core.Models.ModMetadata.SharedDataFileMatches"/>), parses
    /// both copies, and determines whether they can be merged.
    /// </summary>
    public SharedDataFileMergeResult AnalyzeFromZips(
        string modAZipPath,
        string modAInternalName,
        string modBZipPath,
        string modBInternalName,
        string sharedFileName)
    {
        string entryPathA, entryPathB, xmlA, xmlB;
        try
        {
            using var archiveA = ZipFile.OpenRead(modAZipPath);
            var entryA = FindEntryByFileName(archiveA, sharedFileName);
            if (entryA is null)
            {
                return NotMergeable($"'{sharedFileName}' could not be found inside {modAInternalName}'s zip.");
            }

            entryPathA = entryA.FullName;
            using var readerA = new StreamReader(entryA.Open());
            xmlA = readerA.ReadToEnd();
        }
        catch (Exception ex)
        {
            return NotMergeable($"Failed to read '{sharedFileName}' from {modAInternalName}: {ex.Message}");
        }

        try
        {
            using var archiveB = ZipFile.OpenRead(modBZipPath);
            var entryB = FindEntryByFileName(archiveB, sharedFileName);
            if (entryB is null)
            {
                return NotMergeable($"'{sharedFileName}' could not be found inside {modBInternalName}'s zip.");
            }

            entryPathB = entryB.FullName;
            using var readerB = new StreamReader(entryB.Open());
            xmlB = readerB.ReadToEnd();
        }
        catch (Exception ex)
        {
            return NotMergeable($"Failed to read '{sharedFileName}' from {modBInternalName}: {ex.Message}");
        }

        var result = Analyze(sharedFileName, xmlA, modAInternalName, xmlB, modBInternalName);
        return result with { EntryPathInModA = entryPathA, EntryPathInModB = entryPathB };
    }

    /// <summary>Pure XML-in, result-out analysis — no file I/O, used directly by tests.</summary>
    public SharedDataFileMergeResult Analyze(string fileName, string modAXml, string modAInternalName, string modBXml, string modBInternalName)
    {
        XDocument docA, docB;
        try
        {
            docA = XDocument.Parse(modAXml);
        }
        catch (Exception ex)
        {
            return NotMergeable($"'{fileName}' from {modAInternalName} is not valid XML: {ex.Message}");
        }

        try
        {
            docB = XDocument.Parse(modBXml);
        }
        catch (Exception ex)
        {
            return NotMergeable($"'{fileName}' from {modBInternalName} is not valid XML: {ex.Message}");
        }

        var entriesA = FindNamedEntries(docA);
        var entriesB = FindNamedEntries(docB);
        if (entriesA.Count == 0 || entriesB.Count == 0)
        {
            return NotMergeable($"Could not identify individually-named entries in '{fileName}' for one or both mods — falling back to manual review.");
        }

        var mapA = ToNameMap(entriesA);
        var mapB = ToNameMap(entriesB);

        var conflicts = new List<ConflictingSharedDataEntry>();
        foreach (var name in mapA.Keys.Intersect(mapB.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (!XNode.DeepEquals(mapA[name], mapB[name]))
            {
                conflicts.Add(new ConflictingSharedDataEntry(
                    name,
                    mapA[name].ToString(SaveOptions.DisableFormatting),
                    mapB[name].ToString(SaveOptions.DisableFormatting)));
            }
        }

        if (conflicts.Count > 0)
        {
            return new SharedDataFileMergeResult(
                IsMergeable: false,
                MergedXml: null,
                ConflictingEntries: conflicts,
                Reason: $"{conflicts.Count} entr{(conflicts.Count == 1 ? "y is" : "ies are")} defined differently by both mods in '{fileName}' — " +
                        "this needs a manual decision about which value to keep, not an automatic merge.");
        }

        // No reference copy of the base game's original file is available here, so — as a documented
        // heuristic, not a certainty — the mod version with MORE total entries is assumed to be the
        // more complete superset (base game content plus that mod's own additions) and is used as the
        // merge base; the other mod's uniquely-named entries are copied into it. If this assumption is
        // wrong for a particular pair of mods, the smaller file's unique entries are still preserved
        // either way, so no data is lost — only the "which file's structure wins" choice is a guess.
        var useAAsBase = entriesA.Count >= entriesB.Count;
        var baseDoc = new XDocument(useAAsBase ? docA : docB);
        var baseMap = useAAsBase ? mapA : mapB;
        var otherMap = useAAsBase ? mapB : mapA;

        var container = FindNamedEntries(baseDoc).FirstOrDefault()?.Parent;
        if (container is null)
        {
            return NotMergeable($"Could not locate the entry container element in '{fileName}' to merge into.");
        }

        foreach (var (name, element) in otherMap)
        {
            if (!baseMap.ContainsKey(name))
            {
                container.Add(new XElement(element));
            }
        }

        return new SharedDataFileMergeResult(
            IsMergeable: true,
            MergedXml: baseDoc.ToString(),
            ConflictingEntries: Array.Empty<ConflictingSharedDataEntry>(),
            Reason: $"Both mods' additions to '{fileName}' don't overlap — merged into one identical copy both mods will use, so load order no longer matters for this file.");
    }

    private static SharedDataFileMergeResult NotMergeable(string reason) =>
        new(false, null, Array.Empty<ConflictingSharedDataEntry>(), reason);

    private static ZipArchiveEntry? FindEntryByFileName(ZipArchive archive, string fileName) =>
        archive.Entries.FirstOrDefault(e => string.Equals(Path.GetFileName(e.FullName), fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds the shallowest group of same-parent, same-tag elements that carry a "name" attribute —
    /// works generically regardless of the actual element/collection names used by a given shared
    /// data file (fillTypes.xml's &lt;fillType&gt;, or whatever densityHeights.xml's entries are
    /// called), rather than hardcoding a schema per file.
    /// </summary>
    private static List<XElement> FindNamedEntries(XDocument doc)
    {
        if (doc.Root is null)
        {
            return new List<XElement>();
        }

        var named = doc.Root.DescendantsAndSelf()
            .Where(e => e.Parent is not null && e.Attribute("name") is not null)
            .ToList();

        if (named.Count == 0)
        {
            return new List<XElement>();
        }

        var groups = named.GroupBy(e => (Parent: e.Parent, e.Name)).ToList();
        var minDepth = groups.Min(g => Depth(g.Key.Parent));
        return groups.Where(g => Depth(g.Key.Parent) == minDepth).SelectMany(g => g).ToList();
    }

    private static Dictionary<string, XElement> ToNameMap(IEnumerable<XElement> entries)
    {
        var map = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var name = entry.Attribute("name")!.Value;
            map[name] = entry; // last-wins if a single file redeclares the same name more than once
        }

        return map;
    }

    private static int Depth(XElement? element)
    {
        var depth = 0;
        while (element?.Parent is not null)
        {
            depth++;
            element = element.Parent;
        }

        return depth;
    }
}

/// <summary>
/// Applies a mergeable <see cref="SharedDataFileMergeResult"/> to BOTH mods so they end up with the
/// identical merged file — this eliminates the conflict entirely rather than picking a winner,
/// since load order no longer matters once both copies are the same.
/// </summary>
public sealed class MergeableSharedFileFix
{
    private readonly IModFileEditor _editor;

    public MergeableSharedFileFix(IModFileEditor editor)
    {
        _editor = editor;
    }

    public async Task<(EditResult ResultForModA, EditResult ResultForModB)> ApplyAsync(
        SharedDataFileMergeResult result,
        string modAZipPath,
        string modBZipPath,
        CancellationToken cancellationToken = default)
    {
        if (!result.IsMergeable || result.MergedXml is null || result.EntryPathInModA is null || result.EntryPathInModB is null)
        {
            var failure = new EditResult(false, "Merge result is not mergeable — nothing to apply.", string.Empty, Array.Empty<string>(), Array.Empty<string>());
            return (failure, failure);
        }

        var resultA = await _editor.ApplyEditAsync(
            modAZipPath,
            new ModFileEdit(result.EntryPathInModA, EditOperation.ReplaceEntryContent, result.MergedXml),
            "shared-data-file-merge-fix",
            cancellationToken);

        if (!resultA.Success)
        {
            var skipped = new EditResult(false, "Skipped applying to the second mod because the first mod's edit failed.", string.Empty, Array.Empty<string>(), Array.Empty<string>());
            return (resultA, skipped);
        }

        var resultB = await _editor.ApplyEditAsync(
            modBZipPath,
            new ModFileEdit(result.EntryPathInModB, EditOperation.ReplaceEntryContent, result.MergedXml),
            "shared-data-file-merge-fix",
            cancellationToken);

        return (resultA, resultB);
    }
}
