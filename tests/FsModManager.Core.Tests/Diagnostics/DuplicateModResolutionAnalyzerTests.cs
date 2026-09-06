using FsModManager.Core.Diagnostics;
using FsModManager.Core.Models;
using Xunit;

namespace FsModManager.Core.Tests.Diagnostics;

public class DuplicateModResolutionAnalyzerTests
{
    private static ModFileInfo CreateCandidate(
        string zipPath,
        string? version,
        DateTime lastModifiedUtc,
        long fileSizeBytes,
        IReadOnlyList<string>? parseWarnings = null) =>
        new(
            ZipPath: zipPath,
            InternalModName: "fs25_testmod",
            Version: version,
            Author: "Someone",
            DescVersionParsed: true,
            ParseWarnings: parseWarnings ?? Array.Empty<string>(),
            DisplayTitle: "Test Mod",
            IconImageData: null,
            FileSizeBytes: fileSizeBytes,
            LastModifiedUtc: lastModifiedUtc);

    private static StoreItemDetail CreateStoreItem(string xmlFilename, int functionCount) =>
        new(xmlFilename, xmlFilename, null, null, Array.Empty<SpecEntry>(), null, StoreItemKind.Vehicle)
        {
            Functions = Enumerable.Range(1, functionCount).Select(i => $"function{i}").ToList(),
        };

    [Fact]
    public void Analyze_WhenOneCandidateWinsEverySignal_RecommendsItConfidently()
    {
        var winner = CreateCandidate(@"C:\mods\a.zip", "2.1.0", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), 50_000_000);
        var loser = CreateCandidate(@"C:\mods\b.zip", "1.8.0", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 40_000_000,
            parseWarnings: new[] { "some warning" });

        var content = new Dictionary<string, IReadOnlyList<StoreItemDetail>>
        {
            [winner.ZipPath] = new List<StoreItemDetail> { CreateStoreItem("a.xml", 4), CreateStoreItem("b.xml", 2) },
            [loser.ZipPath] = new List<StoreItemDetail> { CreateStoreItem("c.xml", 1) },
        };

        var result = DuplicateModResolutionAnalyzer.Analyze(new[] { winner, loser }, content);

        Assert.True(result.IsConfident);
        Assert.Equal(winner, result.RecommendedKeeper);
        Assert.Equal(new[] { loser }, result.RecommendedToRemove);
        Assert.Null(result.UnclearReason);

        var winnerAssessment = result.CandidateAssessments.Single(a => a.Candidate.Equals(winner));
        Assert.Contains(winnerAssessment.Reasons, r => r.Contains("Newer version"));
        Assert.Contains(ResolutionSignal.Version, winnerAssessment.WinningSignals);
    }

    [Fact]
    public void Analyze_WhenSignalsDisagree_DoesNotForceARecommendation()
    {
        // A has the newer version; B has more store items — signals disagree.
        var a = CreateCandidate(@"C:\mods\a.zip", "2.0.0", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10_000_000);
        var b = CreateCandidate(@"C:\mods\b.zip", "1.0.0", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10_000_000);

        var content = new Dictionary<string, IReadOnlyList<StoreItemDetail>>
        {
            [a.ZipPath] = new List<StoreItemDetail> { CreateStoreItem("x.xml", 1) },
            [b.ZipPath] = new List<StoreItemDetail> { CreateStoreItem("x.xml", 1), CreateStoreItem("y.xml", 1) },
        };

        var result = DuplicateModResolutionAnalyzer.Analyze(new[] { a, b }, content);

        Assert.False(result.IsConfident);
        Assert.Null(result.RecommendedKeeper);
        Assert.Empty(result.RecommendedToRemove);
        Assert.Contains("disagree", result.UnclearReason, StringComparison.OrdinalIgnoreCase);

        var aAssessment = result.CandidateAssessments.Single(x => x.Candidate.Equals(a));
        var bAssessment = result.CandidateAssessments.Single(x => x.Candidate.Equals(b));
        Assert.Contains(aAssessment.Reasons, r => r.Contains("Newer version"));
        Assert.Contains(bAssessment.Reasons, r => r.Contains("store items"));
    }

    [Fact]
    public void Analyze_WhenContentNotScannedForEither_OmitsContentSignalRatherThanTreatingAsTie()
    {
        // Identical version/lastModified/warnings; only file size differs. If content richness were
        // wrongly treated as a 0-0 tie it wouldn't matter here either way, so this proves the
        // last-resort file-size signal still fires (i.e. content wasn't silently "decided" as a tie
        // that blocks anything, and no store-item reason appears at all).
        var lastModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bigger = CreateCandidate(@"C:\mods\a.zip", "1.0.0", lastModified, 60_000_000);
        var smaller = CreateCandidate(@"C:\mods\b.zip", "1.0.0", lastModified, 40_000_000);

        // No scannedContentByZipPath dictionary passed at all — nothing scanned yet for either copy.
        var result = DuplicateModResolutionAnalyzer.Analyze(new[] { bigger, smaller });

        Assert.True(result.IsConfident);
        Assert.Equal(bigger, result.RecommendedKeeper);

        foreach (var assessment in result.CandidateAssessments)
        {
            Assert.DoesNotContain(assessment.Reasons, r => r.Contains("store item", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(assessment.WinningSignals, s => s == ResolutionSignal.StoreItemCount || s == ResolutionSignal.FunctionCount);
        }

        var winnerAssessment = result.CandidateAssessments.Single(a => a.Candidate.Equals(bigger));
        Assert.Contains(ResolutionSignal.FileSize, winnerAssessment.WinningSignals);
    }

    [Fact]
    public void Analyze_WhenBothVersionStringsAreUnparseable_SkipsVersionSignal()
    {
        var lastModified = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = CreateCandidate(@"C:\mods\a.zip", "latest-beta", lastModified, 50_000_000);
        var b = CreateCandidate(@"C:\mods\b.zip", "unstable-build", lastModified, 40_000_000);

        var result = DuplicateModResolutionAnalyzer.Analyze(new[] { a, b });

        foreach (var assessment in result.CandidateAssessments)
        {
            Assert.DoesNotContain(assessment.Reasons, r => r.Contains("version", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(assessment.WinningSignals, s => s == ResolutionSignal.Version);
        }

        // File size still differentiates as a last resort since nothing else did.
        Assert.True(result.IsConfident);
        Assert.Equal(a, result.RecommendedKeeper);
    }
}
