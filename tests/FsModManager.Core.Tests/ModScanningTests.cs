using System.IO.Compression;
using FsModManager.Core.ModScanning;
using FsModManager.Core.ModScanning.Parsing;
using Xunit;

namespace FsModManager.Core.Tests;

public sealed class ModScanningTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fsmodmanager-scan-tests-").FullName;
    private readonly ModDirectoryScanner _scanner = new(new ModDescParser());

    [Fact]
    public async Task ScanAsync_ParsesAllZipsAndSkipsNonZipFiles()
    {
        CreateModZip("ModOne", "<modDesc descVersion=\"85\"><version>1.0</version></modDesc>");
        CreateModZip("ModTwo", "<modDesc descVersion=\"85\"><version>2.0</version></modDesc>");
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "not a mod");

        var results = await _scanner.ScanAsync(_tempDir);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.InternalName == "ModOne");
        Assert.Contains(results, r => r.InternalName == "ModTwo");
    }

    [Fact]
    public async Task ScanAsync_MissingFolder_ReturnsEmpty()
    {
        var results = await _scanner.ScanAsync(Path.Combine(_tempDir, "does-not-exist"));

        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanAsync_MalformedModDesc_DoesNotThrowAndReportsWarnings()
    {
        CreateModZip("BrokenMod", "<modDesc descVersion=\"85\"><version>1.0</modDesc>");

        var results = await _scanner.ScanAsync(_tempDir);

        var brokenMod = Assert.Single(results);
        Assert.False(brokenMod.IsValid);
        Assert.NotEmpty(brokenMod.Warnings);
    }

    private string CreateModZip(string internalName, string modDescXml)
    {
        var zipPath = Path.Combine(_tempDir, internalName + ".zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("modDesc.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(modDescXml);
        return zipPath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
