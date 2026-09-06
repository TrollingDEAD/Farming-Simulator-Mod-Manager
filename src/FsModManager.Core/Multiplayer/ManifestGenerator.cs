using System.Collections.Concurrent;
using FsModManager.Core.Hashing;
using FsModManager.Core.Models;
using FsModManager.Core.Savegames;

namespace FsModManager.Core.Multiplayer;

/// <summary>
/// Builds a <see cref="ModManifest"/> from scanned mods, computing a streamed SHA-256 over each
/// mod zip's bytes (large files are never fully buffered in memory).
///
/// Two scopes are supported:
/// <list type="bullet">
/// <item><see cref="GenerateAsync(IReadOnlyList{ModFileInfo}, string?, CancellationToken)"/> —
/// every scanned mod in the folder.</item>
/// <item><see cref="GenerateActiveAsync(IReadOnlyList{ModFileInfo}, string, string?, CancellationToken)"/> —
/// only mods marked active in a chosen savegame's mods.xml (via <see cref="ModLoadOrderReader"/>).
/// For multiplayer this is usually what you want: a manifest should reflect what's actually
/// active in the save you're about to play, not everything sitting in the folder.</item>
/// </list>
///
/// The <see cref="ModMetadata"/> overloads reuse <see cref="ModMetadata.FileHash"/> — the SHA-256
/// the directory scanner already computed during the mod-list scan — so exporting after a scan
/// does NOT re-read hundreds of MB of zips. Hashes the generator computes itself are cached
/// keyed by path+size+mtime, so repeated exports stay cheap and a changed file is re-hashed
/// automatically (mirrors the on-demand caching approach of ModContentScanner).
/// </summary>
public sealed class ManifestGenerator
{
    private readonly ModLoadOrderReader _loadOrderReader;

    // zip path -> hash + the file state it was computed from. Changed size/mtime = recompute.
    private readonly ConcurrentDictionary<string, HashCacheEntry> _hashCache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record HashCacheEntry(long SizeBytes, DateTime LastWriteUtc, string Hash);

    /// <summary>One mod plus, when available, the hash the scanner already computed for it.</summary>
    private sealed record HashInput(ModFileInfo Info, string? PrecomputedHash);

    public ManifestGenerator(ModLoadOrderReader loadOrderReader)
    {
        _loadOrderReader = loadOrderReader;
    }

    /// <summary>Manifest of ALL supplied mods.</summary>
    public Task<ModManifest> GenerateAsync(
        IReadOnlyList<ModFileInfo> mods,
        string? label,
        CancellationToken cancellationToken = default)
        => GenerateCoreAsync(mods.Select(m => new HashInput(m, null)), label, cancellationToken);

    /// <summary>Manifest of only the supplied mods that are active in the given savegame's mods.xml.</summary>
    public Task<ModManifest> GenerateActiveAsync(
        IReadOnlyList<ModFileInfo> mods,
        string savegameFolderPath,
        string? label,
        CancellationToken cancellationToken = default)
        => GenerateCoreAsync(
            FilterToActive(mods, ReadActiveNames(savegameFolderPath), m => m.InternalModName)
                .Select(m => new HashInput(m, null)),
            label,
            cancellationToken);

    /// <summary>Metadata variant of <see cref="GenerateAsync(IReadOnlyList{ModFileInfo}, string?, CancellationToken)"/> — reuses the scan-time hash when present.</summary>
    public Task<ModManifest> GenerateAsync(
        IReadOnlyList<ModMetadata> mods,
        string? label,
        CancellationToken cancellationToken = default)
        => GenerateCoreAsync(mods.Select(ToHashInput), label, cancellationToken);

    /// <summary>Metadata variant of <see cref="GenerateActiveAsync(IReadOnlyList{ModFileInfo}, string, string?, CancellationToken)"/>.</summary>
    public Task<ModManifest> GenerateActiveAsync(
        IReadOnlyList<ModMetadata> mods,
        string savegameFolderPath,
        string? label,
        CancellationToken cancellationToken = default)
        => GenerateCoreAsync(
            FilterToActive(mods, ReadActiveNames(savegameFolderPath), m => m.InternalName)
                .Select(ToHashInput),
            label,
            cancellationToken);

    /// <summary>Drops every cached hash (e.g. on a manual rescan).</summary>
    public void ClearCache() => _hashCache.Clear();

    private HashSet<string> ReadActiveNames(string savegameFolderPath)
        => _loadOrderReader.ReadLoadOrder(savegameFolderPath)
            .Where(e => e.Active)
            .Select(e => e.InternalModName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static List<T> FilterToActive<T>(IReadOnlyList<T> mods, HashSet<string> activeNames, Func<T, string> internalName)
        => mods.Where(m => activeNames.Contains(internalName(m))).ToList();

    private static HashInput ToHashInput(ModMetadata metadata)
        => new(
            new ModFileInfo(
                ZipPath: metadata.SourceFileName ?? string.Empty,
                InternalModName: metadata.InternalName,
                Version: metadata.Version,
                Author: metadata.Author,
                DescVersionParsed: metadata.DescVersionParsed,
                ParseWarnings: metadata.Warnings,
                DisplayTitle: metadata.DisplayTitle,
                IconImageData: metadata.IconImageData,
                FileSizeBytes: metadata.FileSizeBytes),
            metadata.FileHash);

    private async Task<ModManifest> GenerateCoreAsync(
        IEnumerable<HashInput> inputs,
        string? label,
        CancellationToken cancellationToken)
    {
        var inputList = inputs
            .Where(i => !string.IsNullOrWhiteSpace(i.Info.InternalModName) && !string.IsNullOrWhiteSpace(i.Info.ZipPath))
            .ToList();

        // Same throttle convention as ModDirectoryScanner: parallel, capped at ProcessorCount.
        var entries = new ModManifestEntry?[inputList.Count];
        using var throttle = new SemaphoreSlim(Environment.ProcessorCount);
        var tasks = inputList.Select(async (input, index) =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                entries[index] = await BuildEntryAsync(input, cancellationToken);
            }
            finally
            {
                throttle.Release();
            }
        }).ToArray();
        await Task.WhenAll(tasks);

        return new ModManifest(
            DateTime.UtcNow,
            string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            entries
                .Where(e => e is not null)
                .Cast<ModManifestEntry>()
                .OrderBy(e => e.InternalModName, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    /// <summary>
    /// Builds one entry; returns null when the file can't be hashed (deleted/locked since the
    /// scan). Skipping one unreadable mod beats failing the whole export.
    /// </summary>
    private async Task<ModManifestEntry?> BuildEntryAsync(HashInput input, CancellationToken cancellationToken)
    {
        var info = input.Info;

        // Scan-time hash: trusted as-is. If the user replaced the zip after scanning, the rest of
        // the app shows the same stale snapshot until the next rescan — consistent throughout.
        if (!string.IsNullOrEmpty(input.PrecomputedHash))
        {
            return new ModManifestEntry(
                info.InternalModName,
                info.DisplayTitle,
                info.Version,
                input.PrecomputedHash,
                info.FileSizeBytes);
        }

        try
        {
            var fileInfo = new FileInfo(info.ZipPath);
            if (!fileInfo.Exists)
            {
                return null;
            }

            var size = fileInfo.Length;
            var lastWriteUtc = fileInfo.LastWriteTimeUtc;

            if (!_hashCache.TryGetValue(info.ZipPath, out var cached)
                || cached.SizeBytes != size
                || cached.LastWriteUtc != lastWriteUtc)
            {
                var hash = await FileHashing.ComputeSha256Async(info.ZipPath, cancellationToken);
                cached = new HashCacheEntry(size, lastWriteUtc, hash);
                _hashCache[info.ZipPath] = cached;
            }

            return new ModManifestEntry(info.InternalModName, info.DisplayTitle, info.Version, cached.Hash, size);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
