using System.Globalization;
using System.IO.Compression;
using FsModManager.Core.ModScanning.Parsing;
using Xunit;

namespace FsModManager.Core.Tests.Parsing;

public sealed class ModDescParserTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fsmodmanager-tests-").FullName;
    private readonly ModDescParser _parser = new();

    [Fact]
    public async Task ParseAsync_ValidModDesc_ReturnsExpectedMetadata()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <author>Some Author</author>
                <version>1.2.3</version>
                <multiplayer supported="true" />
                <storeItems>
                    <storeItem xmlFilename="store/myItem.xml" />
                    <storeItem xmlFilename="store/otherItem.xml" />
                </storeItems>
            </modDesc>
            """;
        var zipPath = CreateModZip("CoolTractorMod", xml);

        var result = await _parser.ParseAsync(zipPath);

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
        Assert.Equal("CoolTractorMod", result.InternalName);
        Assert.Equal("85", result.DescVersion);
        Assert.True(result.DescVersionParsed);
        Assert.Equal("1.2.3", result.Version);
        Assert.Equal("Some Author", result.Author);
        Assert.True(result.MultiplayerSupported);
        Assert.Equal(new[] { "store/myItem.xml", "store/otherItem.xml" }, result.StoreItemIds);
        Assert.Contains("storeItems", result.TopLevelElementNames);
        Assert.NotNull(result.FileHash);
    }

    [Fact]
    public async Task ParseAsync_VersionedFilename_StripsVersionSuffixFromInternalName()
    {
        var zipPath = CreateModZip("CoolTractorMod_v1.2.3", "<modDesc descVersion=\"85\"></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.Equal("CoolTractorMod", result.InternalName);
    }

    [Fact]
    public async Task ParseAsync_StoreItemsWithNameAttribute_CapturesBothXmlFilenameAndName()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <storeItems>
                    <storeItem xmlFilename="store/myItem.xml" name="My Item" />
                </storeItems>
            </modDesc>
            """;
        var zipPath = CreateModZip("StoreItemMod", xml);

        var result = await _parser.ParseAsync(zipPath);

        var storeItem = Assert.Single(result.StoreItems);
        Assert.Equal("store/myItem.xml", storeItem.XmlFilename);
        Assert.Equal("My Item", storeItem.Name);
    }

    [Fact]
    public async Task ParseAsync_CustomTypes_AreExtracted()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <fillTypes>
                    <fillType name="MYFILLTYPE" />
                </fillTypes>
                <fruitTypes>
                    <fruitType name="MYFRUIT" />
                </fruitTypes>
                <vehicleTypes>
                    <vehicleType name="myVehicle" />
                </vehicleTypes>
            </modDesc>
            """;
        var zipPath = CreateModZip("CustomTypeMod", xml);

        var result = await _parser.ParseAsync(zipPath);

        Assert.Contains(result.CustomTypes, t => t.Kind == FsModManager.Core.Models.ModCustomTypeKind.FillType && t.Name == "MYFILLTYPE");
        Assert.Contains(result.CustomTypes, t => t.Kind == FsModManager.Core.Models.ModCustomTypeKind.FruitType && t.Name == "MYFRUIT");
        Assert.Contains(result.CustomTypes, t => t.Kind == FsModManager.Core.Models.ModCustomTypeKind.VehicleType && t.Name == "myVehicle");
    }

    [Fact]
    public async Task ParseAsync_DependenciesBlockPresent_IsParsed()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <dependencies>
                    <dependency name="SomeOtherMod" />
                    <dependency>AnotherMod</dependency>
                </dependencies>
            </modDesc>
            """;
        var zipPath = CreateModZip("DependentMod", xml);

        var result = await _parser.ParseAsync(zipPath);

        Assert.Equal(new[] { "SomeOtherMod", "AnotherMod" }, result.Dependencies);
    }

    [Fact]
    public async Task ParseAsync_NoDependenciesBlock_ReturnsEmptyListWithoutWarning()
    {
        var zipPath = CreateModZip("NoDependenciesMod", "<modDesc descVersion=\"85\"><version>1.0</version><author>A</author></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.True(result.IsValid);
        Assert.Empty(result.Dependencies);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("dependencies", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ParseAsync_UnparsableDescVersion_ReportsDescVersionParsedFalse()
    {
        var zipPath = CreateModZip("BadDescVersionMod", "<modDesc descVersion=\"not-a-number\"></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.False(result.DescVersionParsed);
        Assert.Contains(result.Warnings, w => w.Contains("not a valid integer"));
    }

    [Fact]
    public async Task ParseAsync_MissingModDescXml_ReturnsInvalidWithWarning()
    {
        var zipPath = Path.Combine(_tempDir, "NoModDesc.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("readme.txt");
        }

        var result = await _parser.ParseAsync(zipPath);

        Assert.False(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("modDesc.xml not found"));
    }

    [Fact]
    public async Task ParseAsync_MalformedXml_ReturnsInvalidWithWarning()
    {
        var zipPath = CreateModZip("BrokenMod", "<modDesc descVersion=\"85\"><version>1.0</modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.False(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("not well-formed"));
    }

    [Fact]
    public async Task ParseAsync_MissingOptionalFields_StillValidButWarns()
    {
        var zipPath = CreateModZip("MinimalMod", "<modDesc descVersion=\"85\"></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
        Assert.Null(result.Version);
        Assert.Null(result.Author);
    }

    [Fact]
    public async Task ParseAsync_MissingFile_ReturnsInvalid()
    {
        var result = await _parser.ParseAsync(Path.Combine(_tempDir, "does-not-exist.zip"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("File not found"));
    }

    [Fact]
    public async Task ParseAsync_MultiLanguageTitle_PicksCurrentUiCultureEntry()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <title>
                    <en>Real Name</en>
                    <de>Echter Name</de>
                </title>
            </modDesc>
            """;
        var zipPath = CreateModZip("TitleMod", xml);

        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var result = await _parser.ParseAsync(zipPath);
            Assert.Equal("Echter Name", result.DisplayTitle);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public async Task ParseAsync_MultiLanguageTitle_NoMatchingCulture_FallsBackToEnglish()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <title>
                    <en>Real Name</en>
                    <fr>Nom Reel</fr>
                </title>
            </modDesc>
            """;
        var zipPath = CreateModZip("TitleMod", xml);

        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var result = await _parser.ParseAsync(zipPath);
            Assert.Equal("Real Name", result.DisplayTitle);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public async Task ParseAsync_NoTitleElement_FallsBackToInternalName()
    {
        var zipPath = CreateModZip("NoTitleMod", "<modDesc descVersion=\"85\"></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.Equal("NoTitleMod", result.DisplayTitle);
    }

    [Fact]
    public async Task ParseAsync_TitleWithSingleEntryNoCultureMatch_FallsBackToThatSingleEntry()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <title>
                    <fr>Nom Reel</fr>
                </title>
            </modDesc>
            """;
        var zipPath = CreateModZip("SingleTitleMod", xml);

        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var result = await _parser.ParseAsync(zipPath);
            Assert.Equal("Nom Reel", result.DisplayTitle);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public async Task ParseAsync_VersionNeverConcatenatedIntoDisplayTitleOrInternalName()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <title><en>Cool Tractor</en></title>
                <version>2.5.0</version>
            </modDesc>
            """;
        var zipPath = CreateModZip("VersionMod", xml);

        var result = await _parser.ParseAsync(zipPath);

        Assert.Equal("2.5.0", result.Version);
        Assert.DoesNotContain("2.5.0", result.DisplayTitle);
        Assert.DoesNotContain("2.5.0", result.InternalName);
    }

    [Fact]
    public async Task ParseAsync_NoIconFilename_ReturnsNullIconImageData()
    {
        var zipPath = CreateModZip("NoIconMod", "<modDesc descVersion=\"85\"></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.Null(result.IconImageData);
    }

    [Fact]
    public async Task ParseAsync_IconFilenameReferencesMissingFile_ReturnsNullWithoutThrowing()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <iconFilename>missing_icon.dds</iconFilename>
            </modDesc>
            """;
        var zipPath = CreateModZip("MissingIconMod", xml);

        var result = await _parser.ParseAsync(zipPath);

        Assert.True(result.IsValid);
        Assert.Null(result.IconImageData);
    }

    [Fact]
    public async Task ParseAsync_CorruptIconFile_ReturnsNullWithoutThrowing()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <iconFilename>icon.dds</iconFilename>
            </modDesc>
            """;
        var zipPath = Path.Combine(_tempDir, "CorruptIconMod.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("modDesc.xml").Open()))
            {
                writer.Write(xml);
            }

            using var iconWriter = new BinaryWriter(archive.CreateEntry("icon.dds").Open());
            iconWriter.Write(new byte[] { 0x01, 0x02, 0x03, 0x04 }); // not a valid DDS header
        }

        var result = await _parser.ParseAsync(zipPath);

        Assert.True(result.IsValid);
        Assert.Null(result.IconImageData);
    }

    [Fact]
    public async Task ParseAsync_MultipleStoreItemsWithCategories_CollectsDistinctCategoriesFromLinkedFiles()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <storeItems>
                    <storeItem xmlFilename="store/item1.xml" />
                    <storeItem xmlFilename="store/item2.xml" />
                    <storeItem xmlFilename="store/item3.xml" />
                </storeItems>
            </modDesc>
            """;
        const string item1 = """<placeable><storeData><category>decoration</category></storeData></placeable>""";
        const string item2 = """<vehicle><storeData><category>tractorsMedium</category></storeData></vehicle>""";
        // Same category as item1, different casing - should not produce a duplicate entry.
        const string item3 = """<placeable><storeData><category>Decoration</category></storeData></placeable>""";
        var zipPath = CreateModZip("MultiCategoryMod", xml, new Dictionary<string, string>
        {
            ["store/item1.xml"] = item1,
            ["store/item2.xml"] = item2,
            ["store/item3.xml"] = item3,
        });

        var result = await _parser.ParseAsync(zipPath);

        Assert.Equal(2, result.Categories.Count);
        Assert.Contains(result.Categories, c => string.Equals(c, "decoration", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Categories, c => string.Equals(c, "tractorsMedium", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ParseAsync_StoreItemCategoryAttribute_IsUsedDirectlyWithoutOpeningLinkedFile()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <storeItems>
                    <storeItem xmlFilename="store/item1.xml" category="forestry" />
                </storeItems>
            </modDesc>
            """;
        var zipPath = CreateModZip("CategoryAttributeMod", xml);

        var result = await _parser.ParseAsync(zipPath);

        Assert.Equal(new[] { "forestry" }, result.Categories);
    }

    [Fact]
    public async Task ParseAsync_NoStoreItems_ReturnsEmptyCategoriesNotNull()
    {
        var zipPath = CreateModZip("NoStoreItemsMod", "<modDesc descVersion=\"85\"></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.NotNull(result.Categories);
        Assert.Empty(result.Categories);
    }

    [Fact]
    public async Task ParseAsync_StoreItemWithoutResolvableCategory_ReturnsEmptyCategoriesNotError()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <storeItems>
                    <storeItem xmlFilename="$data/vehicles/base/tractor.xml" />
                    <storeItem xmlFilename="store/missingItem.xml" />
                </storeItems>
            </modDesc>
            """;
        var zipPath = CreateModZip("UnresolvableCategoryMod", xml);

        var result = await _parser.ParseAsync(zipPath);

        Assert.True(result.IsValid);
        Assert.Empty(result.Categories);
    }

    [Fact]
    public async Task ParseAsync_MultiplayerSupportedTrue_ParsesAsTrue()
    {
        var zipPath = CreateModZip("MpTrueMod", "<modDesc descVersion=\"85\"><multiplayer supported=\"true\" /></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.True(result.MultiplayerSupported);
    }

    [Fact]
    public async Task ParseAsync_MultiplayerSupportedFalse_ParsesAsFalse()
    {
        var zipPath = CreateModZip("MpFalseMod", "<modDesc descVersion=\"85\"><multiplayer supported=\"false\" /></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.False(result.MultiplayerSupported!.Value);
    }

    [Fact]
    public async Task ParseAsync_NoMultiplayerElement_MultiplayerSupportedIsNull()
    {
        var zipPath = CreateModZip("NoMpMod", "<modDesc descVersion=\"85\"></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.Null(result.MultiplayerSupported);
    }

    [Fact]
    public async Task ParseAsync_UnparsableMultiplayerSupportedAttribute_ReturnsNullWithWarning()
    {
        var zipPath = CreateModZip("BadMpMod", "<modDesc descVersion=\"85\"><multiplayer supported=\"maybe\" /></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.Null(result.MultiplayerSupported);
        Assert.Contains(result.Warnings, w => w.Contains("boolean"));
    }

    [Fact]
    public async Task ParseAsync_AlwaysReturnsCrossplayStatusUnknown()
    {
        var zipPath = CreateModZip("CrossplayMod", "<modDesc descVersion=\"85\"></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.Equal(FsModManager.Core.Models.CrossplayStatus.Unknown, result.CrossplayStatus);
    }

    [Fact]
    public async Task ParseAsync_MultiLanguageDescription_PicksMatchingLanguageWithEnglishFallback()
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture; // no "en" match -> falls back to "en"
        try
        {
            const string xml = """
                <?xml version="1.0" encoding="utf-8"?>
                <modDesc descVersion="85">
                    <description>
                        <en>English description</en>
                        <de>Deutsche Beschreibung</de>
                    </description>
                </modDesc>
                """;
            var zipPath = CreateModZip("DescMod", xml);

            var result = await _parser.ParseAsync(zipPath);

            Assert.Equal("English description", result.Description);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public async Task ParseAsync_NoDescriptionElement_DescriptionIsNull()
    {
        var zipPath = CreateModZip("NoDescMod", "<modDesc descVersion=\"85\"></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.Null(result.Description);
    }

    [Fact]
    public async Task ParseAsync_SetsFileSizeAndLastModifiedFromTheZipFile()
    {
        var zipPath = CreateModZip("SizeMod", "<modDesc descVersion=\"85\"></modDesc>");
        var expectedSize = new FileInfo(zipPath).Length;
        var expectedLastModified = File.GetLastWriteTimeUtc(zipPath);

        var result = await _parser.ParseAsync(zipPath);

        Assert.Equal(expectedSize, result.FileSizeBytes);
        Assert.Equal(expectedLastModified, result.LastModifiedUtc);
    }

    [Fact]
    public async Task ParseAsync_ModDescWithMapsBlock_ClassifiesAsMap()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <modDesc descVersion="85">
                <maps>
                    <map id="MyMap" configFilename="maps/map.xml" />
                </maps>
            </modDesc>
            """;
        var zipPath = CreateModZip("MapMod", xml);

        var result = await _parser.ParseAsync(zipPath);

        Assert.Equal(FsModManager.Core.Models.ModKind.Map, result.ModKind);
    }

    [Fact]
    public async Task ParseAsync_NoMapsBlock_ClassifiesAsUnknownUntilContentIsScanned()
    {
        var zipPath = CreateModZip("VehicleOnlyMod", "<modDesc descVersion=\"85\"></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.Equal(FsModManager.Core.Models.ModKind.Unknown, result.ModKind);
    }

    [Theory]
    [InlineData(new[] { FsModManager.Core.Models.StoreItemKind.Vehicle }, FsModManager.Core.Models.ModKind.VehiclePack)]
    [InlineData(new[] { FsModManager.Core.Models.StoreItemKind.Placeable }, FsModManager.Core.Models.ModKind.PlaceablePack)]
    [InlineData(new FsModManager.Core.Models.StoreItemKind[0], FsModManager.Core.Models.ModKind.ScriptOnly)]
    [InlineData(new[] { FsModManager.Core.Models.StoreItemKind.Vehicle, FsModManager.Core.Models.StoreItemKind.Placeable }, FsModManager.Core.Models.ModKind.Mixed)]
    public void ModKindClassifier_ClassifiesFromContentKindsWhenNoMapSignal(
        FsModManager.Core.Models.StoreItemKind[] contentKinds,
        FsModManager.Core.Models.ModKind expected)
    {
        var actual = FsModManager.Core.Models.ModKindClassifier.Classify(hasMapSignal: false, contentKinds);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ModKindClassifier_MapSignalTakesPriorityOverContentKinds()
    {
        var actual = FsModManager.Core.Models.ModKindClassifier.Classify(
            hasMapSignal: true,
            new[] { FsModManager.Core.Models.StoreItemKind.Vehicle });

        Assert.Equal(FsModManager.Core.Models.ModKind.Map, actual);
    }

    [Fact]
    public async Task ParseAsync_WithInvalidFilenameCharacters_FlagsIsFilenameValidFalseAndPopulatesWarning()
    {
        var zipPath = CreateModZip("FS25 Some Mod (1)", "<modDesc descVersion=\"85\"></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.False(result.IsFilenameValid);
        Assert.NotNull(result.InvalidFilenameReason);
        Assert.Contains("Invalid mod filename", result.Warnings[0]);
    }

    [Fact]
    public async Task ParseAsync_WithValidFilename_FlagsIsFilenameValidTrue()
    {
        var zipPath = CreateModZip("FS25_CoolMod_v2", "<modDesc descVersion=\"85\"></modDesc>");

        var result = await _parser.ParseAsync(zipPath);

        Assert.True(result.IsFilenameValid);
        Assert.Null(result.InvalidFilenameReason);
    }

    private string CreateModZip(string internalName, string modDescXml)
    {
        var zipPath = Path.Combine(_tempDir, internalName + ".zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("modDesc.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(modDescXml);
        return zipPath;
    }

    /// <summary>Like <see cref="CreateModZip"/> but also writes extra zip entries (e.g. linked storeItem XML files).</summary>
    private string CreateModZip(string internalName, string modDescXml, IDictionary<string, string> extraEntries)
    {
        var zipPath = Path.Combine(_tempDir, internalName + ".zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("modDesc.xml").Open()))
        {
            writer.Write(modDescXml);
        }

        foreach (var (path, contents) in extraEntries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open());
            writer.Write(contents);
        }

        return zipPath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
