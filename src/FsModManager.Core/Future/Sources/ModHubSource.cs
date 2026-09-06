using FsModManager.Core.Data.Entities;

namespace FsModManager.Core.Sources;

/// <summary>
/// Placeholder implementation for checking updates on the FS ModHub. There is no public ModHub
/// API, so a working implementation would need to scrape ModHub's HTML pages with HtmlAgilityPack
/// (already referenced by this project) while respecting ModHub's terms of service and robots.txt.
/// TODO: implement HTML scraping once the scraping approach/legal review is confirmed. Until then
/// this intentionally throws so callers don't silently get incorrect "no update" results.
/// </summary>
public sealed class ModHubSource : IModSource
{
    public ModSourceType SourceType => ModSourceType.ModHub;

    public Task<ModSourceCheckResult> CheckForUpdateAsync(
        ModSource source,
        string? currentVersion,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException(
            "ModHub has no public API. Implementing this requires HTML scraping (HtmlAgilityPack) " +
            "of the ModHub mod detail page, which must respect ModHub's terms of service. Not implemented yet.");
}
