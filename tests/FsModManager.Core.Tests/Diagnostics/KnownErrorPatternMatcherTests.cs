using FsModManager.Core.Diagnostics;
using Xunit;

namespace FsModManager.Core.Tests.Diagnostics;

public sealed class KnownErrorPatternMatcherTests
{
    private readonly KnownErrorPatternMatcher _matcher = new();

    [Fact]
    public void Match_SpecializationCollision_ReturnsExplanationWithName()
    {
        var entry = LogFileParser.ParseLine(
            "The vehicle specialization 'harvesterAttachment' could not be added because variable 'spec_harvesterAttachment' already exists!");

        var result = _matcher.Match(entry);

        Assert.Equal(ErrorCategory.SpecializationCollision, result.Category);
        Assert.Contains("harvesterAttachment", result.Explanation);
    }

    [Fact]
    public void Match_TextureArrayLayer_ReturnsTextureFillTypeIssue()
    {
        var entry = LogFileParser.ParseLine("Texture array layer 'grassMask' couldn't be loaded");

        var result = _matcher.Match(entry);

        Assert.Equal(ErrorCategory.TextureFillTypeIssue, result.Category);
    }

    [Fact]
    public void Match_GraphicsAllocationError_ReturnsGraphicsUnrelated()
    {
        var entry = LogFileParser.ParseLine("[ERROR] failed to create placed resource (BufferAllocation)");

        var result = _matcher.Match(entry);

        Assert.Equal(ErrorCategory.GraphicsUnrelated, result.Category);
    }

    [Fact]
    public void Match_ManagerUnavailable_ReturnsManagerUnavailable()
    {
        var entry = LogFileParser.ParseLine("[ERROR] g_configurationManager is not available anymore");

        var result = _matcher.Match(entry);

        Assert.Equal(ErrorCategory.ManagerUnavailable, result.Category);
    }

    [Fact]
    public void Match_NoPatternMatches_ReturnsUnknownFallback()
    {
        var entry = LogFileParser.ParseLine("[ERROR] Something completely novel and unrecognized happened.");

        var result = _matcher.Match(entry);

        Assert.Equal(ErrorCategory.Unknown, result.Category);
        Assert.False(string.IsNullOrWhiteSpace(result.Explanation));
    }

    [Fact]
    public void Match_DuplicateL10nEntry_ExtractsKeyAndModAsSafeFix()
    {
        var result = _matcher.Match(LogFileParser.ParseLine("Duplicate l10n entry 'configuration_valueWide' in mod 'FS25_JohnDeere3X50Series'. Ignoring this definition."));

        Assert.Equal(ErrorCategory.DuplicateL10nEntry, result.Category);
        Assert.Equal(FixabilityTier.Safe, result.Fixability);
        Assert.Equal("configuration_valueWide", result.GetCapture("key"));
        Assert.Equal("FS25_JohnDeere3X50Series", result.GetCapture("mod"));
    }

    [Fact]
    public void Match_MissingL10nEntry_ExtractsKeyAndModAsPartialFix()
    {
        var result = _matcher.Match(LogFileParser.ParseLine("Missing l10n 'input_POWERTOOLS_TREE_REMOVE' in mod 'FS25_PowerTools'"));

        Assert.Equal(ErrorCategory.MissingL10nEntry, result.Category);
        Assert.Equal(FixabilityTier.Partial, result.Fixability);
        Assert.Equal("input_POWERTOOLS_TREE_REMOVE", result.GetCapture("key"));
        Assert.Equal("FS25_PowerTools", result.GetCapture("mod"));
        Assert.Contains("rough placeholder", result.Explanation);
    }

    [Fact]
    public void Match_TextParameterCountMismatch_ExtractsPathTemplateAndCountsAsSuggestion()
    {
        var result = _matcher.Match(LogFileParser.ParseLine("C:/mods/FS25_R38/xerion_tracvc.xml: Unable to insert parameters into text. Invalid number of params. (0 found in string 'Standard', 1 params given in 'R38')"));

        Assert.Equal(ErrorCategory.TextParameterCountMismatch, result.Category);
        Assert.Equal(FixabilityTier.SuggestOnly, result.Fixability);
        Assert.Equal("C:/mods/FS25_R38/xerion_tracvc.xml", result.GetCapture("path"));
        Assert.Equal("0", result.GetCapture("found"));
        Assert.Equal("1", result.GetCapture("expected"));
        Assert.Equal("Standard", result.GetCapture("template"));
        Assert.Equal("R38", result.GetCapture("textKey"));
    }

    [Fact]
    public void Match_MissingGuiProfile_IsInformationalAndNotFixable()
    {
        var result = _matcher.Match(LogFileParser.ParseLine("Could not retrieve GUI profile 'ingameMenuAILimitReached'. Using base reference profile instead."));

        Assert.Equal(ErrorCategory.MissingGuiProfile, result.Category);
        Assert.Equal(FixabilityTier.NotFixable, result.Fixability);
        Assert.Equal("ingameMenuAILimitReached", result.GetCapture("profile"));
    }

    [Fact]
    public void Match_FieldGeometryIssue_IsNotModFileFixable()
    {
        var result = _matcher.Match(LogFileParser.ParseLine("[ERROR] Island 3: First headland intersects field boundary!"));

        Assert.Equal(ErrorCategory.FieldGeometryIssue, result.Category);
        Assert.Equal(FixabilityTier.NotModFileFixable, result.Fixability);
    }

    [Fact]
    public void Match_PerformanceMemoryPressure_ExtractsMemoryCountsAsInformational()
    {
        var result = _matcher.Match(LogFileParser.ParseLine("Performing emergency garbage collection pass - memory went from 377773 KB to 790421 KB in less than one frame"));

        Assert.Equal(ErrorCategory.PerformanceMemoryPressure, result.Category);
        Assert.Equal(FixabilityTier.NotModFileFixable, result.Fixability);
        Assert.Equal("377773", result.GetCapture("before"));
        Assert.Equal("790421", result.GetCapture("after"));
    }
}
