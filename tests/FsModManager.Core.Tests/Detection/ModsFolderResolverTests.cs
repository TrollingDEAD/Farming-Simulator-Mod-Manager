using FsModManager.Core.Detection;
using Xunit;

namespace FsModManager.Core.Tests.Detection;

public sealed class ModsFolderResolverTests : IDisposable
{
    private readonly string _tempProfile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    private string GameFolder => Path.Combine(_tempProfile, "Documents", "My Games", "FarmingSimulator2025");
    private string DefaultModsFolder => Path.Combine(GameFolder, "mods");

    public void Dispose()
    {
        if (Directory.Exists(_tempProfile))
        {
            Directory.Delete(_tempProfile, recursive: true);
        }
    }

    [Fact]
    public void ResolveModsFolder_NoGameSettingsFile_ReturnsDefault()
    {
        var resolver = new ModsFolderResolver(_tempProfile);

        Assert.Equal(DefaultModsFolder, resolver.ResolveModsFolder());
    }

    [Fact]
    public void ResolveModsFolder_ActiveOverride_ReturnsOverrideDirectory()
    {
        var overrideDir = Path.Combine(_tempProfile, "CustomMods");
        WriteGameSettings($"""
            <?xml version="1.0" encoding="utf-8"?>
            <gameSettings>
                <modsDirectoryOverride active="true" directory="{overrideDir}" />
            </gameSettings>
            """);

        var resolver = new ModsFolderResolver(_tempProfile);

        Assert.Equal(overrideDir, resolver.ResolveModsFolder());
    }

    [Fact]
    public void ResolveModsFolder_InactiveOverride_ReturnsDefault()
    {
        var overrideDir = Path.Combine(_tempProfile, "CustomMods");
        WriteGameSettings($"""
            <?xml version="1.0" encoding="utf-8"?>
            <gameSettings>
                <modsDirectoryOverride active="false" directory="{overrideDir}" />
            </gameSettings>
            """);

        var resolver = new ModsFolderResolver(_tempProfile);

        Assert.Equal(DefaultModsFolder, resolver.ResolveModsFolder());
    }

    [Fact]
    public void ResolveModsFolder_ActiveOverrideWithoutDirectory_ReturnsDefault()
    {
        WriteGameSettings("""
            <?xml version="1.0" encoding="utf-8"?>
            <gameSettings>
                <modsDirectoryOverride active="true" />
            </gameSettings>
            """);

        var resolver = new ModsFolderResolver(_tempProfile);

        Assert.Equal(DefaultModsFolder, resolver.ResolveModsFolder());
    }

    [Fact]
    public void ResolveModsFolder_MalformedGameSettings_ReturnsDefault()
    {
        WriteGameSettings("<gameSettings><modsDirectoryOverride active=\"true\"");

        var resolver = new ModsFolderResolver(_tempProfile);

        Assert.Equal(DefaultModsFolder, resolver.ResolveModsFolder());
    }

    private void WriteGameSettings(string content)
    {
        Directory.CreateDirectory(GameFolder);
        File.WriteAllText(Path.Combine(GameFolder, "gameSettings.xml"), content);
    }
}
