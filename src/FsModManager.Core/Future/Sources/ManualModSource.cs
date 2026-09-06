using System.Text.RegularExpressions;
using FsModManager.Core.Data.Entities;
using HtmlAgilityPack;

namespace FsModManager.Core.Sources;

/// <summary>
/// A mod source the user configures manually with a direct download URL. We periodically
/// re-fetch the page and compare a version string extracted via a configured selector.
/// </summary>
public sealed class ManualModSource : IModSource
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private const string RegexPrefix = "regex:";
    private const string XPathPrefix = "xpath:";

    private readonly IHttpClientFactory _httpClientFactory;

    public ManualModSource(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public ModSourceType SourceType => ModSourceType.Manual;

    public async Task<ModSourceCheckResult> CheckForUpdateAsync(
        ModSource source,
        string? currentVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.VersionSelector))
        {
            return new ModSourceCheckResult(false, null, source.Url,
                "No VersionSelector configured; cannot determine the latest version automatically.");
        }

        string html;
        try
        {
            var client = _httpClientFactory.CreateClient(nameof(ManualModSource));
            html = await client.GetStringAsync(source.Url, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ModSourceCheckResult(false, null, source.Url, $"Failed to fetch source page: {ex.Message}");
        }

        var latestVersion = ExtractVersion(html, source.VersionSelector);
        if (latestVersion is null)
        {
            return new ModSourceCheckResult(false, null, source.Url,
                "VersionSelector did not match anything on the source page.");
        }

        var isNewer = !string.IsNullOrWhiteSpace(currentVersion) &&
                      !string.Equals(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase);

        return new ModSourceCheckResult(isNewer, latestVersion, source.Url, null);
    }

    /// <summary>
    /// Supports a "regex:" prefixed pattern (first capture group, or whole match if none) or an
    /// "xpath:" prefixed XPath expression. HtmlAgilityPack has no native CSS selector support, so a
    /// plain CSS selector is not accepted here — TODO: add a CSS-to-XPath translation layer if needed.
    /// </summary>
    private static string? ExtractVersion(string html, string selector)
    {
        if (selector.StartsWith(RegexPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var pattern = selector[RegexPrefix.Length..];
            var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
            if (!match.Success)
            {
                return null;
            }

            return match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : match.Value.Trim();
        }

        var xpath = selector.StartsWith(XPathPrefix, StringComparison.OrdinalIgnoreCase)
            ? selector[XPathPrefix.Length..]
            : selector;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var node = doc.DocumentNode.SelectSingleNode(xpath);
        return node?.InnerText.Trim();
    }
}
