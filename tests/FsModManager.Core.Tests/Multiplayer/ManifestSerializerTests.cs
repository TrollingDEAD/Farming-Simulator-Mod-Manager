using FsModManager.Core.Multiplayer;
using Xunit;

namespace FsModManager.Core.Tests.Multiplayer;

public sealed class ManifestSerializerTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public ManifestSerializerTests()
    {
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, recursive: true);
        }
    }

    private string TempFile(string name) => Path.Combine(_tempFolder, name);

    [Fact]
    public async Task SaveThenLoad_RoundTripsEverything()
    {
        var serializer = new ManifestSerializer();
        var manifest = new ModManifest(
            new DateTime(2026, 9, 6, 12, 30, 0, DateTimeKind.Utc),
            "Sunday Co-op Farm",
            new List<ModManifestEntry>
            {
                new("FS25_ModA", "Cool Tractor", "1.0.0.0", "60096D72E4BFF635E19D6864F85FC47A", 123456),
                new("FS25_ModB", "No Version Mod", null, "AAAA", 42),
            });

        var path = TempFile("MyFarm_manifest.json");
        await serializer.SaveAsync(manifest, path);
        var loaded = await serializer.LoadAsync(path);

        Assert.Equal(manifest.GeneratedUtc, loaded.GeneratedUtc);
        Assert.Equal("Sunday Co-op Farm", loaded.Label);
        Assert.Equal(2, loaded.Entries.Count);
        Assert.Equal(manifest.Entries[0], loaded.Entries[0]);
        Assert.Equal(manifest.Entries[1], loaded.Entries[1]);
    }

    [Fact]
    public async Task SaveThenLoad_NullLabelStaysNull()
    {
        var serializer = new ManifestSerializer();
        var path = TempFile("nolabel.json");
        await serializer.SaveAsync(new ModManifest(DateTime.UtcNow, null, Array.Empty<ModManifestEntry>()), path);

        var loaded = await serializer.LoadAsync(path);

        Assert.Null(loaded.Label);
        Assert.Empty(loaded.Entries);
    }

    [Fact]
    public async Task Load_MalformedJson_ThrowsInvalidData()
    {
        var serializer = new ManifestSerializer();
        var path = TempFile("broken.json");
        await File.WriteAllTextAsync(path, "{ this is not json");

        await Assert.ThrowsAsync<InvalidDataException>(() => serializer.LoadAsync(path));
    }

    [Fact]
    public async Task Load_ValidJsonButNotAManifest_ThrowsInvalidData()
    {
        var serializer = new ManifestSerializer();
        var path = TempFile("other.json");
        await File.WriteAllTextAsync(path, """{"someSetting": true}""");

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => serializer.LoadAsync(path));
        Assert.Contains("not an FS Mod Manager manifest", ex.Message);
    }

    [Fact]
    public async Task Load_NewerFormatVersion_ThrowsInvalidData()
    {
        var serializer = new ManifestSerializer();
        var path = TempFile("future.json");
        await File.WriteAllTextAsync(path, """
            {"format": "fsmodmanager-mod-manifest", "formatVersion": 99, "entries": []}
            """);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => serializer.LoadAsync(path));
        Assert.Contains("newer format", ex.Message);
    }

    [Fact]
    public async Task Load_SkipsEntriesMissingNameOrHash()
    {
        var serializer = new ManifestSerializer();
        var path = TempFile("partial.json");
        await File.WriteAllTextAsync(path, """
            {
              "format": "fsmodmanager-mod-manifest",
              "formatVersion": 1,
              "generatedUtc": "2026-09-06T12:00:00Z",
              "entries": [
                { "internalModName": "FS25_Good", "displayTitle": "Good", "version": "1.0.0.0", "contentHash": "AAAA", "fileSizeBytes": 5 },
                { "internalModName": "", "contentHash": "BBBB" },
                { "internalModName": "FS25_NoHash" }
              ]
            }
            """);

        var loaded = await serializer.LoadAsync(path);

        var entry = Assert.Single(loaded.Entries);
        Assert.Equal("FS25_Good", entry.InternalModName);
    }

    [Fact]
    public async Task Load_EntryMissingDisplayTitle_FallsBackToInternalName()
    {
        var serializer = new ManifestSerializer();
        var path = TempFile("notitle.json");
        await File.WriteAllTextAsync(path, """
            {
              "format": "fsmodmanager-mod-manifest",
              "formatVersion": 1,
              "generatedUtc": "2026-09-06T12:00:00Z",
              "entries": [ { "internalModName": "FS25_ModA", "contentHash": "AAAA" } ]
            }
            """);

        var loaded = await serializer.LoadAsync(path);

        Assert.Equal("FS25_ModA", Assert.Single(loaded.Entries).DisplayTitle);
    }

    [Fact]
    public async Task Save_WritesHumanReadableJsonWithFormatMarker()
    {
        var serializer = new ManifestSerializer();
        var path = TempFile("readable.json");
        await serializer.SaveAsync(
            new ModManifest(DateTime.UtcNow, "Farm", new List<ModManifestEntry>
            {
                new("FS25_ModA", "Cool Tractor", "1.0.0.0", "AAAA", 1),
            }),
            path);

        var raw = await File.ReadAllTextAsync(path);

        Assert.Contains("fsmodmanager-mod-manifest", raw);
        Assert.Contains("internalModName", raw); // camelCase wire format
        Assert.Contains("FS25_ModA", raw);
    }
}
