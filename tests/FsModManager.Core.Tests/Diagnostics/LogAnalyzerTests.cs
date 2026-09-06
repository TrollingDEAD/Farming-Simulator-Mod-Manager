using FsModManager.Core.Diagnostics;
using FsModManager.Core.Models;
using Xunit;

namespace FsModManager.Core.Tests.Diagnostics;

public sealed class LogAnalyzerTests
{
    private static ModMetadata Mod(string internalName, string displayTitle) => new()
    {
        InternalName = internalName,
        DisplayTitle = displayTitle,
    };

    [Fact]
    public void AnalyzeEntries_CoversAllRequiredScenarios()
    {
        var fakeLog = new[]
        {
            // 1. Specialization-collision pattern, attributable only via path-matching.
            "2026-08-24 20:00:00.000   [ERROR] The vehicle specialization 'harvesterAttachment' could not be added because variable 'spec_harvesterAttachment' already exists! Path: C:/MODS/FS25_ModA/modDesc.xml",
            // 2. Texture/fillType pattern.
            "2026-08-24 20:00:01.000   [ERROR] Texture array layer 'grassMask' couldn't be loaded (C:/MODS/FS25_ModB/data/grass.xml)",
            // 3. Explicit mod self-tag.
            "[Soil Fertilizer] Warning: fertilizer application failed for field 7",
            // 4. Attributable only via path-matching (no known pattern match).
            "2026-08-24 20:00:03.000   Warning: something odd happened in C:/MODS/FS25_ModC/scripts/main.lua",
            // 5. Matches neither a known pattern nor any mod - must still show up as "unrecognized", not dropped.
            "2026-08-24 20:00:04.000   [ERROR] A totally novel unrecognized failure occurred.",
            // 6. Graphics/VRAM allocation error - must be GraphicsUnrelated and NOT attributed to any mod.
            "2026-08-24 20:00:05.000   [ERROR] failed to create placed resource (BufferAllocation) C:/MODS/FS25_ModA/textures/big.dds",
        };

        var mods = new[]
        {
            Mod("FS25_ModA", "Mod A"),
            Mod("FS25_ModB", "Mod B"),
            Mod("FS25_ModC", "Mod C"),
            Mod("FS25_SoilFertilizer", "Soil Fertilizer"),
        };

        var entries = LogFileParser.Parse(fakeLog);
        var analyzer = new LogAnalyzer();

        var result = analyzer.AnalyzeEntries(entries, mods);

        Assert.True(result.LogFileExists);
        Assert.Equal(4, result.TotalErrorCount);
        Assert.Equal(2, result.TotalWarningCount);

        // Scenario 1: specialization collision, attributed via path.
        var modAGroup = Assert.Single(result.ModGroups, g => g.ModInternalName == "FS25_ModA");
        Assert.Contains(modAGroup.Entries, e => e.Category == ErrorCategory.SpecializationCollision);

        // Scenario 2: texture/fillType, attributed via path.
        var modBGroup = Assert.Single(result.ModGroups, g => g.ModInternalName == "FS25_ModB");
        Assert.Contains(modBGroup.Entries, e => e.Category == ErrorCategory.TextureFillTypeIssue);

        // Scenario 3: self-tag attribution.
        var soilGroup = Assert.Single(result.ModGroups, g => g.ModInternalName == "FS25_SoilFertilizer");
        Assert.Single(soilGroup.Entries);

        // Scenario 4: path-only attribution, no known pattern.
        var modCGroup = Assert.Single(result.ModGroups, g => g.ModInternalName == "FS25_ModC");
        Assert.Contains(modCGroup.Entries, e => e.Category == ErrorCategory.Unknown);

        // Scenario 5: unrecognized, not attributable - still present, not dropped.
        Assert.Contains(result.UnattributedEntries, e =>
            e.Category == ErrorCategory.Unknown &&
            e.Entry.RawLine.Contains("totally novel unrecognized failure"));

        // Scenario 6: graphics error classified correctly and NOT attributed, even though the path
        // contains a known mod's internal name.
        var graphicsEntry = Assert.Single(result.UnattributedEntries, e => e.Category == ErrorCategory.GraphicsUnrelated);
        Assert.Null(graphicsEntry.Entry.AttributedModInternalName);
        Assert.DoesNotContain(modAGroup.Entries, e => e.Category == ErrorCategory.GraphicsUnrelated);
    }

    [Fact]
    public void AnalyzeEntries_MatchedErrorAttributedToModWithKnownConflict_FlagsMatchesKnownConflict()
    {
        var entries = LogFileParser.Parse(new[]
        {
            "2026-08-24 20:00:00.000   [ERROR] The vehicle specialization 'foo' could not be added because variable 'spec_foo' already exists! C:/MODS/FS25_ModA/modDesc.xml",
        });
        var mods = new[] { Mod("FS25_ModA", "Mod A") };
        var conflicts = new[]
        {
            new ModConflict("FS25_ModA", null, "FS25_ModX", null, ConflictSeverity.Critical, "Both mods declare a specialization named 'foo'"),
        };

        var analyzer = new LogAnalyzer();
        var result = analyzer.AnalyzeEntries(entries, mods, conflicts);

        var group = Assert.Single(result.ModGroups);
        Assert.True(Assert.Single(group.Entries).MatchesKnownConflict);
    }

    [Fact]
    public void Analyze_LogFileDoesNotExist_ReturnsEmptyResultWithLogFileExistsFalse()
    {
        var analyzer = new LogAnalyzer();

        var result = analyzer.Analyze(Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.txt"), Array.Empty<ModMetadata>());

        Assert.False(result.LogFileExists);
        Assert.Equal(0, result.TotalErrorCount);
        Assert.Empty(result.ModGroups);
    }

    [Fact]
    public void AnalyzeEntries_RealKnownPatternFixtures_HasNoUnknownEntries()
    {
        var entries = LogFileParser.Parse(new[]
        {
            "Warning: Duplicate l10n entry 'configuration_valueWide' in mod 'FS25_JohnDeere3X50Series'. Ignoring this definition.",
            "Warning: Missing l10n 'input_POWERTOOLS_TREE_REMOVE' in mod 'FS25_PowerTools'",
            "Warning: C:/mods/FS25_R38/xerion_tracvc.xml: Unable to insert parameters into text. Invalid number of params. (0 found in string 'Standard', 1 params given in 'R38')",
            "Warning: Could not retrieve GUI profile 'ingameMenuAILimitReached'. Using base reference profile instead.",
            "2026-08-24 20:00:01.000 [ERROR] Island 3: First headland intersects field boundary!",
            "Warning: Performing emergency garbage collection pass - memory went from 377773 KB to 790421 KB in less than one frame",
        });

        var result = new LogAnalyzer().AnalyzeEntries(entries, new[]
        {
            Mod("FS25_JohnDeere3X50Series", "John Deere"),
            Mod("FS25_PowerTools", "Power Tools"),
        });

        var analyzed = result.ModGroups.SelectMany(group => group.Entries)
            .Concat(result.UnattributedEntries)
            .ToList();

        Assert.Equal(6, analyzed.Count);
        Assert.DoesNotContain(analyzed, entry => entry.Category == ErrorCategory.Unknown);
        Assert.Equal("FS25_JohnDeere3X50Series", analyzed[0].Entry.AttributedModInternalName);
        Assert.Equal("FS25_PowerTools", analyzed[1].Entry.AttributedModInternalName);
        Assert.Equal("C:/mods/FS25_R38/xerion_tracvc.xml", analyzed[2].Captures["path"]);
        Assert.Contains(analyzed, entry => entry.Category == ErrorCategory.FieldGeometryIssue);
        Assert.Contains(analyzed, entry => entry.Category == ErrorCategory.PerformanceMemoryPressure);
    }
}
