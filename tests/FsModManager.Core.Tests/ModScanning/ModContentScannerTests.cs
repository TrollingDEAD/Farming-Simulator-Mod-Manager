using System.IO.Compression;
using FsModManager.Core.ModScanning;
using FsModManager.Core.Models;
using Xunit;

namespace FsModManager.Core.Tests.ModScanning;

public sealed class ModContentScannerTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fsmodmanager-content-tests-").FullName;
    private readonly ModContentScanner _scanner = new();

    [Fact]
    public async Task ScanContentAsync_VehicleStoreItem_ExtractsNameSpecsPriceCategory()
    {
        const string modDesc = """
            <modDesc descVersion="85">
                <storeItems>
                    <storeItem xmlFilename="vehicles/ideal/ideal.xml" />
                </storeItems>
            </modDesc>
            """;
        const string vehicleXml = """
            <vehicle type="combineDrivable">
                <storeData>
                    <name>IDEAL</name>
                    <specs>
                        <power>451</power>
                        <maxSpeed>40</maxSpeed>
                        <combination xmlFilename="vehicles/cutter/cutter.xml" />
                    </specs>
                    <price>484500</price>
                    <category>harvesters</category>
                </storeData>
            </vehicle>
            """;

        var zipPath = CreateModZip("VehicleMod", modDesc, ("vehicles/ideal/ideal.xml", vehicleXml));

        var results = await _scanner.ScanContentAsync(new ModFileInfo(zipPath, "VehicleMod", null, null, true, [], "VehicleMod", null));

        var detail = Assert.Single(results);
        Assert.Equal("vehicles/ideal/ideal.xml", detail.XmlFilename);
        Assert.Equal("IDEAL", detail.ObjectDisplayName);
        Assert.Equal(484500m, detail.Price);
        Assert.Equal("harvesters", detail.Category);
        Assert.Equal(StoreItemKind.Vehicle, detail.Kind);
        Assert.Contains(detail.Specs, s => s.Label == "Power" && s.Value == "451");
        Assert.Contains(detail.Specs, s => s.Label == "Max Speed" && s.Value == "40");
        Assert.DoesNotContain(detail.Specs, s => s.Value.Contains("cutter"));
        Assert.Empty(detail.Warnings);
    }

    [Fact]
    public async Task ScanContentAsync_ImplementStoreItem_ClassifiedAsImplementNotVehicle()
    {
        const string modDesc = """
            <modDesc descVersion="85">
                <storeItems><storeItem xmlFilename="vehicles/cutter/cutter.xml" /></storeItems>
            </modDesc>
            """;
        const string implementXml = """
            <vehicle type="cutter">
                <storeData>
                    <name>PowerFlow 40FT</name>
                    <specs><workingWidth>12</workingWidth></specs>
                    <price>74500</price>
                    <category>cutters</category>
                </storeData>
            </vehicle>
            """;

        var zipPath = CreateModZip("ImplementMod", modDesc, ("vehicles/cutter/cutter.xml", implementXml));

        var results = await _scanner.ScanContentAsync(new ModFileInfo(zipPath, "ImplementMod", null, null, true, [], "ImplementMod", null));

        var detail = Assert.Single(results);
        Assert.Equal(StoreItemKind.Implement, detail.Kind);
        Assert.Contains(detail.Specs, s => s.Label == "Working Width" && s.Value == "12");
    }

    [Fact]
    public async Task ScanContentAsync_PlaceableStoreItem_NoSpecsElement_ReturnsEmptySpecsNotFailure()
    {
        var originalCulture = System.Globalization.CultureInfo.CurrentUICulture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            const string modDesc = """
                <modDesc descVersion="85">
                    <storeItems><storeItem xmlFilename="XML/1.xml" /></storeItems>
                </modDesc>
                """;
            const string placeableXml = """
                <placeable type="simplePlaceable">
                    <storeData>
                        <name><en>Garbage 1</en><de>Muell 1</de></name>
                        <price>17</price>
                        <category>decoration</category>
                    </storeData>
                </placeable>
                """;

            var zipPath = CreateModZip("PlaceableMod", modDesc, ("XML/1.xml", placeableXml));

            var results = await _scanner.ScanContentAsync(new ModFileInfo(zipPath, "PlaceableMod", null, null, true, [], "PlaceableMod", null));

            var detail = Assert.Single(results);
            Assert.Equal(StoreItemKind.Placeable, detail.Kind);
            Assert.Equal("Garbage 1", detail.ObjectDisplayName);
            Assert.Equal(17m, detail.Price);
            Assert.Empty(detail.Specs);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public async Task ScanContentAsync_MissingReferencedFile_ReturnsPartialDetailWithWarning()
    {
        const string modDesc = """
            <modDesc descVersion="85">
                <storeItems><storeItem xmlFilename="does/not/exist.xml" /></storeItems>
            </modDesc>
            """;

        var zipPath = CreateModZip("BrokenRefMod", modDesc);

        var results = await _scanner.ScanContentAsync(new ModFileInfo(zipPath, "BrokenRefMod", null, null, true, [], "BrokenRefMod", null));

        var detail = Assert.Single(results);
        Assert.Equal("does/not/exist.xml", detail.XmlFilename);
        Assert.Null(detail.Price);
        Assert.NotEmpty(detail.Warnings);
    }

    [Fact]
    public async Task ScanContentAsync_MalformedReferencedXml_ReturnsPartialDetailWithWarning()
    {
        const string modDesc = """
            <modDesc descVersion="85">
                <storeItems><storeItem xmlFilename="broken.xml" /></storeItems>
            </modDesc>
            """;

        var zipPath = CreateModZip("MalformedMod", modDesc, ("broken.xml", "<vehicle><storeData><name>Oops</vehicle>"));

        var results = await _scanner.ScanContentAsync(new ModFileInfo(zipPath, "MalformedMod", null, null, true, [], "MalformedMod", null));

        var detail = Assert.Single(results);
        Assert.NotEmpty(detail.Warnings);
    }

    [Fact]
    public async Task ScanContentAsync_DollarDataReference_IsSkippedNotWarned()
    {
        const string modDesc = """
            <modDesc descVersion="85">
                <storeItems><storeItem xmlFilename="$data/vehicles/base/base.xml" /></storeItems>
            </modDesc>
            """;

        var zipPath = CreateModZip("BaseGameRefMod", modDesc);

        var results = await _scanner.ScanContentAsync(new ModFileInfo(zipPath, "BaseGameRefMod", null, null, true, [], "BaseGameRefMod", null));

        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanContentAsync_CachesResultAndInvalidateCacheForcesRescan()
    {
        const string modDesc = """
            <modDesc descVersion="85">
                <storeItems><storeItem xmlFilename="item.xml" /></storeItems>
            </modDesc>
            """;
        const string itemXml = "<placeable><storeData><name>First</name></storeData></placeable>";

        var zipPath = CreateModZip("CacheMod", modDesc, ("item.xml", itemXml));
        var mod = new ModFileInfo(zipPath, "CacheMod", null, null, true, [], "CacheMod", null);

        var first = await _scanner.ScanContentAsync(mod);
        Assert.Equal("First", first[0].ObjectDisplayName);

        // Rewrite the zip's referenced XML without invalidating — cached result should still win.
        RewriteEntry(zipPath, "item.xml", "<placeable><storeData><name>Second</name></storeData></placeable>");
        var stillCached = await _scanner.ScanContentAsync(mod);
        Assert.Equal("First", stillCached[0].ObjectDisplayName);

        _scanner.InvalidateCache(zipPath);
        var rescanned = await _scanner.ScanContentAsync(mod);
        Assert.Equal("Second", rescanned[0].ObjectDisplayName);
    }

    [Fact]
    public async Task ScanContentAsync_VehicleWithBrandMassFunctionsFillUnitsAndConfigs_ExtractsAllNewFields()
    {
        const string modDesc = """
            <modDesc descVersion="85">
                <storeItems><storeItem xmlFilename="vehicles/ideal/ideal.xml" /></storeItems>
            </modDesc>
            """;
        const string vehicleXml = """
            <vehicle type="combineDrivable">
                <storeData>
                    <name>IDEAL</name>
                    <price>484500</price>
                    <brand>FENDT</brand>
                    <functions>
                        <function>$l10n_function_harvesting</function>
                    </functions>
                </storeData>
                <base>
                    <components>
                        <component mass="11230"/>
                        <component mass="9400"/>
                    </components>
                </base>
                <fillUnitConfigurations>
                    <fillUnitConfiguration name="Small">
                        <fillUnits>
                            <fillUnit fillTypeCategories="combine" capacity="12500"/>
                            <fillUnit fillTypes="diesel" capacity="1000"/>
                        </fillUnits>
                    </fillUnitConfiguration>
                </fillUnitConfigurations>
                <designConfigurations>
                    <designConfiguration name="Fendt"/>
                    <designConfiguration name="Massey Ferguson"/>
                </designConfigurations>
            </vehicle>
            """;

        var zipPath = CreateModZip("BrandedVehicleMod", modDesc, ("vehicles/ideal/ideal.xml", vehicleXml));

        var results = await _scanner.ScanContentAsync(new ModFileInfo(zipPath, "BrandedVehicleMod", null, null, true, [], "BrandedVehicleMod", null));

        var detail = Assert.Single(results);
        Assert.Equal("FENDT", detail.Brand);
        Assert.Equal(20630m, detail.MassKg);
        Assert.Contains("$l10n_function_harvesting", detail.Functions);
        Assert.Contains(detail.RawFillCapacities, f => f.FillTypeName == "combine" && f.LiterCapacity == 12500m);
        Assert.Contains(detail.RawFillCapacities, f => f.FillTypeName == "diesel" && f.LiterCapacity == 1000m);
        Assert.Contains(detail.ConfigurationGroupCounts, g => g.GroupName == "Design" && g.OptionCount == 2);
        Assert.Null(detail.RequiredPowerHp);
    }

    [Fact]
    public async Task ScanContentAsync_PlaceableWithNoneOfTheNewFields_LeavesThemNullOrEmptyNotThrowing()
    {
        const string modDesc = """
            <modDesc descVersion="85">
                <storeItems><storeItem xmlFilename="XML/1.xml" /></storeItems>
            </modDesc>
            """;
        const string placeableXml = """
            <placeable type="simplePlaceable">
                <storeData>
                    <name>Garbage 1</name>
                    <price>17</price>
                </storeData>
            </placeable>
            """;

        var zipPath = CreateModZip("PlainPlaceableMod", modDesc, ("XML/1.xml", placeableXml));

        var results = await _scanner.ScanContentAsync(new ModFileInfo(zipPath, "PlainPlaceableMod", null, null, true, [], "PlainPlaceableMod", null));

        var detail = Assert.Single(results);
        Assert.Null(detail.Brand);
        Assert.Null(detail.MassKg);
        Assert.Null(detail.RequiredPowerHp);
        Assert.Empty(detail.Functions);
        Assert.Empty(detail.RawFillCapacities);
        Assert.Empty(detail.ConfigurationGroupCounts);
    }

    private string CreateModZip(string internalName, string modDescXml, params (string Path, string Content)[] referencedFiles)
    {
        var zipPath = Path.Combine(_tempDir, internalName + ".zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        var descEntry = archive.CreateEntry("modDesc.xml");
        using (var writer = new StreamWriter(descEntry.Open()))
        {
            writer.Write(modDescXml);
        }

        foreach (var (path, content) in referencedFiles)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return zipPath;
    }

    private static void RewriteEntry(string zipPath, string entryPath, string content)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        archive.GetEntry(entryPath)?.Delete();
        var entry = archive.CreateEntry(entryPath);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
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
