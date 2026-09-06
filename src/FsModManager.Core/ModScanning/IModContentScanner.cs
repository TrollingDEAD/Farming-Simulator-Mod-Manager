using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning;

/// <summary>
/// On-demand, per-mod content scanner: opens the XML file each &lt;storeItem&gt; in a mod's
/// modDesc.xml points to and extracts shop details (display name, price, shop image, specs).
/// </summary>
/// <remarks>
/// Deliberately NOT part of <see cref="IModDirectoryScanner"/>'s fast pass — parsing every
/// referenced store XML for every mod up front would meaningfully slow down the initial
/// scan/rescan. Callers should invoke this only when a mod's row is actually expanded in the UI,
/// and rely on the built-in per-zip cache (see <see cref="InvalidateCache"/>) so re-expanding the
/// same mod doesn't re-parse it.
/// </remarks>
public interface IModContentScanner
{
    /// <summary>
    /// Returns shop/spec details for every storeItem declared in the mod's modDesc.xml. Never
    /// throws — missing/malformed referenced files or fields produce best-effort partial
    /// <see cref="StoreItemDetail"/> entries with warnings, not a skipped object or an exception.
    /// Results are cached per <see cref="ModFileInfo.ZipPath"/> until <see cref="InvalidateCache"/>
    /// is called for that path (e.g. after a "Rescan").
    /// </summary>
    Task<IReadOnlyList<StoreItemDetail>> ScanContentAsync(ModFileInfo mod, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a previously cached scan result for <paramref name="zipPath"/> WITHOUT triggering a
    /// new scan. Returns false (and a null <paramref name="content"/>) when nothing has been scanned
    /// for that path yet — callers that only want to reuse existing results (e.g. duplicate-mod
    /// resolution scoring) should use this instead of <see cref="ScanContentAsync"/>.
    /// </summary>
    bool TryGetCachedContent(string zipPath, out IReadOnlyList<StoreItemDetail>? content);

    /// <summary>Drops any cached scan result for the given mod zip path, e.g. after a rescan.</summary>
    void InvalidateCache(string zipPath);

    /// <summary>Drops every cached scan result.</summary>
    void ClearCache();
}
