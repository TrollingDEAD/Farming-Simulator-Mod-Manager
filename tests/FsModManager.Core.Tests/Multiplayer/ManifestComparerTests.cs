using FsModManager.Core.Multiplayer;
using Xunit;

namespace FsModManager.Core.Tests.Multiplayer;

public sealed class ManifestComparerTests
{
    private static readonly DateTime FixedUtc = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    private static ModManifestEntry Entry(
        string name,
        string? version = "1.0.0.0",
        string hash = "AAAA",
        string? title = null)
        => new(name, title ?? name, version, hash, 1024);

    private static ModManifest Manifest(params ModManifestEntry[] entries)
        => new(FixedUtc, null, entries);

    [Fact]
    public void Compare_IdenticalManifests_AllMatchingNoDifferences()
    {
        var comparer = new ManifestComparer();
        var mine = Manifest(Entry("FS25_ModA"), Entry("FS25_ModB", "2.1.0.0", "BBBB"));
        var theirs = Manifest(Entry("FS25_ModA"), Entry("FS25_ModB", "2.1.0.0", "BBBB"));

        var result = comparer.Compare(mine, theirs);

        Assert.True(result.IsMatch);
        Assert.Empty(result.MissingOnMine);
        Assert.Empty(result.MissingOnTheirs);
        Assert.Empty(result.VersionMismatches);
        Assert.Empty(result.HashMismatches);
        Assert.Equal(2, result.Matching.Count);
    }

    [Fact]
    public void Compare_MissingModOnEachSide_ReportsBothDirections()
    {
        var comparer = new ManifestComparer();
        var mine = Manifest(Entry("FS25_Shared"), Entry("FS25_OnlyMine"));
        var theirs = Manifest(Entry("FS25_Shared"), Entry("FS25_OnlyTheirs"));

        var result = comparer.Compare(mine, theirs);

        Assert.False(result.IsMatch);
        Assert.Equal("FS25_OnlyTheirs", Assert.Single(result.MissingOnMine).InternalModName);
        Assert.Equal("FS25_OnlyMine", Assert.Single(result.MissingOnTheirs).InternalModName);
        Assert.Equal("FS25_Shared", Assert.Single(result.Matching).InternalModName);
        Assert.Empty(result.VersionMismatches);
        Assert.Empty(result.HashMismatches);
    }

    [Fact]
    public void Compare_DifferentVersions_VersionMismatchOnly()
    {
        var comparer = new ManifestComparer();
        // A version bump almost always means different bytes too — it must land ONLY in the
        // version-mismatch bucket, not double-reported as a hash mismatch.
        var mine = Manifest(Entry("FS25_ModA", "1.0.0.0", "AAAA"));
        var theirs = Manifest(Entry("FS25_ModA", "1.1.0.0", "CCCC"));

        var result = comparer.Compare(mine, theirs);

        var mismatch = Assert.Single(result.VersionMismatches);
        Assert.Equal("1.0.0.0", mismatch.Mine.Version);
        Assert.Equal("1.1.0.0", mismatch.Theirs.Version);
        Assert.Empty(result.HashMismatches);
        Assert.Empty(result.Matching);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Compare_SameVersionDifferentHash_HashMismatch()
    {
        var comparer = new ManifestComparer();
        var mine = Manifest(Entry("FS25_ModA", "1.0.0.0", "AAAA"));
        var theirs = Manifest(Entry("FS25_ModA", "1.0.0.0", "DDDD"));

        var result = comparer.Compare(mine, theirs);

        var mismatch = Assert.Single(result.HashMismatches);
        Assert.Equal("AAAA", mismatch.Mine.ContentHash);
        Assert.Equal("DDDD", mismatch.Theirs.ContentHash);
        Assert.Empty(result.VersionMismatches);
        Assert.Empty(result.Matching);
    }

    [Fact]
    public void Compare_NullVersions_TreatedAsEmptyNotMismatch()
    {
        var comparer = new ManifestComparer();
        // null vs "" is the same "no version declared" state — must not be flagged.
        var mine = Manifest(Entry("FS25_ModA", null, "AAAA"));
        var theirs = Manifest(Entry("FS25_ModA", "", "AAAA"));

        var result = comparer.Compare(mine, theirs);

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void Compare_NullVersionAgainstRealVersion_IsVersionMismatch()
    {
        var comparer = new ManifestComparer();
        var mine = Manifest(Entry("FS25_ModA", null, "AAAA"));
        var theirs = Manifest(Entry("FS25_ModA", "1.0.0.0", "AAAA"));

        var result = comparer.Compare(mine, theirs);

        Assert.Single(result.VersionMismatches);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Compare_NameMatchingIsCaseInsensitive()
    {
        var comparer = new ManifestComparer();
        var mine = Manifest(Entry("FS25_moda", "1.0.0.0", "AAAA"));
        var theirs = Manifest(Entry("FS25_MODA", "1.0.0.0", "aaaa"));

        var result = comparer.Compare(mine, theirs);

        // Hex hash casing is not meaningful either — same content, different letter case.
        Assert.True(result.IsMatch);
        Assert.Single(result.Matching);
    }

    [Fact]
    public void Compare_DuplicateNamesInOneManifest_FirstEntryWins()
    {
        var comparer = new ManifestComparer();
        var mine = Manifest(Entry("FS25_ModA", "1.0.0.0", "AAAA"), Entry("FS25_ModA", "9.9.9.9", "ZZZZ"));
        var theirs = Manifest(Entry("FS25_ModA", "1.0.0.0", "AAAA"));

        var result = comparer.Compare(mine, theirs);

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void Compare_ResultsSortedByInternalName()
    {
        var comparer = new ManifestComparer();
        var mine = Manifest();
        var theirs = Manifest(Entry("FS25_Zulu"), Entry("FS25_Alpha"), Entry("FS25_Mike"));

        var result = comparer.Compare(mine, theirs);

        Assert.Equal(
            ["FS25_Alpha", "FS25_Mike", "FS25_Zulu"],
            result.MissingOnMine.Select(e => e.InternalModName));
    }
}
