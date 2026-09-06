using System.IO.Compression;
using FsModManager.Core.Editing;
using FsModManager.Core.ModScanning.Parsing;
using FsModManager.Core.Savegames;
using FsModManager.Core.Resolution;
using Xunit;

namespace FsModManager.Core.Tests.Resolution;

public sealed class SharedDataFileMergeAnalyzerTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly SharedDataFileMergeAnalyzer _analyzer = new();

    public SharedDataFileMergeAnalyzerTests()
    {
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private string CreateZipWithFile(string internalName, string entryFileName, string content)
    {
        var zipPath = Path.Combine(_tempFolder, $"{internalName}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryFileName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
        return zipPath;
    }

    // --- fillTypes.xml scenarios ---

    [Fact]
    public void Analyze_DisjointFillTypeAdditions_IsMergeableAndCombinesBoth()
    {
        const string xmlA = """
            <fillTypes>
                <fillType name="WHEAT"><price>100</price></fillType>
                <fillType name="CUSTOMGRAIN_A"><price>500</price></fillType>
            </fillTypes>
            """;

        const string xmlB = """
            <fillTypes>
                <fillType name="WHEAT"><price>100</price></fillType>
                <fillType name="CUSTOMGRAIN_B"><price>700</price></fillType>
            </fillTypes>
            """;

        var result = _analyzer.Analyze("fillTypes.xml", xmlA, "ModA", xmlB, "ModB");

        Assert.True(result.IsMergeable);
        Assert.NotNull(result.MergedXml);
        Assert.Empty(result.ConflictingEntries);
        Assert.Contains("CUSTOMGRAIN_A", result.MergedXml);
        Assert.Contains("CUSTOMGRAIN_B", result.MergedXml);
        Assert.Contains("WHEAT", result.MergedXml);
    }

    [Fact]
    public void Analyze_OverlappingFillTypeDefinedDifferently_IsNotMergeableAndReportsEntries()
    {
        const string xmlA = """
            <fillTypes>
                <fillType name="CUSTOMGRAIN"><price>500</price></fillType>
            </fillTypes>
            """;

        const string xmlB = """
            <fillTypes>
                <fillType name="CUSTOMGRAIN"><price>999</price></fillType>
            </fillTypes>
            """;

        var result = _analyzer.Analyze("fillTypes.xml", xmlA, "ModA", xmlB, "ModB");

        Assert.False(result.IsMergeable);
        Assert.Null(result.MergedXml);
        Assert.Single(result.ConflictingEntries);
        Assert.Equal("CUSTOMGRAIN", result.ConflictingEntries[0].Name);
        Assert.Contains("500", result.ConflictingEntries[0].DefinitionFromModA);
        Assert.Contains("999", result.ConflictingEntries[0].DefinitionFromModB);
        Assert.Contains("manual decision", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_SameEntryDefinedIdenticallyByBoth_IsStillMergeable()
    {
        const string xmlA = """
            <fillTypes>
                <fillType name="WHEAT"><price>100</price></fillType>
                <fillType name="ONLY_A"><price>1</price></fillType>
            </fillTypes>
            """;

        const string xmlB = """
            <fillTypes>
                <fillType name="WHEAT"><price>100</price></fillType>
            </fillTypes>
            """;

        var result = _analyzer.Analyze("fillTypes.xml", xmlA, "ModA", xmlB, "ModB");

        Assert.True(result.IsMergeable);
        Assert.Empty(result.ConflictingEntries);
    }

    // --- densityHeights.xml: same generic logic path, different element/collection names ---

    [Fact]
    public void Analyze_DensityHeights_DisjointAdditions_IsMergeable()
    {
        const string xmlA = """
            <map>
                <densityMapHeightTypes>
                    <densityMapHeightType name="DIRT" collisionMask="1"/>
                    <densityMapHeightType name="MOD_A_TYPE" collisionMask="2"/>
                </densityMapHeightTypes>
            </map>
            """;

        const string xmlB = """
            <map>
                <densityMapHeightTypes>
                    <densityMapHeightType name="DIRT" collisionMask="1"/>
                    <densityMapHeightType name="MOD_B_TYPE" collisionMask="3"/>
                </densityMapHeightTypes>
            </map>
            """;

        var result = _analyzer.Analyze("densityHeights.xml", xmlA, "ModA", xmlB, "ModB");

        Assert.True(result.IsMergeable);
        Assert.Contains("MOD_A_TYPE", result.MergedXml);
        Assert.Contains("MOD_B_TYPE", result.MergedXml);
    }

    [Fact]
    public void Analyze_DensityHeights_OverlappingDifferentDefinition_IsNotMergeable()
    {
        const string xmlA = """
            <map>
                <densityMapHeightTypes>
                    <densityMapHeightType name="CUSTOM" collisionMask="2"/>
                </densityMapHeightTypes>
            </map>
            """;

        const string xmlB = """
            <map>
                <densityMapHeightTypes>
                    <densityMapHeightType name="CUSTOM" collisionMask="9"/>
                </densityMapHeightTypes>
            </map>
            """;

        var result = _analyzer.Analyze("densityHeights.xml", xmlA, "ModA", xmlB, "ModB");

        Assert.False(result.IsMergeable);
        Assert.Single(result.ConflictingEntries);
        Assert.Equal("CUSTOM", result.ConflictingEntries[0].Name);
    }

    // --- end-to-end via zip files + apply to both mods ---

    [Fact]
    public async Task AnalyzeFromZips_And_MergeableSharedFileFix_AppliesIdenticalFileToBothMods()
    {
        const string xmlA = """
            <fillTypes>
                <fillType name="WHEAT"><price>100</price></fillType>
                <fillType name="CUSTOMGRAIN_A"><price>500</price></fillType>
            </fillTypes>
            """;

        const string xmlB = """
            <fillTypes>
                <fillType name="WHEAT"><price>100</price></fillType>
                <fillType name="CUSTOMGRAIN_B"><price>700</price></fillType>
            </fillTypes>
            """;

        var zipA = CreateZipWithFile("FS25_ModA", "data/fillTypes.xml", xmlA);
        var zipB = CreateZipWithFile("FS25_ModB", "maps/data/fillTypes.xml", xmlB);

        var mergeResult = _analyzer.AnalyzeFromZips(zipA, "FS25_ModA", zipB, "FS25_ModB", "fillTypes.xml");
        Assert.True(mergeResult.IsMergeable);
        Assert.Equal("data/fillTypes.xml", mergeResult.EntryPathInModA);
        Assert.Equal("maps/data/fillTypes.xml", mergeResult.EntryPathInModB);

        var parser = new ModDescParser();
        var editor = new ModFileEditor(new FakeGameProcessChecker(false), parser, contentScanner: null, Path.Combine(_tempFolder, "backups"));
        var fix = new MergeableSharedFileFix(editor);

        // Need valid modDesc.xml for ModFileEditor's parse-validation step not to consider it a
        // regression — add one to each zip first.
        AddModDesc(zipA, "FS25_ModA");
        AddModDesc(zipB, "FS25_ModB");

        var (resultA, resultB) = await fix.ApplyAsync(mergeResult, zipA, zipB);

        Assert.True(resultA.Success, resultA.FailureReason);
        Assert.True(resultB.Success, resultB.FailureReason);

        var contentA = ReadZipEntry(zipA, "data/fillTypes.xml");
        var contentB = ReadZipEntry(zipB, "maps/data/fillTypes.xml");

        Assert.Equal(mergeResult.MergedXml, contentA);
        Assert.Equal(mergeResult.MergedXml, contentB);
        Assert.Contains("CUSTOMGRAIN_A", contentA);
        Assert.Contains("CUSTOMGRAIN_B", contentA);
    }

    private static void AddModDesc(string zipPath, string internalName)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var entry = archive.CreateEntry("modDesc.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write($"""
            <modDesc descVersion="94">
                <title><en>{internalName}</en></title>
                <description><en>Desc</en></description>
                <author>Author</author>
                <version>1.0.0.0</version>
            </modDesc>
            """);
    }

    private static string ReadZipEntry(string zipPath, string entryPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/'), entryPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private sealed class FakeGameProcessChecker(bool isRunning) : IGameProcessChecker
    {
        public bool IsGameRunning() => isRunning;
    }
}
