using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using FsModManager.Core.Hashing;
using FsModManager.Core.ModScanning.Conflicts;
using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning.Parsing;

/// <inheritdoc cref="IModDescParser"/>
public sealed partial class ModDescParser : IModDescParser
{
    // Matches a trailing version suffix like "_v1.2.3", "-1.2", " v2" so the internal name used
    // for identity/conflict detection lines up even when mods are distributed with a version tag.
    [GeneratedRegex(@"[-_\s]v?\d+(?:[._]\d+){0,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffixRegex();

    public async Task<ModMetadata> ParseAsync(string zipFilePath, CancellationToken cancellationToken = default)
    {
        var internalName = DeriveInternalName(zipFilePath);
        var warnings = new List<string>();

        var fileName = Path.GetFileName(zipFilePath);
        var filenameValidation = ModFilenameValidator.Validate(fileName);
        if (!filenameValidation.IsValid && filenameValidation.Reason is not null)
        {
            warnings.Add($"Invalid mod filename '{fileName}': {filenameValidation.Reason}");
        }

        if (!File.Exists(zipFilePath))
        {
            warnings.Add($"File not found: {zipFilePath}");
            return ModMetadata.Invalid(internalName, warnings, zipFilePath);
        }

        var fileSizeBytes = new FileInfo(zipFilePath).Length;
        var lastModifiedUtc = File.GetLastWriteTimeUtc(zipFilePath);

        string? fileHash;
        try
        {
            fileHash = await FileHashing.ComputeSha256Async(zipFilePath, cancellationToken);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to hash file: {ex.Message}");
            fileHash = null;
        }

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(zipFilePath);
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not open zip archive: {ex.Message}");
            return ModMetadata.Invalid(internalName, warnings, zipFilePath) with { FileHash = fileHash, FileSizeBytes = fileSizeBytes, LastModifiedUtc = lastModifiedUtc };
        }

        using (archive)
        {
            // modDesc.xml is expected at the archive root, but be lenient about casing/subfolders.
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, "modDesc.xml", StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                warnings.Add("modDesc.xml not found in archive.");
                return ModMetadata.Invalid(internalName, warnings, zipFilePath) with { FileHash = fileHash, FileSizeBytes = fileSizeBytes, LastModifiedUtc = lastModifiedUtc };
            }

            XDocument doc;
            try
            {
                await using var stream = entry.Open();
                doc = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
            }
            catch (XmlException ex)
            {
                warnings.Add($"modDesc.xml is not well-formed XML: {ex.Message}");
                return ModMetadata.Invalid(internalName, warnings, zipFilePath) with { FileHash = fileHash, FileSizeBytes = fileSizeBytes, LastModifiedUtc = lastModifiedUtc };
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to read modDesc.xml: {ex.Message}");
                return ModMetadata.Invalid(internalName, warnings, zipFilePath) with { FileHash = fileHash, FileSizeBytes = fileSizeBytes, LastModifiedUtc = lastModifiedUtc };
            }

            var root = doc.Root;
            if (root is null || !string.Equals(root.Name.LocalName, "modDesc", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("modDesc.xml has no <modDesc> root element.");
                return ModMetadata.Invalid(internalName, warnings, zipFilePath) with { FileHash = fileHash, FileSizeBytes = fileSizeBytes, LastModifiedUtc = lastModifiedUtc };
            }

            var descVersion = root.Attribute("descVersion")?.Value;
            var descVersionParsed = false;
            if (descVersion is null)
            {
                warnings.Add("Missing descVersion attribute on <modDesc>.");
            }
            else if (!int.TryParse(descVersion, out _))
            {
                warnings.Add($"descVersion attribute \"{descVersion}\" is not a valid integer.");
            }
            else
            {
                descVersionParsed = true;
            }

            var version = root.Element("version")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(version))
            {
                warnings.Add("Missing or empty <version> element.");
                version = null;
            }

            var author = root.Element("author")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(author))
            {
                warnings.Add("Missing or empty <author> element.");
                author = null;
            }

            var displayTitle = ParseDisplayTitle(root) ?? internalName;
            var iconImageData = TryExtractIconImageData(archive, root);
            var description = LocalizedTextParser.Parse(root.Element("description"));

            // Confirmed reliable signal against real installed FS25 map mods: a top-level
            // <maps><map .../></maps> block. Content hasn't been scanned yet at this point, so the
            // result here can only ever be Map or Unknown (refined further once storeItem kinds
            // are known - see ModRowViewModel).
            var hasMapSignal = root.Element("maps")?.Elements("map").Any() == true;
            var modKind = ModKindClassifier.Classify(hasMapSignal, contentKinds: null);

            bool? multiplayerSupported = null;
            var multiplayerElement = root.Element("multiplayer");
            var supportedAttr = multiplayerElement?.Attribute("supported")?.Value;
            if (supportedAttr is not null)
            {
                if (bool.TryParse(supportedAttr, out var parsed))
                {
                    multiplayerSupported = parsed;
                }
                else
                {
                    warnings.Add($"Could not parse <multiplayer supported=\"{supportedAttr}\"/> as a boolean.");
                }
            }

            var storeItemElements = root.Element("storeItems")?.Elements("storeItem").ToList() ?? new List<XElement>();
            var storeItems = storeItemElements
                .Select(e => new ModStoreItem(e.Attribute("xmlFilename")?.Value, e.Attribute("name")?.Value))
                .ToList();
            var storeItemIds = storeItemElements
                .Select(GetStoreItemId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList();

            var categories = storeItemElements
                .Select(e => TryGetStoreItemCategory(archive, e))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var customTypes = new List<ModCustomType>();
            customTypes.AddRange(GetCustomTypeNames(root, "fillType").Select(name => new ModCustomType(ModCustomTypeKind.FillType, name)));
            customTypes.AddRange(GetCustomTypeNames(root, "fruitType").Select(name => new ModCustomType(ModCustomTypeKind.FruitType, name)));
            customTypes.AddRange(GetCustomTypeNames(root, "vehicleType").Select(name => new ModCustomType(ModCustomTypeKind.VehicleType, name)));

            // <specializations><specialization name="..." className="..."/></specializations> — two
            // mods declaring the same name is a real, hard load-time error in FS25 (see
            // DuplicateSpecializationNameDetector), not a subtle silent-override bug.
            var specializationNames = root.Element("specializations")
                ?.Elements("specialization")
                .Select(e => e.Attribute("name")?.Value)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToList() ?? new List<string>();

            var sharedDataFileDefinitions = SharedDataFileDefinitions.Load();
            var sharedDataFileMatches = archive.Entries
                .Select(e => e.Name)
                .Where(entryFileName => !string.IsNullOrEmpty(entryFileName))
                .Where(entryFileName => sharedDataFileDefinitions.Any(d => string.Equals(d.FileName, entryFileName, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Non-standard, third-party-only block. Its absence is completely normal, never a warning.
            var dependencies = root.Element("dependencies")
                ?.Elements()
                .Select(e => e.Attribute("name")?.Value ?? e.Attribute("modName")?.Value ?? e.Value?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToList() ?? new List<string>();

            var topLevelElementNames = root.Elements()
                .Select(e => e.Name.LocalName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ModMetadata
            {
                InternalName = internalName,
                DescVersion = descVersion,
                DescVersionParsed = descVersionParsed,
                Version = version,
                DisplayTitle = displayTitle,
                IconImageData = iconImageData,
                Description = description,
                FileSizeBytes = fileSizeBytes,
                LastModifiedUtc = lastModifiedUtc,
                ModKind = modKind,
                Author = author,
                MultiplayerSupported = multiplayerSupported,
                // See the remarks on ModMetadata.CrossplayStatus - no confirmed modDesc.xml source exists for this yet.
                CrossplayStatus = CrossplayStatus.Unknown,
                Categories = categories,
                StoreItemIds = storeItemIds,
                StoreItems = storeItems,
                CustomTypes = customTypes,
                Dependencies = dependencies,
                SpecializationNames = specializationNames,
                SharedDataFileMatches = sharedDataFileMatches,
                TopLevelElementNames = topLevelElementNames,
                SourceFileName = zipFilePath,
                FileHash = fileHash,
                IsValid = true,
                IsFilenameValid = filenameValidation.IsValid,
                InvalidFilenameReason = filenameValidation.Reason,
                Warnings = warnings,
            };
        }
    }

    /// <summary>
    /// FS mods declare custom types either wrapped (e.g. &lt;fillTypes&gt;&lt;fillType/&gt;...) or,
    /// less commonly, as bare descendants — Descendants() finds either shape from the root down.
    /// </summary>
    private static IEnumerable<string> GetCustomTypeNames(XElement root, string elementName) =>
        root.Descendants(elementName)
            .Select(e => e.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// FS mods identify store items either by an "xmlFilename" attribute (the common convention)
    /// or, less commonly, an explicit "id"/"name" attribute. We fall back through each.
    /// </summary>
    private static string? GetStoreItemId(XElement storeItem) =>
        storeItem.Attribute("xmlFilename")?.Value
        ?? storeItem.Attribute("id")?.Value
        ?? storeItem.Attribute("name")?.Value;

    /// <summary>
    /// Real modDesc.xml files never carry a "category" attribute directly on &lt;storeItem&gt;
    /// (confirmed by inspecting real installed FS25 mods) - the category actually lives as a
    /// &lt;category&gt; element inside &lt;storeData&gt; of the XML file the storeItem's xmlFilename
    /// points to. We still check the attribute first for leniency/forward-compatibility (the
    /// schema isn't officially documented), then fall back to opening the referenced entry.
    /// References into the base game's "$data/..." folder aren't inside the mod zip and can't be
    /// resolved - that's normal, not a warning.
    /// </summary>
    private static string? TryGetStoreItemCategory(ZipArchive archive, XElement storeItemElement)
    {
        var categoryAttr = storeItemElement.Attribute("category")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(categoryAttr))
        {
            return categoryAttr;
        }

        var xmlFilename = storeItemElement.Attribute("xmlFilename")?.Value;
        if (string.IsNullOrWhiteSpace(xmlFilename) || xmlFilename.StartsWith("$data/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var normalized = xmlFilename.Replace('\\', '/').TrimStart('.', '/');
            var itemEntry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName.Replace('\\', '/').TrimStart('.', '/'), normalized, StringComparison.OrdinalIgnoreCase));
            if (itemEntry is null)
            {
                return null;
            }

            using var stream = itemEntry.Open();
            var itemDoc = XDocument.Load(stream);
            var category = itemDoc.Root?.Element("storeData")?.Element("category")?.Value?.Trim();
            return string.IsNullOrWhiteSpace(category) ? null : category;
        }
        catch
        {
            return null;
        }
    }

    private static string DeriveInternalName(string zipFilePath)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(zipFilePath);
        var match = VersionSuffixRegex().Match(nameWithoutExtension);
        return match.Success ? nameWithoutExtension[..match.Index] : nameWithoutExtension;
    }

    /// <summary>
    /// Picks the best &lt;title&gt; entry for the current UI culture: exact two-letter language
    /// match, then "en", then whatever single entry exists, else null (caller falls back to the
    /// internal name). Also handles a bare &lt;title&gt;SomeName&lt;/title&gt; with no language
    /// children, which some mods use instead of the dictionary form.
    /// </summary>
    private static string? ParseDisplayTitle(XElement root) => LocalizedTextParser.Parse(root.Element("title"));

    /// <summary>
    /// Reads &lt;iconFilename&gt;, extracts that file from the zip, and decodes it (DDS is the
    /// common FS icon format) to BMP bytes. Returns null for any missing/unfound/undecodable
    /// icon — this is a "nice to have" and must never fail the whole parse.
    /// </summary>
    private static byte[]? TryExtractIconImageData(ZipArchive archive, XElement root)
    {
        var iconFilename = root.Element("iconFilename")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(iconFilename))
        {
            return null;
        }

        try
        {
            // iconFilename is usually a root-relative path like "store_icon.dds"; be lenient
            // about leading "./" and backslashes some mods use.
            var normalized = iconFilename.Replace('\\', '/').TrimStart('.', '/');
            var iconEntry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName.Replace('\\', '/').TrimStart('.', '/'), normalized, StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(e => string.Equals(e.Name, Path.GetFileName(normalized), StringComparison.OrdinalIgnoreCase));

            if (iconEntry is null)
            {
                return null;
            }

            using var entryStream = iconEntry.Open();
            using var memoryStream = new MemoryStream();
            entryStream.CopyTo(memoryStream);
            var ddsBytes = memoryStream.ToArray();

            return DdsIconDecoder.TryDecodeToBmp(ddsBytes);
        }
        catch
        {
            return null;
        }
    }
}
