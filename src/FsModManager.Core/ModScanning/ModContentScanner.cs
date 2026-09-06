using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using FsModManager.Core.ModScanning.Parsing;
using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning;

/// <inheritdoc cref="IModContentScanner"/>
public sealed partial class ModContentScanner : IModContentScanner
{
    // Real FS25 storeData/specs elements are plain numbers with no units (confirmed against
    // several installed mods) and their element name IS the meaningful label — this humanizes it
    // (e.g. "maxSpeed" -> "Max Speed"). "combination" entries are attachment cross-references, not
    // specs, and are skipped.
    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex CamelCaseBoundaryRegex();

    private static readonly string[] NonSpecElementNames = { "combination" };

    private readonly ConcurrentDictionary<string, IReadOnlyList<StoreItemDetail>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<StoreItemDetail>> ScanContentAsync(ModFileInfo mod, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(mod.ZipPath, out var cached))
        {
            return Task.FromResult(cached);
        }

        var result = ScanContent(mod.ZipPath);
        _cache[mod.ZipPath] = result;
        return Task.FromResult(result);
    }

    public bool TryGetCachedContent(string zipPath, out IReadOnlyList<StoreItemDetail>? content) =>
        _cache.TryGetValue(zipPath, out content);

    public void InvalidateCache(string zipPath) => _cache.TryRemove(zipPath, out _);

    public void ClearCache() => _cache.Clear();

    private static IReadOnlyList<StoreItemDetail> ScanContent(string zipPath)
    {
        if (!File.Exists(zipPath))
        {
            return Array.Empty<StoreItemDetail>();
        }

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(zipPath);
        }
        catch
        {
            return Array.Empty<StoreItemDetail>();
        }

        using (archive)
        {
            var modDescEntry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, "modDesc.xml", StringComparison.OrdinalIgnoreCase));
            if (modDescEntry is null)
            {
                return Array.Empty<StoreItemDetail>();
            }

            XElement? modDescRoot;
            try
            {
                using var stream = modDescEntry.Open();
                modDescRoot = XDocument.Load(stream).Root;
            }
            catch
            {
                return Array.Empty<StoreItemDetail>();
            }

            var xmlFilenames = modDescRoot?.Element("storeItems")?.Elements("storeItem")
                .Select(e => e.Attribute("xmlFilename")?.Value)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f!)
                .ToList() ?? new List<string>();

            var details = new List<StoreItemDetail>();
            foreach (var xmlFilename in xmlFilenames)
            {
                // "$data/..." references point into the base game's own data, not this mod's zip —
                // there's nothing here to open, and that's expected, not an error.
                if (xmlFilename.StartsWith("$data/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                details.Add(ScanOneStoreItem(archive, xmlFilename));
            }

            return details;
        }
    }

    private static StoreItemDetail ScanOneStoreItem(ZipArchive archive, string xmlFilename)
    {
        var warnings = new List<string>();

        var normalized = xmlFilename.Replace('\\', '/').TrimStart('.', '/');
        var entry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/').TrimStart('.', '/'), normalized, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            warnings.Add($"Referenced store item file \"{xmlFilename}\" was not found in the mod's zip.");
            return new StoreItemDetail(xmlFilename, xmlFilename, null, null, Array.Empty<SpecEntry>(), null, StoreItemKind.Unknown)
            {
                Warnings = warnings,
            };
        }

        XElement? root;
        try
        {
            using var stream = entry.Open();
            root = XDocument.Load(stream).Root;
        }
        catch (XmlException ex)
        {
            warnings.Add($"\"{xmlFilename}\" is not well-formed XML: {ex.Message}");
            return new StoreItemDetail(xmlFilename, xmlFilename, null, null, Array.Empty<SpecEntry>(), null, StoreItemKind.Unknown)
            {
                Warnings = warnings,
            };
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to read \"{xmlFilename}\": {ex.Message}");
            return new StoreItemDetail(xmlFilename, xmlFilename, null, null, Array.Empty<SpecEntry>(), null, StoreItemKind.Unknown)
            {
                Warnings = warnings,
            };
        }

        if (root is null)
        {
            warnings.Add($"\"{xmlFilename}\" has no root element.");
            return new StoreItemDetail(xmlFilename, xmlFilename, null, null, Array.Empty<SpecEntry>(), null, StoreItemKind.Unknown)
            {
                Warnings = warnings,
            };
        }

        // MassKg/RawFillCapacities/ConfigurationGroupCounts live under <base>/anywhere in the object
        // XML, not <storeData> — computed regardless of whether <storeData> itself is present.
        var massKg = ParseMassKg(root);
        var rawFillCapacities = ParseFillCapacities(root);
        var configurationGroupCounts = ParseConfigurationGroups(root);

        var storeData = root.Element("storeData");
        if (storeData is null)
        {
            warnings.Add($"\"{xmlFilename}\" has no <storeData> element.");
            return new StoreItemDetail(xmlFilename, xmlFilename, null, null, Array.Empty<SpecEntry>(), null, DetermineKind(root))
            {
                Warnings = warnings,
                MassKg = massKg,
                RawFillCapacities = rawFillCapacities,
                ConfigurationGroupCounts = configurationGroupCounts,
            };
        }

        var displayName = LocalizedTextParser.Parse(storeData.Element("name"));
        if (string.IsNullOrWhiteSpace(displayName))
        {
            warnings.Add($"\"{xmlFilename}\" has no usable <name> in <storeData>.");
            displayName = xmlFilename;
        }

        decimal? price = null;
        var priceText = storeData.Element("price")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(priceText))
        {
            if (decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedPrice))
            {
                price = parsedPrice;
            }
            else
            {
                warnings.Add($"\"{xmlFilename}\" has a <price> that isn't a valid number: \"{priceText}\".");
            }
        }

        var shopImageData = TryExtractShopImageData(archive, storeData, warnings, xmlFilename);

        var category = storeData.Element("category")?.Value?.Trim();
        category = string.IsNullOrWhiteSpace(category) ? null : category;

        var specs = ParseSpecs(storeData.Element("specs"));

        var brand = storeData.Element("brand")?.Value?.Trim();
        brand = string.IsNullOrWhiteSpace(brand) ? null : brand;

        var functions = storeData.Element("functions")?.Elements("function")
            .Select(e => e.Value?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList() ?? new List<string>();

        return new StoreItemDetail(xmlFilename, displayName, price, shopImageData, specs, category, DetermineKind(root))
        {
            Warnings = warnings,
            Brand = brand,
            MassKg = massKg,
            Functions = functions,
            RawFillCapacities = rawFillCapacities,
            // See remarks on StoreItemDetail.RequiredPowerHp - no confirmed real pattern found,
            // intentionally left null rather than guessing.
            RequiredPowerHp = null,
            ConfigurationGroupCounts = configurationGroupCounts,
        };
    }

    /// <summary>
    /// Confirmed real pattern: &lt;base&gt;&lt;components&gt;&lt;component mass="..."/&gt;&lt;/components&gt;
    /// &lt;/base&gt;. Sums every component's mass; null when there's no &lt;components&gt; block at all
    /// (e.g. most placeables) or none of its components has a parseable mass.
    /// </summary>
    private static decimal? ParseMassKg(XElement root)
    {
        var components = root.Element("base")?.Element("components")?.Elements("component").ToList();
        if (components is null || components.Count == 0)
        {
            return null;
        }

        decimal total = 0;
        var any = false;
        foreach (var component in components)
        {
            var massText = component.Attribute("mass")?.Value;
            if (decimal.TryParse(massText, NumberStyles.Number, CultureInfo.InvariantCulture, out var mass))
            {
                total += mass;
                any = true;
            }
        }

        return any ? total : null;
    }

    /// <summary>
    /// Confirmed real pattern: &lt;fillUnit fillTypes="..."|fillTypeCategories="..." capacity="..."/&gt;,
    /// commonly nested several levels deep under &lt;fillUnitConfigurations&gt;&lt;fillUnitConfiguration&gt;
    /// &lt;fillUnits&gt;. This flattens every &lt;fillUnit&gt; found anywhere in the document rather than
    /// picking one "default" configuration variant, since there's no reliable way to know which
    /// variant is the default from the XML alone.
    /// </summary>
    private static IReadOnlyList<(string FillTypeName, decimal LiterCapacity)> ParseFillCapacities(XElement root)
    {
        var result = new List<(string, decimal)>();
        foreach (var fillUnit in root.Descendants("fillUnit"))
        {
            var capacityText = fillUnit.Attribute("capacity")?.Value;
            if (!decimal.TryParse(capacityText, NumberStyles.Number, CultureInfo.InvariantCulture, out var capacity))
            {
                continue;
            }

            var fillTypeName = fillUnit.Attribute("fillTypes")?.Value ?? fillUnit.Attribute("fillTypeCategories")?.Value;
            if (string.IsNullOrWhiteSpace(fillTypeName))
            {
                continue;
            }

            result.Add((fillTypeName.Trim(), capacity));
        }

        return result;
    }

    /// <summary>
    /// Confirmed generic, recurring pattern across real vehicle XML: any &lt;xConfigurations&gt;
    /// element (e.g. &lt;designConfigurations&gt;, &lt;wheelConfigurations&gt;, &lt;motorConfigurations&gt;)
    /// containing one or more &lt;xConfiguration&gt; children. Counts each such group found anywhere
    /// in the document; groups with zero matching children are skipped.
    /// </summary>
    private static IReadOnlyList<(string GroupName, int OptionCount)> ParseConfigurationGroups(XElement root)
    {
        var groups = new List<(string, int)>();
        foreach (var element in root.Descendants())
        {
            var name = element.Name.LocalName;
            if (!name.EndsWith("Configurations", StringComparison.Ordinal) || name.Length <= "Configurations".Length)
            {
                continue;
            }

            var singular = name[..^1];
            var count = element.Elements().Count(c => string.Equals(c.Name.LocalName, singular, StringComparison.OrdinalIgnoreCase));
            if (count > 0)
            {
                var groupName = HumanizeElementName(name[..^"Configurations".Length]);
                groups.Add((groupName, count));
            }
        }

        return groups;
    }

    /// <summary>
    /// Root element is &lt;placeable&gt; for buildings/decoration, or &lt;vehicle type="..."&gt;
    /// for everything else. FS doesn't document an authoritative list of drivable vs. towed
    /// "type" values, so this uses the observed convention that drivable vehicle types contain
    /// "Drivable" (e.g. "combineDrivable", "tractorDrivable") — anything else under &lt;vehicle&gt;
    /// is treated as a towed/attached implement (e.g. "cutter").
    /// </summary>
    private static StoreItemKind DetermineKind(XElement root)
    {
        if (string.Equals(root.Name.LocalName, "placeable", StringComparison.OrdinalIgnoreCase))
        {
            return StoreItemKind.Placeable;
        }

        if (string.Equals(root.Name.LocalName, "vehicle", StringComparison.OrdinalIgnoreCase))
        {
            var type = root.Attribute("type")?.Value ?? string.Empty;
            return type.Contains("Drivable", StringComparison.OrdinalIgnoreCase) ? StoreItemKind.Vehicle : StoreItemKind.Implement;
        }

        return StoreItemKind.Unknown;
    }

    private static IReadOnlyList<SpecEntry> ParseSpecs(XElement? specsElement)
    {
        if (specsElement is null)
        {
            return Array.Empty<SpecEntry>();
        }

        return specsElement.Elements()
            .Where(e => !NonSpecElementNames.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            .Select(e => (Label: HumanizeElementName(e.Name.LocalName), Value: e.Value?.Trim()))
            .Where(s => !string.IsNullOrWhiteSpace(s.Value))
            .Select(s => new SpecEntry(s.Label, s.Value!))
            .ToList();
    }

    private static string HumanizeElementName(string elementName)
    {
        if (string.IsNullOrEmpty(elementName))
        {
            return elementName;
        }

        var spaced = CamelCaseBoundaryRegex().Replace(elementName, " $1");
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    /// <summary>
    /// storeData/image is a plain zip-relative path — .dds needs the same Pfim-backed decode the
    /// mod icon uses, anything else (.png/.jpg/etc, also seen in real mods) WPF/any UI framework
    /// can decode natively, so the raw bytes are returned unchanged.
    /// </summary>
    private static byte[]? TryExtractShopImageData(ZipArchive archive, XElement storeData, List<string> warnings, string xmlFilename)
    {
        var imagePath = storeData.Element("image")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        try
        {
            var normalized = imagePath.Replace('\\', '/').TrimStart('.', '/');
            var imageEntry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName.Replace('\\', '/').TrimStart('.', '/'), normalized, StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(e => string.Equals(e.Name, Path.GetFileName(normalized), StringComparison.OrdinalIgnoreCase));

            if (imageEntry is null)
            {
                warnings.Add($"\"{xmlFilename}\"'s shop image \"{imagePath}\" was not found in the mod's zip.");
                return null;
            }

            using var entryStream = imageEntry.Open();
            using var memoryStream = new MemoryStream();
            entryStream.CopyTo(memoryStream);
            var rawBytes = memoryStream.ToArray();

            return Path.GetExtension(imagePath).Equals(".dds", StringComparison.OrdinalIgnoreCase)
                ? DdsIconDecoder.TryDecodeToBmp(rawBytes)
                : rawBytes;
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to read \"{xmlFilename}\"'s shop image: {ex.Message}");
            return null;
        }
    }
}
