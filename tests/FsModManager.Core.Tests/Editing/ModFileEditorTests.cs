using System.IO.Compression;
using System.Xml.Linq;
using FsModManager.Core.Editing;
using FsModManager.Core.Launching;
using FsModManager.Core.ModScanning;
using FsModManager.Core.ModScanning.Parsing;
using FsModManager.Core.Savegames;
using Xunit;

namespace FsModManager.Core.Tests.Editing;

public sealed class ModFileEditorTests : IDisposable
{
    private readonly string _tempFolder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly ModDescParser _parser = new();
    private readonly ModContentScanner _contentScanner = new();

    public ModFileEditorTests()
    {
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
        {
            try
            {
                Directory.Delete(_tempFolder, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    private string ModsFolder => Path.Combine(_tempFolder, "mods");
    private string BackupsRoot => Path.Combine(_tempFolder, "mod_backups");

    private ModFileEditor CreateEditor(bool isGameRunning = false, string? backupsOverride = null)
    {
        return new ModFileEditor(
            new FakeGameProcessChecker(isGameRunning),
            _parser,
            _contentScanner,
            backupsOverride ?? BackupsRoot);
    }

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

    [Fact]
    public async Task ApplyEditAsync_SuccessfulEdit_EndToEnd()
    {
        const string initialModDesc = """
            <modDesc descVersion="94">
                <title><en>Initial Title</en></title>
                <description><en>Initial Description</en></description>
                <author>TestAuthor</author>
                <version>1.0.0.0</version>
            </modDesc>
            """;

        const string updatedModDesc = """
            <modDesc descVersion="94">
                <title><en>Updated Title</en></title>
                <description><en>Updated Description</en></description>
                <author>TestAuthor</author>
                <version>1.1.0.0</version>
            </modDesc>
            """;

        var zipPath = CreateModZip("FS25_TestMod", initialModDesc, ("custom.txt", "hello world"));
        var editor = CreateEditor();

        var edit = new ModFileEdit("modDesc.xml", EditOperation.ReplaceEntryContent, updatedModDesc);
        var result = await editor.ApplyEditAsync(zipPath, edit, reason: "update-version");

        // 1. Result should be success
        Assert.True(result.Success);
        Assert.Null(result.FailureReason);
        Assert.NotEmpty(result.BackupPath);

        // 2. Backup must exist and contain original content
        Assert.True(File.Exists(result.BackupPath));
        var backupContent = ReadZipEntry(result.BackupPath, "modDesc.xml");
        Assert.Contains("Initial Title", backupContent);

        // 3. Live file must reflect the change
        var liveContent = ReadZipEntry(zipPath, "modDesc.xml");
        Assert.Contains("Updated Title", liveContent);
        Assert.Equal("hello world", ReadZipEntry(zipPath, "custom.txt"));

        // 4. Mod must still parse correctly
        var metadata = await _parser.ParseAsync(zipPath);
        Assert.True(metadata.IsValid);
        Assert.Equal("Updated Title", metadata.DisplayTitle);
        Assert.Equal("1.1.0.0", metadata.Version);
    }

    [Fact]
    public async Task ApplyEditAsync_ValidationFailure_LeavesLiveFileUntouched()
    {
        const string initialModDesc = """
            <modDesc descVersion="94">
                <title><en>Good Title</en></title>
                <description><en>Good Description</en></description>
                <author>GoodAuthor</author>
                <version>1.0.0.0</version>
            </modDesc>
            """;

        const string brokenModDesc = "<modDesc descVersion=\"94\"><title><unclosedTag></modDesc>";

        var zipPath = CreateModZip("FS25_BrokenEditMod", initialModDesc);
        var initialHash = File.ReadAllBytes(zipPath);
        var editor = CreateEditor();

        var edit = new ModFileEdit("modDesc.xml", EditOperation.ReplaceEntryContent, brokenModDesc);
        var result = await editor.ApplyEditAsync(zipPath, edit);

        // 1. Result should indicate failure with specific validation problem
        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("Validation failed", result.FailureReason);
        Assert.NotEmpty(result.BackupPath);

        // 2. Backup was made before validation
        Assert.True(File.Exists(result.BackupPath));

        // 3. Live file must remain 100% untouched
        var currentHash = File.ReadAllBytes(zipPath);
        Assert.Equal(initialHash, currentHash);
        var liveContent = ReadZipEntry(zipPath, "modDesc.xml");
        Assert.Contains("Good Title", liveContent);
    }

    [Fact]
    public async Task ApplyEditAsync_StoreItemValidation_CatchesCorruptedStoreXml()
    {
        const string modDescXml = """
            <modDesc descVersion="94">
                <title><en>Tractor Mod</en></title>
                <description><en>Cool tractor</en></description>
                <author>Modder</author>
                <version>1.0.0.0</version>
                <storeItems>
                    <storeItem xmlFilename="vehicles/tractor.xml"/>
                </storeItems>
            </modDesc>
            """;

        const string initialTractorXml = """
            <vehicle>
                <storeData>
                    <name><en>Big Tractor</en></name>
                    <specs><power>300</power></specs>
                </storeData>
            </vehicle>
            """;

        const string brokenTractorXml = "<vehicle><storeData><name><broken";

        var zipPath = CreateModZip("FS25_TractorMod", modDescXml, ("vehicles/tractor.xml", initialTractorXml));
        var editor = CreateEditor();

        var edit = new ModFileEdit("vehicles/tractor.xml", EditOperation.ReplaceEntryContent, brokenTractorXml);
        var result = await editor.ApplyEditAsync(zipPath, edit);

        Assert.False(result.Success);
        Assert.Contains("Validation failed", result.FailureReason);

        // Live file was untouched
        var liveContent = ReadZipEntry(zipPath, "vehicles/tractor.xml");
        Assert.Contains("Big Tractor", liveContent);
    }

    [Fact]
    public async Task ApplyEditAsync_BackupFailure_AbortsBeforeModifyingFile()
    {
        const string initialModDesc = """
            <modDesc descVersion="94">
                <title><en>Safe Title</en></title>
                <description><en>Safe Description</en></description>
                <author>SafeAuthor</author>
                <version>1.0.0.0</version>
            </modDesc>
            """;

        var zipPath = CreateModZip("FS25_BackupFailMod", initialModDesc);
        var initialHash = File.ReadAllBytes(zipPath);

        // Point backup location to an invalid file path that cannot be created as a directory
        var invalidBackupsPath = Path.Combine(_tempFolder, "fileAsDir");
        File.WriteAllText(invalidBackupsPath, "I am a file, not a directory");
        var invalidOverride = Path.Combine(invalidBackupsPath, "nested");

        var editor = CreateEditor(backupsOverride: invalidOverride);

        var edit = new ModFileEdit("modDesc.xml", EditOperation.ReplaceEntryContent, "<modDesc descVersion=\"94\"><title><en>New</en></title><author>A</author><version>2</version></modDesc>");
        var result = await editor.ApplyEditAsync(zipPath, edit);

        // Should fail due to backup creation failure
        Assert.False(result.Success);
        Assert.Contains("backup", result.FailureReason, StringComparison.OrdinalIgnoreCase);

        // Live file was NOT touched at all
        var currentHash = File.ReadAllBytes(zipPath);
        Assert.Equal(initialHash, currentHash);
    }

    [Fact]
    public async Task ApplyEditAsync_DeleteEntry_RemovesTargetEntry()
    {
        const string initialModDesc = """
            <modDesc descVersion="94">
                <title><en>Delete Test</en></title>
                <description><en>Testing delete</en></description>
                <author>Author</author>
                <version>1.0.0.0</version>
            </modDesc>
            """;

        var zipPath = CreateModZip("FS25_DeleteMod", initialModDesc, ("obsolete/old_file.xml", "<old />"));
        var editor = CreateEditor();

        var edit = new ModFileEdit("obsolete/old_file.xml", EditOperation.DeleteEntry);
        var result = await editor.ApplyEditAsync(zipPath, edit);

        Assert.True(result.Success);

        using var archive = ZipFile.OpenRead(zipPath);
        Assert.Null(archive.GetEntry("obsolete/old_file.xml"));
        Assert.NotNull(archive.GetEntry("modDesc.xml"));
    }

    [Fact]
    public async Task RevertToBackupAsync_RoundTrip_RestoresOriginalFile()
    {
        const string initialModDesc = """
            <modDesc descVersion="94">
                <title><en>Original Title</en></title>
                <description><en>Original Description</en></description>
                <author>OriginalAuthor</author>
                <version>1.0.0.0</version>
            </modDesc>
            """;

        const string modifiedModDesc = """
            <modDesc descVersion="94">
                <title><en>Modified Title</en></title>
                <description><en>Modified Description</en></description>
                <author>OriginalAuthor</author>
                <version>2.0.0.0</version>
            </modDesc>
            """;

        var zipPath = CreateModZip("FS25_RevertMod", initialModDesc);
        var editor = CreateEditor();

        // 1. Apply edit
        var edit = new ModFileEdit("modDesc.xml", EditOperation.ReplaceEntryContent, modifiedModDesc);
        var editResult = await editor.ApplyEditAsync(zipPath, edit);
        Assert.True(editResult.Success);
        Assert.Contains("Modified Title", ReadZipEntry(zipPath, "modDesc.xml"));

        // 2. Revert to backup
        var revertResult = await editor.RevertToBackupAsync(zipPath, editResult.BackupPath);
        Assert.True(revertResult.Success);
        Assert.Null(revertResult.FailureReason);

        // 3. Confirm original content restored
        var restoredContent = ReadZipEntry(zipPath, "modDesc.xml");
        Assert.Contains("Original Title", restoredContent);
        Assert.DoesNotContain("Modified Title", restoredContent);
    }

    [Fact]
    public async Task ApplyXmlTransformAsync_TransformsXmlAndAppliesEdit()
    {
        const string initialModDesc = """
            <modDesc descVersion="90">
                <title><en>Old DescVersion</en></title>
                <description><en>Testing transform</en></description>
                <author>Author</author>
                <version>1.0.0.0</version>
            </modDesc>
            """;

        var zipPath = CreateModZip("FS25_TransformMod", initialModDesc);
        var editor = CreateEditor();

        var result = await editor.ApplyXmlTransformAsync(zipPath, "modDesc.xml", doc =>
        {
            var root = doc.Root;
            Assert.NotNull(root);
            root.SetAttributeValue("descVersion", "94");
            return doc;
        });

        Assert.True(result.Success);
        var liveContent = ReadZipEntry(zipPath, "modDesc.xml");
        Assert.Contains("descVersion=\"94\"", liveContent);

        var metadata = await _parser.ParseAsync(zipPath);
        Assert.Equal("94", metadata.DescVersion);
    }

    [Fact]
    public async Task ApplyXmlTransformAsync_MissingEntry_ReturnsCleanFailure()
    {
        const string initialModDesc = """
            <modDesc descVersion="94">
                <title><en>Title</en></title>
                <description><en>Desc</en></description>
                <author>Author</author>
                <version>1.0.0.0</version>
            </modDesc>
            """;

        var zipPath = CreateModZip("FS25_MissingEntryMod", initialModDesc);
        var editor = CreateEditor();

        var result = await editor.ApplyXmlTransformAsync(zipPath, "nonexistent.xml", doc => doc);

        Assert.False(result.Success);
        Assert.Contains("not found", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyXmlTransformAsync_InvalidXmlEntry_ReturnsCleanFailure()
    {
        const string initialModDesc = """
            <modDesc descVersion="94">
                <title><en>Title</en></title>
                <description><en>Desc</en></description>
                <author>Author</author>
                <version>1.0.0.0</version>
            </modDesc>
            """;

        var zipPath = CreateModZip("FS25_NonXmlMod", initialModDesc, ("textfile.txt", "not xml at all <<>>"));
        var editor = CreateEditor();

        var result = await editor.ApplyXmlTransformAsync(zipPath, "textfile.txt", doc => doc);

        Assert.False(result.Success);
        Assert.Contains("invalid XML", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Operations_RefuseWhenGameIsRunning()
    {
        const string initialModDesc = """
            <modDesc descVersion="94">
                <title><en>Game Running</en></title>
                <description><en>Desc</en></description>
                <author>Author</author>
                <version>1.0.0.0</version>
            </modDesc>
            """;

        var zipPath = CreateModZip("FS25_GuardedMod", initialModDesc);
        var editor = CreateEditor(isGameRunning: true);

        await Assert.ThrowsAsync<GameAlreadyRunningException>(() =>
            editor.ApplyEditAsync(zipPath, new ModFileEdit("modDesc.xml", EditOperation.ReplaceEntryContent, initialModDesc)));

        await Assert.ThrowsAsync<GameAlreadyRunningException>(() =>
            editor.RevertToBackupAsync(zipPath, "dummy_backup.zip"));

        await Assert.ThrowsAsync<GameAlreadyRunningException>(() =>
            editor.ApplyXmlTransformAsync(zipPath, "modDesc.xml", d => d));
    }

    [Fact]
    public async Task ListBackups_ReturnsBackupsNewestFirst()
    {
        const string modDesc = """
            <modDesc descVersion="94">
                <title><en>Multi Backup</en></title>
                <description><en>Desc</en></description>
                <author>Author</author>
                <version>1.0.0.0</version>
            </modDesc>
            """;

        var zipPath = CreateModZip("FS25_ListBackupMod", modDesc);
        var editor = CreateEditor();

        var res1 = await editor.ApplyEditAsync(zipPath, new ModFileEdit("custom.txt", EditOperation.ReplaceEntryContent, "v1"), reason: "fix1");
        await Task.Delay(10);
        var res2 = await editor.ApplyEditAsync(zipPath, new ModFileEdit("custom.txt", EditOperation.ReplaceEntryContent, "v2"), reason: "fix2");

        var backups = editor.ListBackups("FS25_ListBackupMod");
        Assert.Equal(2, backups.Count);
        Assert.Equal(res2.BackupPath, backups[0].FilePath);
        Assert.Equal("fix2", backups[0].Reason);
        Assert.Equal(res1.BackupPath, backups[1].FilePath);
        Assert.Equal("fix1", backups[1].Reason);

        var allBackups = editor.ListBackups();
        Assert.Contains(allBackups, b => b.FilePath == res1.BackupPath);
        Assert.Contains(allBackups, b => b.FilePath == res2.BackupPath);
    }

    [Fact]
    public async Task ApplyEditAsync_NonExistentModZip_ReturnsFailureCleanly()
    {
        var editor = CreateEditor();
        var result = await editor.ApplyEditAsync(
            Path.Combine(ModsFolder, "NonExistent.zip"),
            new ModFileEdit("modDesc.xml", EditOperation.ReplaceEntryContent, "<modDesc />"));

        Assert.False(result.Success);
        Assert.Contains("not found", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RevertToBackupAsync_NonExistentBackup_ReturnsFailureCleanly()
    {
        var zipPath = CreateModZip("FS25_DummyMod", "<modDesc descVersion=\"94\"><title><en>D</en></title><author>A</author><version>1</version></modDesc>");
        var editor = CreateEditor();

        var result = await editor.RevertToBackupAsync(zipPath, Path.Combine(BackupsRoot, "nonexistent_backup.zip"));
        Assert.False(result.Success);
        Assert.Contains("not found", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AtomicallyReplaceFile_ReplacesFileCleanly()
    {
        var targetDir = Path.Combine(_tempFolder, "atomic_test");
        Directory.CreateDirectory(targetDir);

        var destFile = Path.Combine(targetDir, "destination.txt");
        File.WriteAllText(destFile, "initial content");

        var srcFile = Path.Combine(_tempFolder, "source.txt");
        File.WriteAllText(srcFile, "updated content");

        ModFileEditor.AtomicallyReplaceFile(srcFile, destFile);

        Assert.Equal("updated content", File.ReadAllText(destFile));
    }

    [Fact]
    public async Task ApplyEditBatchAsync_MultiStepRename_AppliesAllEditsAsOneUnit()
    {
        const string modDescXml = """
            <modDesc descVersion="94">
                <title><en>Batch Rename Mod</en></title>
                <description><en>Desc</en></description>
                <author>Author</author>
                <version>1.0.0.0</version>
                <storeItems>
                    <storeItem xmlFilename="powerRoll1230.xml" name="Roll"/>
                </storeItems>
            </modDesc>
            """;

        const string itemXml = """
            <vehicle>
                <storeData>
                    <name><en>Power Roll</en></name>
                </storeData>
            </vehicle>
            """;

        var updatedModDescXml = modDescXml.Replace("powerRoll1230.xml", "powerRoll1230_FS25_BatchRenameMod.xml");

        var zipPath = CreateModZip("FS25_BatchRenameMod", modDescXml, ("powerRoll1230.xml", itemXml));
        var editor = CreateEditor();

        var edits = new List<ModFileEdit>
        {
            new("powerRoll1230_FS25_BatchRenameMod.xml", EditOperation.ReplaceEntryContent, itemXml),
            new("powerRoll1230.xml", EditOperation.DeleteEntry),
            new("modDesc.xml", EditOperation.ReplaceEntryContent, updatedModDescXml),
        };

        var result = await editor.ApplyEditBatchAsync(zipPath, edits, reason: "batch-rename-test");

        Assert.True(result.Success);
        Assert.Null(result.FailureReason);

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            Assert.Null(archive.GetEntry("powerRoll1230.xml"));
            Assert.NotNull(archive.GetEntry("powerRoll1230_FS25_BatchRenameMod.xml"));
        }

        Assert.Contains("Power Roll", ReadZipEntry(zipPath, "powerRoll1230_FS25_BatchRenameMod.xml"));
        Assert.Contains("powerRoll1230_FS25_BatchRenameMod.xml", ReadZipEntry(zipPath, "modDesc.xml"));

        var metadata = await _parser.ParseAsync(zipPath);
        Assert.True(metadata.IsValid);
        Assert.Equal("powerRoll1230_FS25_BatchRenameMod.xml", metadata.StoreItems.Single().XmlFilename);
    }

    [Fact]
    public async Task ApplyEditBatchAsync_ValidationFailurePartwayThrough_RollsBackEntireBatch()
    {
        const string modDescXml = """
            <modDesc descVersion="94">
                <title><en>Batch Rollback Mod</en></title>
                <description><en>Desc</en></description>
                <author>Author</author>
                <version>1.0.0.0</version>
                <storeItems>
                    <storeItem xmlFilename="powerRoll1230.xml" name="Roll"/>
                </storeItems>
            </modDesc>
            """;

        const string itemXml = "<vehicle><storeData><name><en>Power Roll</en></name></storeData></vehicle>";

        // Deliberately break modDesc.xml so the batch's LAST step fails validation, even though the
        // first two steps (rename the entry) would have succeeded on their own — proving the whole
        // batch rolls back together instead of leaving the rename half-applied.
        const string brokenModDescXml = "<modDesc descVersion=\"94\"><title><unclosedTag></modDesc>";

        var zipPath = CreateModZip("FS25_BatchRollbackMod", modDescXml, ("powerRoll1230.xml", itemXml));
        var originalHash = File.ReadAllBytes(zipPath);
        var editor = CreateEditor();

        var edits = new List<ModFileEdit>
        {
            new("powerRoll1230_FS25_BatchRollbackMod.xml", EditOperation.ReplaceEntryContent, itemXml),
            new("powerRoll1230.xml", EditOperation.DeleteEntry),
            new("modDesc.xml", EditOperation.ReplaceEntryContent, brokenModDescXml),
        };

        var result = await editor.ApplyEditBatchAsync(zipPath, edits, reason: "batch-rollback-test");

        Assert.False(result.Success);
        Assert.Contains("Validation failed", result.FailureReason);

        // The live file must be byte-for-byte untouched — old entry still present, new one absent.
        var currentHash = File.ReadAllBytes(zipPath);
        Assert.Equal(originalHash, currentHash);

        using var archive = ZipFile.OpenRead(zipPath);
        Assert.NotNull(archive.GetEntry("powerRoll1230.xml"));
        Assert.Null(archive.GetEntry("powerRoll1230_FS25_BatchRollbackMod.xml"));
    }

    private sealed class FakeGameProcessChecker(bool isRunning) : IGameProcessChecker
    {
        public bool IsGameRunning() => isRunning;
    }
}
