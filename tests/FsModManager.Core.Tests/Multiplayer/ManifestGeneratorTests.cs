using System.Security.Cryptography;
using FsModManager.Core.Models;
using FsModManager.Core.Multiplayer;
using FsModManager.Core.Savegames;
using Xunit;

namespace FsModManager.Core.Tests.Multiplayer;

public sealed class ManifestGeneratorTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public ManifestGeneratorTests()
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

    private static ManifestGenerator CreateGenerator() => new(new ModLoadOrderReader());

    /// <summary>Writes a file with the given content and returns its path (a real zip isn't needed — the hash is over raw bytes).</summary>
    private string WriteModZip(string fileName, byte[] content)
    {
        var path = Path.Combine(_tempFolder, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static string ExpectedSha256(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    private static ModFileInfo FileInfoFor(string zipPath, string internalName, string? version = "1.0.0.0")
        => new(
            ZipPath: zipPath,
            InternalModName: internalName,
            Version: version,
            Author: null,
            DescVersionParsed: true,
            ParseWarnings: Array.Empty<string>(),
            DisplayTitle: $"{internalName} title",
            IconImageData: null);

    private static ModMetadata MetadataFor(string zipPath, string internalName, string? version, string? fileHash)
        => new()
        {
            InternalName = internalName,
            DisplayTitle = $"{internalName} title",
            Version = version,
            SourceFileName = zipPath,
            FileHash = fileHash,
        };

    [Fact]
    public async Task GenerateAsync_HashesZipBytesStreamed()
    {
        var contentA = new byte[1024 * 100]; // 100 KB
        new Random(42).NextBytes(contentA);
        var contentB = new byte[257];
        new Random(7).NextBytes(contentB);
        var pathA = WriteModZip("FS25_ModA.zip", contentA);
        var pathB = WriteModZip("FS25_ModB.zip", contentB);

        var manifest = await CreateGenerator().GenerateAsync(
            new List<ModFileInfo> { FileInfoFor(pathA, "FS25_ModA"), FileInfoFor(pathB, "FS25_ModB", "2.0.0.0") },
            label: "Test Farm");

        Assert.Equal("Test Farm", manifest.Label);
        Assert.Equal(2, manifest.Entries.Count);

        var entryA = Assert.Single(manifest.Entries, e => e.InternalModName == "FS25_ModA");
        Assert.Equal(ExpectedSha256(contentA), entryA.ContentHash);
        Assert.Equal(contentA.Length, entryA.FileSizeBytes);
        Assert.Equal("1.0.0.0", entryA.Version);
        Assert.Equal("FS25_ModA title", entryA.DisplayTitle);

        var entryB = Assert.Single(manifest.Entries, e => e.InternalModName == "FS25_ModB");
        Assert.Equal(ExpectedSha256(contentB), entryB.ContentHash);
        Assert.Equal("2.0.0.0", entryB.Version);
    }

    [Fact]
    public async Task GenerateAsync_EntriesSortedByInternalName()
    {
        var pathZ = WriteModZip("z.zip", [1]);
        var pathA = WriteModZip("a.zip", [2]);

        var manifest = await CreateGenerator().GenerateAsync(
            new List<ModFileInfo> { FileInfoFor(pathZ, "FS25_Zulu"), FileInfoFor(pathA, "FS25_Alpha") },
            label: null);

        Assert.Equal(["FS25_Alpha", "FS25_Zulu"], manifest.Entries.Select(e => e.InternalModName));
    }

    [Fact]
    public async Task GenerateAsync_WhitespaceLabelBecomesNull()
    {
        var path = WriteModZip("a.zip", [1]);

        var manifest = await CreateGenerator().GenerateAsync(
            new List<ModFileInfo> { FileInfoFor(path, "FS25_A") }, label: "   ");

        Assert.Null(manifest.Label);
    }

    [Fact]
    public async Task GenerateAsync_MissingFileIsSkippedNotFatal()
    {
        var realPath = WriteModZip("real.zip", [1, 2, 3]);
        var ghostPath = Path.Combine(_tempFolder, "deleted-since-scan.zip");

        var manifest = await CreateGenerator().GenerateAsync(
            new List<ModFileInfo> { FileInfoFor(realPath, "FS25_Real"), FileInfoFor(ghostPath, "FS25_Ghost") },
            label: null);

        Assert.Equal("FS25_Real", Assert.Single(manifest.Entries).InternalModName);
    }

    [Fact]
    public async Task GenerateActiveAsync_KeepsOnlyModsMarkedActiveInModsXml()
    {
        var savegame = Path.Combine(_tempFolder, "savegame1");
        Directory.CreateDirectory(savegame);
        await File.WriteAllTextAsync(Path.Combine(savegame, "mods.xml"), """
            <?xml version="1.0" encoding="utf-8"?>
            <modsList>
                <mod modName="FS25_ActiveA" active="true">FS25_ActiveA</mod>
                <mod modName="FS25_InactiveB" active="false">FS25_InactiveB</mod>
                <mod modName="FS25_ActiveC" active="true">FS25_ActiveC</mod>
            </modsList>
            """);

        var pathA = WriteModZip("a.zip", [10]);
        var pathB = WriteModZip("b.zip", [20]);
        var pathC = WriteModZip("c.zip", [30]);
        var mods = new List<ModFileInfo>
        {
            FileInfoFor(pathA, "FS25_ActiveA"),
            FileInfoFor(pathB, "FS25_InactiveB"),
            FileInfoFor(pathC, "FS25_ActiveC"),
        };

        var manifest = await CreateGenerator().GenerateActiveAsync(mods, savegame, label: null);

        Assert.Equal(
            ["FS25_ActiveA", "FS25_ActiveC"],
            manifest.Entries.Select(e => e.InternalModName));
    }

    [Fact]
    public async Task GenerateActiveAsync_MissingModsXmlYieldsEmptyManifest()
    {
        var pathA = WriteModZip("a.zip", [1]);

        var manifest = await CreateGenerator().GenerateActiveAsync(
            new List<ModFileInfo> { FileInfoFor(pathA, "FS25_A") }, _tempFolder, label: null);

        Assert.Empty(manifest.Entries);
    }

    [Fact]
    public async Task GenerateAsync_MetadataVariant_ReusesScanTimeHashWithoutTouchingDisk()
    {
        // The precomputed hash must be used verbatim even when the file no longer exists —
        // proving no re-hash happened (the scan's hash is trusted until the next rescan).
        var ghostPath = Path.Combine(_tempFolder, "gone.zip");
        var metadata = MetadataFor(ghostPath, "FS25_ModA", "1.0.0.0", fileHash: "SCANHASH123");

        var manifest = await CreateGenerator().GenerateAsync(
            new List<ModMetadata> { metadata }, label: null);

        var entry = Assert.Single(manifest.Entries);
        Assert.Equal("SCANHASH123", entry.ContentHash);
        Assert.Equal("FS25_ModA", entry.InternalModName);
    }

    [Fact]
    public async Task GenerateAsync_MetadataWithoutScanHash_HashesFromDisk()
    {
        var content = new byte[] { 9, 8, 7 };
        var path = WriteModZip("a.zip", content);
        var metadata = MetadataFor(path, "FS25_ModA", "1.0.0.0", fileHash: null);

        var manifest = await CreateGenerator().GenerateAsync(
            new List<ModMetadata> { metadata }, label: null);

        Assert.Equal(ExpectedSha256(content), Assert.Single(manifest.Entries).ContentHash);
    }

    [Fact]
    public async Task GenerateAsync_CachesHashes_ButDetectsChangedFile()
    {
        var path = WriteModZip("a.zip", [1, 2, 3]);
        var generator = CreateGenerator();
        var mods = new List<ModFileInfo> { FileInfoFor(path, "FS25_A") };

        var first = await generator.GenerateAsync(mods, label: null);
        Assert.Equal(ExpectedSha256([1, 2, 3]), Assert.Single(first.Entries).ContentHash);

        // Rewrite with different content (and ensure the mtime actually moves).
        await File.WriteAllBytesAsync(path, [4, 5, 6, 7]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        var second = await generator.GenerateAsync(mods, label: null);
        Assert.Equal(ExpectedSha256([4, 5, 6, 7]), Assert.Single(second.Entries).ContentHash);
    }
}
