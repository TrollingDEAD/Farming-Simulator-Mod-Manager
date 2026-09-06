namespace FsModManager.Core.Multiplayer;

/// <summary>
/// Diffs two manifests for multiplayer prep. Matching is by <see cref="ModManifestEntry.InternalModName"/>
/// (case-insensitive, the same convention used for internal names everywhere else in the app);
/// version strings are compared ordinally with null normalized to empty; content hashes are
/// compared case-insensitively (hex casing is not meaningful).
/// </summary>
public sealed class ManifestComparer
{
    public ManifestComparisonResult Compare(ModManifest mine, ModManifest theirs)
    {
        var mineByName = ByName(mine.Entries);
        var theirsByName = ByName(theirs.Entries);

        var missingOnMine = new List<ModManifestEntry>();
        var missingOnTheirs = new List<ModManifestEntry>();
        var versionMismatches = new List<ManifestVersionMismatch>();
        var hashMismatches = new List<ManifestHashMismatch>();
        var matching = new List<ModManifestEntry>();

        foreach (var (name, myEntry) in mineByName)
        {
            if (!theirsByName.TryGetValue(name, out var theirEntry))
            {
                missingOnTheirs.Add(myEntry);
                continue;
            }

            // A null version (modDesc.xml without one) can't confidently be called a "mismatch"
            // against anything — normalize to empty so only real differences get flagged.
            var myVersion = myEntry.Version ?? string.Empty;
            var theirVersion = theirEntry.Version ?? string.Empty;

            if (!string.Equals(myVersion, theirVersion, StringComparison.Ordinal))
            {
                versionMismatches.Add(new ManifestVersionMismatch(myEntry, theirEntry));
            }
            else if (!string.Equals(myEntry.ContentHash, theirEntry.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                // Same name, same version string, different bytes: the sneaky re-release case.
                // Flagged separately from a clean version bump because it's the classic
                // "but we HAVE the same version!" source of multiplayer desync confusion.
                hashMismatches.Add(new ManifestHashMismatch(myEntry, theirEntry));
            }
            else
            {
                matching.Add(myEntry);
            }
        }

        foreach (var (name, theirEntry) in theirsByName)
        {
            if (!mineByName.ContainsKey(name))
            {
                missingOnMine.Add(theirEntry);
            }
        }

        return new ManifestComparisonResult(
            SortByName(missingOnMine),
            SortByName(missingOnTheirs),
            versionMismatches.OrderBy(v => v.Mine.InternalModName, StringComparer.OrdinalIgnoreCase).ToList(),
            hashMismatches.OrderBy(h => h.Mine.InternalModName, StringComparer.OrdinalIgnoreCase).ToList(),
            SortByName(matching));
    }

    // First entry wins when a manifest lists the same internal name twice (a mods folder CAN
    // contain duplicate internal names — that's what the library conflict detectors exist for).
    private static Dictionary<string, ModManifestEntry> ByName(IReadOnlyList<ModManifestEntry> entries)
    {
        var map = new Dictionary<string, ModManifestEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            map.TryAdd(entry.InternalModName, entry);
        }

        return map;
    }

    private static List<ModManifestEntry> SortByName(List<ModManifestEntry> entries)
        => entries.OrderBy(e => e.InternalModName, StringComparer.OrdinalIgnoreCase).ToList();
}
