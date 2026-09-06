using FsModManager.Core.Diagnostics;
using FsModManager.Core.Models;
using Xunit;

namespace FsModManager.Core.Tests.Diagnostics;

public sealed class ModAttributionMatcherTests
{
    private static ModMetadata Mod(string internalName, string displayTitle) => new()
    {
        InternalName = internalName,
        DisplayTitle = displayTitle,
    };

    [Fact]
    public void Attribute_SelfTagMatchesDisplayTitle_ReturnsInternalName()
    {
        var mods = new[] { Mod("FS25_SoilFertilizer", "Soil Fertilizer") };
        var entry = LogFileParser.ParseLine("[Soil Fertilizer] Applied fertilizer to field 3.");

        var result = ModAttributionMatcher.Attribute(entry, mods);

        Assert.Equal("FS25_SoilFertilizer", result);
    }

    [Fact]
    public void Attribute_PathContainsInternalName_ReturnsInternalName()
    {
        var mods = new[] { Mod("FS25_ZYX_SeasonalPrices_crossplay", "Seasonal Prices") };
        var entry = LogFileParser.ParseLine(
            "Failed to open xml file 'C:/MODS/FS25_ZYX_SeasonalPrices_crossplay/data/objects/bigBag/pigFood/bigBag_pigFood.xml'");

        var result = ModAttributionMatcher.Attribute(entry, mods);

        Assert.Equal("FS25_ZYX_SeasonalPrices_crossplay", result);
    }

    [Fact]
    public void Attribute_NoMatch_ReturnsNull()
    {
        var mods = new[] { Mod("FS25_SomeOtherMod", "Some Other Mod") };
        var entry = LogFileParser.ParseLine("[ERROR] Totally unrelated engine message.");

        var result = ModAttributionMatcher.Attribute(entry, mods);

        Assert.Null(result);
    }

    [Fact]
    public void Attribute_BracketIsErrorMarkerNotSelfTag_DoesNotFalselyMatch()
    {
        var mods = new[] { Mod("FS25_Error", "ERROR") };
        var entry = LogFileParser.ParseLine("2026-08-24 22:15:21.029 8346.328 [--- 74] [ERROR] Island 3: First headland intersects field boundary!");

        var result = ModAttributionMatcher.Attribute(entry, mods);

        Assert.Null(result);
    }
}
