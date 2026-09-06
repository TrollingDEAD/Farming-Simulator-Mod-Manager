using FsModManager.Core.Detection;
using Xunit;

namespace FsModManager.Core.Tests.Detection;

public sealed class GameVersionReaderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetVersion_InvalidPath_ReturnsNull(string? path)
    {
        Assert.Null(GameVersionReader.GetVersion(path!));
    }

    [Fact]
    public void GetVersion_MissingFile_ReturnsNull()
    {
        var missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "nope.exe");

        Assert.Null(GameVersionReader.GetVersion(missing));
    }

    [Fact]
    public void GetVersion_FileWithoutVersionResource_ReturnsNull()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".exe");
        try
        {
            File.WriteAllText(tempFile, "not a real executable");

            Assert.Null(GameVersionReader.GetVersion(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetVersion_RealExecutable_ReturnsVersion()
    {
        // Building a dedicated stub exe inside a test isn't feasible without extra build
        // steps, so we use the running test host — it is guaranteed to exist and (being a
        // shipping Microsoft binary) to carry a version resource.
        var processPath = Environment.ProcessPath;
        Assert.NotNull(processPath);

        var version = GameVersionReader.GetVersion(processPath);

        Assert.False(string.IsNullOrWhiteSpace(version));
    }
}
