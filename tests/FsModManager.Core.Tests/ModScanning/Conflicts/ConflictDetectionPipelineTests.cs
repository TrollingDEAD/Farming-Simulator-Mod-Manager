using System.IO.Compression;
using FsModManager.Core.ModScanning.Conflicts;
using FsModManager.Core.ModScanning.Parsing;
using FsModManager.Core.Models;
using Xunit;

namespace FsModManager.Core.Tests.ModScanning.Conflicts;

public sealed class ConflictDetectionPipelineTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fsmodmanager-conflict-tests-").FullName;
    private readonly ModDescParser _parser = new();

    [Fact]
    public async Task Detect_DuplicateInternalNames_ReportsCritical()
    {
        var modA = await ParseZip("SameName", "<modDesc descVersion=\"85\"></modDesc>", zipNameOverride: "SameName_v1.zip");
        var modB = await ParseZip("SameName", "<modDesc descVersion=\"85\"></modDesc>", zipNameOverride: "SameName_v2.zip");

        var pipeline = ConflictDetectionPipeline.CreateDefault(includeLuaDetector: false);
        var conflicts = pipeline.Detect(new[] { modA, modB });

        Assert.Contains(conflicts, c => c.Severity == ConflictSeverity.Critical && c.Description.Contains("internal name"));
    }

    [Fact]
    public async Task Detect_DuplicateStoreItemXmlFilename_ReportsCritical()
    {
        const string xmlA = """
            <modDesc descVersion="85">
                <storeItems><storeItem xmlFilename="store/tool.xml" /></storeItems>
            </modDesc>
            """;
        const string xmlB = """
            <modDesc descVersion="85">
                <storeItems><storeItem xmlFilename="store/tool.xml" /></storeItems>
            </modDesc>
            """;
        var modA = await ParseZip("ModA", xmlA);
        var modB = await ParseZip("ModB", xmlB);

        var pipeline = ConflictDetectionPipeline.CreateDefault(includeLuaDetector: false);
        var conflicts = pipeline.Detect(new[] { modA, modB });

        Assert.Contains(conflicts, c => c.Severity == ConflictSeverity.Critical && c.Description.Contains("store/tool.xml"));
    }

    [Fact]
    public async Task Detect_DuplicateStoreItemXmlFilename_PopulatesObjectXmlFilenameOnBothSides()
    {
        const string xmlA = """
            <modDesc descVersion="85">
                <storeItems><storeItem xmlFilename="store/tool.xml" /></storeItems>
            </modDesc>
            """;
        const string xmlB = """
            <modDesc descVersion="85">
                <storeItems><storeItem xmlFilename="store/tool.xml" /></storeItems>
            </modDesc>
            """;
        var modA = await ParseZip("ModA", xmlA);
        var modB = await ParseZip("ModB", xmlB);

        var pipeline = ConflictDetectionPipeline.CreateDefault(includeLuaDetector: false);
        var conflicts = pipeline.Detect(new[] { modA, modB });

        var conflict = Assert.Single(conflicts, c => c.Severity == ConflictSeverity.Critical);
        Assert.Equal("store/tool.xml", conflict.ObjectAXmlFilename);
        Assert.Equal("store/tool.xml", conflict.ObjectBXmlFilename);
    }

    [Fact]
    public async Task Detect_DuplicateInternalName_LeavesObjectXmlFilenamesNull()
    {
        var modA = await ParseZip("SameName", "<modDesc descVersion=\"85\"></modDesc>", zipNameOverride: "SameName_v1.zip");
        var modB = await ParseZip("SameName", "<modDesc descVersion=\"85\"></modDesc>", zipNameOverride: "SameName_v2.zip");

        var pipeline = ConflictDetectionPipeline.CreateDefault(includeLuaDetector: false);
        var conflicts = pipeline.Detect(new[] { modA, modB });

        var conflict = Assert.Single(conflicts);
        Assert.Null(conflict.ObjectAXmlFilename);
        Assert.Null(conflict.ObjectBXmlFilename);
    }

    [Fact]
    public async Task GetAggregateSeverity_ModWithObjectAndModLevelConflicts_ReportsHighestSeverity()
    {
        const string xmlA = """
            <modDesc descVersion="85">
                <storeItems><storeItem xmlFilename="store/tool.xml" /></storeItems>
                <fillTypes><fillType name="MYFILL" /></fillTypes>
            </modDesc>
            """;
        const string xmlB = """
            <modDesc descVersion="85">
                <storeItems><storeItem xmlFilename="store/tool.xml" /></storeItems>
                <fillTypes><fillType name="MYFILL" /></fillTypes>
            </modDesc>
            """;
        var modA = await ParseZip("ModA", xmlA);
        var modB = await ParseZip("ModB", xmlB);

        var pipeline = ConflictDetectionPipeline.CreateDefault(includeLuaDetector: false);
        var conflicts = pipeline.Detect(new[] { modA, modB });

        // Both a Critical (storeItem, object-level) and a Likely (custom type, mod-level) conflict exist for ModA.
        Assert.Equal(ConflictSeverity.Critical, conflicts.GetAggregateSeverity("ModA"));
    }

    [Fact]
    public async Task GetAggregateSeverity_ModWithNoConflicts_ReturnsNull()
    {
        var modA = await ParseZip("ModA", "<modDesc descVersion=\"85\"></modDesc>");

        var pipeline = ConflictDetectionPipeline.CreateDefault(includeLuaDetector: false);
        var conflicts = pipeline.Detect(new[] { modA });

        Assert.Null(conflicts.GetAggregateSeverity("ModA"));
    }

    [Fact]
    public async Task Detect_DuplicateCustomType_ReportsLikely()
    {
        const string xmlA = """
            <modDesc descVersion="85">
                <fillTypes><fillType name="MYFILL" /></fillTypes>
            </modDesc>
            """;
        const string xmlB = """
            <modDesc descVersion="85">
                <fillTypes><fillType name="MYFILL" /></fillTypes>
            </modDesc>
            """;
        var modA = await ParseZip("ModA", xmlA);
        var modB = await ParseZip("ModB", xmlB);

        var pipeline = ConflictDetectionPipeline.CreateDefault(includeLuaDetector: false);
        var conflicts = pipeline.Detect(new[] { modA, modB });

        Assert.Contains(conflicts, c => c.Severity == ConflictSeverity.Likely && c.Description.Contains("MYFILL"));
    }

    [Fact]
    public async Task Detect_OverlappingGlobalLuaFunctions_ReportsPossible()
    {
        var modA = await ParseZipWithLua("ModA", "<modDesc descVersion=\"85\"></modDesc>", "function DoTheThing(self)\n  return 1\nend");
        var modB = await ParseZipWithLua("ModB", "<modDesc descVersion=\"85\"></modDesc>", "function DoTheThing(self)\n  return 2\nend");

        var pipeline = ConflictDetectionPipeline.CreateDefault(includeLuaDetector: true);
        var conflicts = pipeline.Detect(new[] { modA, modB });

        Assert.Contains(conflicts, c => c.Severity == ConflictSeverity.Possible && c.Description.Contains("DoTheThing"));
    }

    [Fact]
    public async Task Detect_IndentedLuaFunction_IsNotFlagged()
    {
        var modA = await ParseZipWithLua("ModA", "<modDesc descVersion=\"85\"></modDesc>", "local function helper()\n  function Nested()\n  end\nend");
        var modB = await ParseZipWithLua("ModB", "<modDesc descVersion=\"85\"></modDesc>", "local function helper()\n  function Nested()\n  end\nend");

        var pipeline = ConflictDetectionPipeline.CreateDefault(includeLuaDetector: true);
        var conflicts = pipeline.Detect(new[] { modA, modB });

        Assert.DoesNotContain(conflicts, c => c.Description.Contains("Nested"));
    }

    [Fact]
    public async Task Detect_LuaDetectorDisabled_SkipsLuaCollisions()
    {
        var modA = await ParseZipWithLua("ModA", "<modDesc descVersion=\"85\"></modDesc>", "function DoTheThing(self)\nend");
        var modB = await ParseZipWithLua("ModB", "<modDesc descVersion=\"85\"></modDesc>", "function DoTheThing(self)\nend");

        var pipeline = ConflictDetectionPipeline.CreateDefault(includeLuaDetector: false);
        var conflicts = pipeline.Detect(new[] { modA, modB });

        Assert.DoesNotContain(conflicts, c => c.Description.Contains("DoTheThing"));
    }

    [Fact]
    public async Task Detect_NoOverlap_ReturnsEmpty()
    {
        var modA = await ParseZip("ModA", "<modDesc descVersion=\"85\"><storeItems><storeItem xmlFilename=\"store/a.xml\" /></storeItems></modDesc>");
        var modB = await ParseZip("ModB", "<modDesc descVersion=\"85\"><storeItems><storeItem xmlFilename=\"store/b.xml\" /></storeItems></modDesc>");

        var pipeline = ConflictDetectionPipeline.CreateDefault(includeLuaDetector: false);
        var conflicts = pipeline.Detect(new[] { modA, modB });

        Assert.Empty(conflicts);
    }

    private async Task<ModMetadata> ParseZip(string internalName, string modDescXml, string? zipNameOverride = null)
    {
        var zipPath = Path.Combine(_tempDir, zipNameOverride ?? internalName + ".zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("modDesc.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(modDescXml);
        }

        return await _parser.ParseAsync(zipPath);
    }

    private async Task<ModMetadata> ParseZipWithLua(string internalName, string modDescXml, string luaContent)
    {
        var zipPath = Path.Combine(_tempDir, internalName + ".zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var descEntry = archive.CreateEntry("modDesc.xml");
            using (var writer = new StreamWriter(descEntry.Open()))
            {
                writer.Write(modDescXml);
            }

            var luaEntry = archive.CreateEntry("scripts/main.lua");
            using var luaWriter = new StreamWriter(luaEntry.Open());
            luaWriter.Write(luaContent);
        }

        return await _parser.ParseAsync(zipPath);
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
