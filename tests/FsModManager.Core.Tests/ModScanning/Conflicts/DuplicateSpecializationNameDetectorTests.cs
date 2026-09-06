using System.IO.Compression;
using FsModManager.Core.ModScanning.Conflicts;
using FsModManager.Core.ModScanning.Parsing;
using FsModManager.Core.Models;
using Xunit;

namespace FsModManager.Core.Tests.ModScanning.Conflicts;

public sealed class DuplicateSpecializationNameDetectorTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fsmodmanager-spec-tests-").FullName;
    private readonly ModDescParser _parser = new();
    private readonly DuplicateSpecializationNameDetector _detector = new();

    [Fact]
    public async Task Detect_TwoModsWithSameSpecializationName_ReportsCriticalWithRealErrorMessagePattern()
    {
        const string xml = """
            <modDesc descVersion="85">
                <specializations>
                    <specialization name="harvesterAttachment" className="HarvesterAttachment" />
                </specializations>
            </modDesc>
            """;
        var modA = await ParseZip("ModA", xml);
        var modB = await ParseZip("ModB", xml);

        var conflicts = _detector.Detect(new[] { modA, modB });

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictSeverity.Critical, conflict.Severity);
        Assert.Contains("harvesterAttachment", conflict.Description);
        Assert.Contains("spec_harvesterAttachment already exists", conflict.Description);
    }

    [Fact]
    public async Task Detect_ModsWithNoSpecializations_ReportsNoConflict()
    {
        var modA = await ParseZip("ModA", "<modDesc descVersion=\"85\"></modDesc>");
        var modB = await ParseZip("ModB", "<modDesc descVersion=\"85\"></modDesc>");

        var conflicts = _detector.Detect(new[] { modA, modB });

        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task Detect_UniquelyNamedSpecializations_ReportsNoConflict()
    {
        const string xmlA = """
            <modDesc descVersion="85">
                <specializations><specialization name="fooSpec" className="Foo" /></specializations>
            </modDesc>
            """;
        const string xmlB = """
            <modDesc descVersion="85">
                <specializations><specialization name="barSpec" className="Bar" /></specializations>
            </modDesc>
            """;
        var modA = await ParseZip("ModA", xmlA);
        var modB = await ParseZip("ModB", xmlB);

        var conflicts = _detector.Detect(new[] { modA, modB });

        Assert.Empty(conflicts);
    }

    private async Task<ModMetadata> ParseZip(string internalName, string modDescXml)
    {
        var zipPath = Path.Combine(_tempDir, internalName + ".zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("modDesc.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(modDescXml);
        }

        return await _parser.ParseAsync(zipPath);
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
