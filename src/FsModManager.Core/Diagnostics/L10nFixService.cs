using System.IO.Compression;
using System.Xml.Linq;
using FsModManager.Core.Editing;

namespace FsModManager.Core.Diagnostics;

/// <summary>Applies the two deliberately limited l10n repairs through the safe mod editor.</summary>
public sealed class L10nFixService
{
    private readonly IModFileEditor _modFileEditor;

    public L10nFixService(IModFileEditor modFileEditor)
    {
        _modFileEditor = modFileEditor;
    }

    public async Task<IReadOnlyList<EditResult>> RemoveDuplicateAsync(
        string modZipPath,
        string key,
        CancellationToken cancellationToken = default)
    {
        var l10nEntries = FindL10nEntries(modZipPath);
        var seen = false;
        var results = new List<EditResult>();

        foreach (var entryPath in l10nEntries)
        {
            var result = await _modFileEditor.ApplyXmlTransformAsync(
                modZipPath,
                entryPath,
                document => RemoveDuplicates(document, key, ref seen),
                "remove duplicate l10n entry",
                cancellationToken);
            results.Add(result);
            if (!result.Success)
            {
                break;
            }
        }

        return results;
    }

    public async Task<EditResult?> AddPlaceholderAsync(
        string modZipPath,
        string key,
        CancellationToken cancellationToken = default)
    {
        var entryPath = FindL10nEntries(modZipPath).FirstOrDefault();
        if (entryPath is null)
        {
            return null;
        }

        return await _modFileEditor.ApplyXmlTransformAsync(
            modZipPath,
            entryPath,
            document => AddPlaceholder(document, key),
            "add placeholder l10n entry",
            cancellationToken);
    }

    private static XDocument RemoveDuplicates(XDocument document, string key, ref bool seen)
    {
        foreach (var element in document.Descendants().Where(IsTextEntry).ToList())
        {
            if (!string.Equals((string?)element.Attribute("name"), key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seen)
            {
                element.Remove();
            }
            else
            {
                seen = true;
            }
        }

        return document;
    }

    private static XDocument AddPlaceholder(XDocument document, string key)
    {
        if (document.Descendants().Any(element => IsTextEntry(element) &&
            string.Equals((string?)element.Attribute("name"), key, StringComparison.OrdinalIgnoreCase)))
        {
            return document;
        }

        var parent = document.Root;
        if (parent is null)
        {
            return document;
        }

        parent.Add(new XElement(parent.Name.Namespace + "text",
            new XAttribute("name", key),
            new XElement(parent.Name.Namespace + "en", HumanizeKey(key))));
        return document;
    }

    private static IReadOnlyList<string> FindL10nEntries(string modZipPath)
    {
        var paths = new List<string>();
        using var archive = ZipFile.OpenRead(modZipPath);
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var stream = entry.Open();
                var document = XDocument.Load(stream);
                if (document.Descendants().Any(IsTextEntry))
                {
                    paths.Add(entry.FullName);
                }
            }
            catch (Exception) when (entry.Length >= 0)
            {
                // The editor will report malformed XML if a selected entry is edited; skip unrelated XML here.
            }
        }

        return paths;
    }

    private static bool IsTextEntry(XElement element) =>
        element.Name.LocalName.Equals("text", StringComparison.OrdinalIgnoreCase) &&
        element.Attribute("name") is not null;

    private static string HumanizeKey(string key)
    {
        var words = key.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 0 && words[0].Equals("input", StringComparison.OrdinalIgnoreCase))
        {
            words.RemoveAt(0);
        }

        if (words.Count > 1 && words[0].All(char.IsUpper))
        {
            words.RemoveAt(0);
        }

        return string.Join(" ", words.Select(word =>
            word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }
}
