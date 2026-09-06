using System.IO.Compression;
using FsModManager.Core.ModScanning.Conflicts;
using FsModManager.Core.ModScanning.Parsing;
using FsModManager.Core.Models;
using Xunit;

namespace FsModManager.Core.Tests.ModScanning.Conflicts;

public sealed class SharedDataFileOverrideDetectorTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fsmodmanager-shareddata-tests-").FullName;
    private readonly ModDescParser _parser = new();
    private readonly SharedDataFileOverrideDetector _detector = new();

    [Fact]
    public async Task Detect_TwoModsWithFillTypesXmlAtDifferentPaths_ReportsCriticalNamingTheWinner()
    {
        var modA = await ParseZip("FS25_zLiftablePalletsBales.zip", "ModA", "maps/map1/fillTypes.xml");
        var modB = await ParseZip("FS25_ZZGuaranteedCropPrices.zip", "ModB", "data/fillTypes.xml");

        var conflicts = _detector.Detect(new[] { modA, modB });

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictSeverity.Critical, conflict.Severity);
        Assert.Contains("fillTypes.xml", conflict.Description);
        // "ZZ" forces FS25_ZZGuaranteedCropPrices.zip to load (and win) last - see LoadOrderPriorityCalculatorTests.
        Assert.Contains("FS25_ZZGuaranteedCropPrices.zip", conflict.Description);
        Assert.Contains("FS25_zLiftablePalletsBales.zip", conflict.Description);
    }

    [Fact]
    public async Task Detect_TwoModsWithDensityHeightsXml_ReportsCritical()
    {
        var modA = await ParseZip("ModA.zip", "ModA", "densityHeights.xml");
        var modB = await ParseZip("ModB.zip", "ModB", "terrain/densityHeights.xml");

        var conflicts = _detector.Detect(new[] { modA, modB });

        Assert.Contains(conflicts, c => c.Severity == ConflictSeverity.Critical && c.Description.Contains("densityHeights.xml"));
    }

    [Fact]
    public async Task Detect_NoSharedFileOverlap_ReportsNoConflict()
    {
        var modA = await ParseZip("ModA.zip", "ModA", "maps/fillTypes.xml");
        var modB = await ParseZip("ModB.zip", "ModB", "scripts/main.lua");

        var conflicts = _detector.Detect(new[] { modA, modB });

        Assert.Empty(conflicts);
    }

    private async Task<ModMetadata> ParseZip(string zipFileName, string internalName, string sharedEntryPath)
    {
        var zipPath = Path.Combine(_tempDir, zipFileName);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var descEntry = archive.CreateEntry("modDesc.xml");
            using (var writer = new StreamWriter(descEntry.Open()))
            {
                writer.Write($"<modDesc descVersion=\"85\"><version>1.0</version><author>{internalName}</author></modDesc>");
            }

            var sharedEntry = archive.CreateEntry(sharedEntryPath);
            using var sharedWriter = new StreamWriter(sharedEntry.Open());
            sharedWriter.Write("<fillTypes></fillTypes>");
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
