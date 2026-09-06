namespace FsModManager.Core.Multiplayer;

/// <summary>Both sides of an entry present in both manifests but with different Version strings.</summary>
public sealed record ManifestVersionMismatch(ModManifestEntry Mine, ModManifestEntry Theirs);

/// <summary>
/// Both sides of an entry with matching name AND matching version string but different content
/// hash — the sneaky case: a mod author re-released under an unchanged version number, so the
/// games look identical on paper but the actual files differ.
/// </summary>
public sealed record ManifestHashMismatch(ModManifestEntry Mine, ModManifestEntry Theirs);

/// <summary>
/// The peer-to-peer diff of two manifests. This is intentionally NOT typed as conflict severities:
/// nothing here is "a conflict in your library", it's "your list and their list disagree".
/// </summary>
public sealed record ManifestComparisonResult(
    /// <summary>Present in theirs, absent in mine — what I still need to go find and install.</summary>
    IReadOnlyList<ModManifestEntry> MissingOnMine,
    /// <summary>Present in mine, absent in theirs.</summary>
    IReadOnlyList<ModManifestEntry> MissingOnTheirs,
    /// <summary>Present in both, different version strings.</summary>
    IReadOnlyList<ManifestVersionMismatch> VersionMismatches,
    /// <summary>Present in both with the SAME version string, but different file content.</summary>
    IReadOnlyList<ManifestHashMismatch> HashMismatches,
    /// <summary>Agree on name, version, and content hash.</summary>
    IReadOnlyList<ModManifestEntry> Matching)
{
    /// <summary>True when every entry agrees on all three compared fields — safe to play together.</summary>
    public bool IsMatch =>
        MissingOnMine.Count == 0
        && MissingOnTheirs.Count == 0
        && VersionMismatches.Count == 0
        && HashMismatches.Count == 0;
}
