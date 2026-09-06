using FsModManager.Core.Diagnostics;
using FsModManager.Core.Models;
using Xunit;

namespace FsModManager.Core.Tests.Diagnostics;

public class DuplicateModVersionDetectorTests
{
    private static ModMetadata CreateMod(string fileName, string internalName, string displayTitle, string? version = "1.0.0", long size = 1000) =>
        new()
        {
            InternalName = internalName,
            DisplayTitle = displayTitle,
            Version = version,
            FileSizeBytes = size,
            SourceFileName = $@"C:\mods\{fileName}",
            LastModifiedUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        };

    [Fact]
    public void DetectDuplicates_WithRelatedNamesAndMatchingTitle_FlagsDuplicateGroup()
    {
        var mods = new[]
        {
            CreateMod("FS25_Fendt900.zip", "FS25_Fendt900", "Fendt 900 Vario", "1.0.0", 25000000),
            CreateMod("FS25_Fendt900 (1).zip", "FS25_Fendt900", "Fendt 900 Vario", "1.0.0", 25000000),
        };

        var groups = DuplicateModVersionDetector.DetectDuplicates(mods);

        Assert.Single(groups);
        Assert.Equal("Fendt 900 Vario", groups[0].DisplayTitle);
        Assert.Equal(2, groups[0].Copies.Count);
        var fileNames = groups[0].Copies.Select(c => c.FileName).ToList();
        Assert.Contains("FS25_Fendt900.zip", fileNames);
        Assert.Contains("FS25_Fendt900 (1).zip", fileNames);
    }

    [Fact]
    public void DetectDuplicates_WithVersionedNamesAndMatchingTitle_FlagsDuplicateGroup()
    {
        var mods = new[]
        {
            CreateMod("FS25_SomeMod_v1.0.zip", "FS25_SomeMod", "Some Super Mod", "1.0.0", 10000000),
            CreateMod("FS25_SomeMod_v2.0.zip", "FS25_SomeMod", "Some Super Mod", "2.0.0", 12000000),
        };

        var groups = DuplicateModVersionDetector.DetectDuplicates(mods);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Copies.Count);
    }

    [Fact]
    public void DetectDuplicates_WithSimilarNamesButDifferentTitles_DoesNotFlag()
    {
        var mods = new[]
        {
            CreateMod("FS25_Fendt900.zip", "FS25_Fendt900", "Fendt 900 Vario"),
            CreateMod("FS25_Fendt900 (1).zip", "FS25_Fendt900", "John Deere 8R"), // Different title
        };

        var groups = DuplicateModVersionDetector.DetectDuplicates(mods);

        Assert.Empty(groups);
    }

    [Fact]
    public void DetectDuplicates_WithSameTitleButCompletelyDifferentNames_DoesNotFlag()
    {
        var mods = new[]
        {
            CreateMod("FS25_TrailerA.zip", "FS25_TrailerA", "Heavy Duty Trailer"),
            CreateMod("FS25_TrailerB.zip", "FS25_TrailerB", "Heavy Duty Trailer"),
        };

        var groups = DuplicateModVersionDetector.DetectDuplicates(mods);

        Assert.Empty(groups);
    }

    [Fact]
    public void DetectDuplicates_WithNormalLibrary_NoFalsePositives()
    {
        var mods = new[]
        {
            CreateMod("FS25_Tractor.zip", "FS25_Tractor", "Big Tractor"),
            CreateMod("FS25_Harvester.zip", "FS25_Harvester", "Combine Harvester"),
            CreateMod("FS25_Trailer.zip", "FS25_Trailer", "Tipper Trailer"),
        };

        var groups = DuplicateModVersionDetector.DetectDuplicates(mods);

        Assert.Empty(groups);
    }

    [Fact]
    public void NormalizeBaseName_CleansVariousSuffixes()
    {
        Assert.Equal("fs25_somemod", DuplicateModVersionDetector.NormalizeBaseName("FS25_SomeMod.zip"));
        Assert.Equal("fs25_somemod", DuplicateModVersionDetector.NormalizeBaseName("FS25_SomeMod (1).zip"));
        Assert.Equal("fs25_somemod", DuplicateModVersionDetector.NormalizeBaseName("FS25_SomeMod (2)"));
        Assert.Equal("fs25_somemod", DuplicateModVersionDetector.NormalizeBaseName("FS25_SomeMod_v1.2.3.zip"));
        Assert.Equal("fs25_somemod", DuplicateModVersionDetector.NormalizeBaseName("FS25_SomeMod-copy.zip"));
    }
}
