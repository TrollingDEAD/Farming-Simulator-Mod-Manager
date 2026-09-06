namespace FsModManager.Core.Multiplayer;

/// <summary>
/// One mod in a manifest: who it is, which version it claims, and a SHA-256 hash of the zip's
/// bytes. The hash is this app's OWN content hash (streamed SHA-256, uppercase hex) — it is
/// deliberately NOT Giants' internal mod hash (their algorithm is undocumented). All a
/// peer-to-peer comparison needs is that two instances of this app hash the same file
/// identically, which this guarantees.
/// </summary>
public sealed record ModManifestEntry(
    string InternalModName,
    string DisplayTitle,
    string? Version,
    string ContentHash,
    long FileSizeBytes);

/// <summary>
/// A portable snapshot of a mod library (or one savegame's active subset), exportable to a single
/// JSON file that a friend or server host can compare against their own.
/// </summary>
public sealed record ModManifest(
    DateTime GeneratedUtc,
    string? Label,
    IReadOnlyList<ModManifestEntry> Entries);
