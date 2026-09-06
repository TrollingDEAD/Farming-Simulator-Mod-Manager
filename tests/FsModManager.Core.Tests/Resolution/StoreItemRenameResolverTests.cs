using System.IO.Compression;
using FsModManager.Core.Editing;
using FsModManager.Core.ModScanning;
using FsModManager.Core.ModScanning.Parsing;
using FsModManager.Core.Savegames;
using FsModManager.Core.Resolution;
using Xunit;

namespace FsModManager.Core.Tests.Resolution;

public sealed class StoreItemRenameResolverTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly ModDescParser _parser = new();
    private readonly ModContentScanner _contentScanner = new();

    public StoreItemRenameResolverTests()
    {
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private string ModsFolder => Path.Combine(_tempFolder, "mods");
    private string BackupsRoot => Path.Combine(_tempFolder, "mod_backups");

    private string CreateModZip(string internalName, string modDescXml, params (string Path, string Content)[] additionalFiles)
    {
        Directory.CreateDirectory(ModsFolder);
        var zipPath = Path.Combine(ModsFolder, $"{internalName}.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var modDescEntry = archive.CreateEntry("modDesc.xml");
        using (var writer = new StreamWriter(modDescEntry.Open()))
        {
            writer.Write(modDescXml);
        }

        foreach (var (path, content) in additionalFiles)
        {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return zipPath;
    }

    private static string ReadZipEntry(string zipPath, string entryPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/'), entryPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string ModDescXmlWithStoreItem(string title, string xmlFilename) => $"""
        <modDesc descVersion="94">
            <title><en>{title}</en></title>
            <description><en>Desc</en></description>
            <author>Author</author>
            <version>1.0.0.0</version>
            <storeItems>
                <storeItem xmlFilename="{xmlFilename}" name="Item"/>
            </storeItems>
        </modDesc>
        """;

    [Fact]
    public async Task ApplyAsync_RenamesLaterLoadingModsCollidingFile_EndToEnd()
    {
        // Mirrors the real forum-reported FS25_dalboPowerRoll1230 <-> FS25_powerRoll1230HD case:
        // both declare a storeItem pointing at the same xmlFilename by coincidence.
        const string collidingFileName = "powerRoll1230.xml";
        const string contentA = "<vehicle><storeData><name><en>Dalbo Roll</en></name></storeData></vehicle>";
        const string contentB = "<vehicle><storeData><name><en>HD Roll</en></name></storeData></vehicle>";

        var zipA = CreateModZip("FS25_dalboPowerRoll1230", ModDescXmlWithStoreItem("Dalbo Roll", collidingFileName), (collidingFileName, contentA));
        var zipB = CreateModZip("FS25_powerRoll1230HD", ModDescXmlWithStoreItem("HD Roll", collidingFileName), (collidingFileName, contentB));

        var editor = new ModFileEditor(new FakeGameProcessChecker(false), _parser, _contentScanner, BackupsRoot);
        var resolver = new StoreItemRenameResolver(editor);

        var metadataA = await _parser.ParseAsync(zipA);
        var metadataB = await _parser.ParseAsync(zipB);

        var conflict = new Models.ModConflict(
            metadataA.InternalName, collidingFileName, metadataB.InternalName, collidingFileName,
            Models.ConflictSeverity.Critical, "Both mods declare storeItem collision");

        // "FS25_powerRoll1230HD" sorts after "FS25_dalboPowerRoll1230" (ordinal-ignore-case), so it's
        // the alphabetically-second-loading mod and the one expected to be renamed.
        var plan = resolver.BuildPlan(conflict, metadataA, metadataB);
        Assert.NotNull(plan);
        Assert.Equal("FS25_powerRoll1230HD", plan!.ModInternalName);
        Assert.Equal("powerRoll1230_FS25_powerRoll1230HD.xml", plan.NewEntryPath);

        var result = await resolver.ApplyAsync(conflict, metadataA, metadataB);

        Assert.True(result.Success, result.FailureReason);

        // Mod A (not renamed) is untouched — still has the original file under the original name.
        using (var archiveA = ZipFile.OpenRead(zipA))
        {
            Assert.NotNull(archiveA.GetEntry(collidingFileName));
        }

        // Mod B (renamed): old entry gone, new entry present with the SAME content, modDesc.xml updated.
        using (var archiveB = ZipFile.OpenRead(zipB))
        {
            Assert.Null(archiveB.GetEntry(collidingFileName));
            Assert.NotNull(archiveB.GetEntry("powerRoll1230_FS25_powerRoll1230HD.xml"));
        }

        Assert.Equal(contentB, ReadZipEntry(zipB, "powerRoll1230_FS25_powerRoll1230HD.xml"));
        Assert.Contains("powerRoll1230_FS25_powerRoll1230HD.xml", ReadZipEntry(zipB, "modDesc.xml"));

        var reparsedB = await _parser.ParseAsync(zipB);
        Assert.True(reparsedB.IsValid);
        Assert.Equal("powerRoll1230_FS25_powerRoll1230HD.xml", reparsedB.StoreItems.Single().XmlFilename);
    }

    [Fact]
    public async Task ApplyAsync_ValidationFailure_RollsBackEntireRename()
    {
        const string collidingFileName = "shared.xml";
        const string itemXml = "<vehicle><storeData><name><en>Item</en></name></storeData></vehicle>";

        var zipA = CreateModZip("FS25_aaaFirstMod", ModDescXmlWithStoreItem("First", collidingFileName), (collidingFileName, itemXml));
        var zipB = CreateModZip("FS25_zzzSecondMod", ModDescXmlWithStoreItem("Second", collidingFileName), (collidingFileName, itemXml));

        var faultyContentScanner = new ThrowingContentScanner();
        var editor = new ModFileEditor(new FakeGameProcessChecker(false), new ThrowingAfterFirstParseParser(_parser), faultyContentScanner, BackupsRoot);
        var resolver = new StoreItemRenameResolver(editor);

        var metadataA = await _parser.ParseAsync(zipA);
        var metadataB = await _parser.ParseAsync(zipB);

        var conflict = new Models.ModConflict(
            metadataA.InternalName, collidingFileName, metadataB.InternalName, collidingFileName,
            Models.ConflictSeverity.Critical, "Both mods declare storeItem collision");

        var originalHashB = File.ReadAllBytes(zipB);

        var result = await resolver.ApplyAsync(conflict, metadataA, metadataB);

        Assert.False(result.Success);
        var currentHashB = File.ReadAllBytes(zipB);
        Assert.Equal(originalHashB, currentHashB);

        using var archiveB = ZipFile.OpenRead(zipB);
        Assert.NotNull(archiveB.GetEntry(collidingFileName));
        Assert.Null(archiveB.GetEntry("shared_FS25_zzzSecondMod.xml"));
    }

    private sealed class FakeGameProcessChecker(bool isRunning) : IGameProcessChecker
    {
        public bool IsGameRunning() => isRunning;
    }

    /// <summary>Wraps the real parser but throws on the SECOND call (the post-edit validation parse), simulating a mid-batch validation failure.</summary>
    private sealed class ThrowingAfterFirstParseParser : IModDescParser
    {
        private readonly IModDescParser _inner;
        private int _callCount;

        public ThrowingAfterFirstParseParser(IModDescParser inner) => _inner = inner;

        public async Task<Models.ModMetadata> ParseAsync(string zipFilePath, CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount > 1)
            {
                throw new InvalidOperationException("Simulated validation failure partway through the batch.");
            }

            return await _inner.ParseAsync(zipFilePath, cancellationToken);
        }
    }

    private sealed class ThrowingContentScanner : IModContentScanner
    {
        public Task<IReadOnlyList<Models.StoreItemDetail>> ScanContentAsync(Models.ModFileInfo mod, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Models.StoreItemDetail>>(Array.Empty<Models.StoreItemDetail>());

        public bool TryGetCachedContent(string zipPath, out IReadOnlyList<Models.StoreItemDetail>? content)
        {
            content = null;
            return false;
        }

        public void InvalidateCache(string zipPath)
        {
        }

        public void ClearCache()
        {
        }
    }
}
