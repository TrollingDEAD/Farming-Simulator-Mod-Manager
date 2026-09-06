using FsModManager.Core.Diagnostics;
using FsModManager.Core.Models;
using Xunit;

namespace FsModManager.Core.Tests.Diagnostics;

public class GameVersionCompatibilityTests
{
    private static ModMetadata CreateMod(string name, string? descVersion, bool parsed = true) =>
        new()
        {
            InternalName = name,
            DisplayTitle = $"Title {name}",
            DescVersion = descVersion,
            DescVersionParsed = parsed,
            SourceFileName = $@"C:\mods\{name}.zip",
        };

    [Fact]
    public void FindPotentiallyOutdatedMods_WithOneOlderMod_FlagsOlderMod()
    {
        var mods = new[]
        {
            CreateMod("FS25_NewModA", "90"),
            CreateMod("FS25_NewModB", "90"),
            CreateMod("FS25_RecentMod", "89"),
            CreateMod("FS25_OldMod", "85"), // diff 5 > threshold 2
        };

        var flagged = GameVersionCompatibility.FindPotentiallyOutdatedMods(mods, thresholdDifference: 2);

        Assert.Single(flagged);
        Assert.Equal("FS25_OldMod", flagged[0].InternalName);
        Assert.Equal(85, flagged[0].ModDescVersion);
        Assert.Equal(90, flagged[0].MaxLibraryDescVersion);
        Assert.Equal(5, flagged[0].VersionDifference);
        Assert.Contains("heuristic", flagged[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindPotentiallyOutdatedMods_WhenAllModsShareSameDescVersion_ProducesNoFlags()
    {
        var mods = new[]
        {
            CreateMod("FS25_ModA", "90"),
            CreateMod("FS25_ModB", "90"),
            CreateMod("FS25_ModC", "90"),
        };

        var flagged = GameVersionCompatibility.FindPotentiallyOutdatedMods(mods);

        Assert.Empty(flagged);
    }

    [Fact]
    public void FindPotentiallyOutdatedMods_WithCloseVersions_DoesNotFlag()
    {
        var mods = new[]
        {
            CreateMod("FS25_ModA", "90"),
            CreateMod("FS25_ModB", "89"), // diff 1 <= 2
            CreateMod("FS25_ModC", "88"), // diff 2 <= 2
        };

        var flagged = GameVersionCompatibility.FindPotentiallyOutdatedMods(mods, thresholdDifference: 2);

        Assert.Empty(flagged);
    }

    [Fact]
    public void FindPotentiallyOutdatedMods_WithNoParsableDescVersions_ReturnsEmpty()
    {
        var mods = new[]
        {
            CreateMod("FS25_ModA", null, parsed: false),
            CreateMod("FS25_ModB", "not_a_number", parsed: false),
        };

        var flagged = GameVersionCompatibility.FindPotentiallyOutdatedMods(mods);

        Assert.Empty(flagged);
    }

    [Fact]
    public void FindPotentiallyOutdatedMods_IncludesDetectedGameVersionInMessage()
    {
        var mods = new[]
        {
            CreateMod("FS25_ModA", "90"),
            CreateMod("FS25_OldMod", "80"),
        };

        var flagged = GameVersionCompatibility.FindPotentiallyOutdatedMods(mods, thresholdDifference: 2, detectedGameVersion: "1.2.0.0");

        Assert.Single(flagged);
        Assert.Contains("1.2.0.0", flagged[0].Message);
    }
}
