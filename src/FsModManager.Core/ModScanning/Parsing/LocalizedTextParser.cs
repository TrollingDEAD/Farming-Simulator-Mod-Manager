using System.Globalization;
using System.Xml.Linq;

namespace FsModManager.Core.ModScanning.Parsing;

/// <summary>
/// Shared logic for FS's recurring "either a dictionary of per-language child elements, or a bare
/// text value" element shape — used for modDesc.xml's &lt;title&gt; and a storeItem XML's
/// &lt;storeData&gt;&lt;name&gt;, which both follow the same convention.
/// </summary>
internal static class LocalizedTextParser
{
    /// <summary>
    /// Picks the best text for the current UI culture: exact two-letter language match, then
    /// "en", then whatever single entry exists, else the element's own flat text. Null if the
    /// element itself is null or has neither language children nor flat text.
    /// </summary>
    public static string? Parse(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var languageEntries = element.Elements()
            .Select(e => (Language: e.Name.LocalName, Value: e.Value?.Trim()))
            .Where(e => !string.IsNullOrWhiteSpace(e.Value))
            .ToList();

        if (languageEntries.Count == 0)
        {
            var flatValue = element.Value?.Trim();
            return string.IsNullOrWhiteSpace(flatValue) ? null : flatValue;
        }

        var currentLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var match = languageEntries.FirstOrDefault(e => string.Equals(e.Language, currentLanguage, StringComparison.OrdinalIgnoreCase));
        if (match.Value is not null)
        {
            return match.Value;
        }

        match = languageEntries.FirstOrDefault(e => string.Equals(e.Language, "en", StringComparison.OrdinalIgnoreCase));
        if (match.Value is not null)
        {
            return match.Value;
        }

        return languageEntries[0].Value;
    }
}
