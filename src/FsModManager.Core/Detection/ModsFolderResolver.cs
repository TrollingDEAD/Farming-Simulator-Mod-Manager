using System.Xml.Linq;

namespace FsModManager.Core.Detection;

/// <summary>
/// Resolves the FS25 mods folder. Honours a &lt;modsDirectoryOverride active="true"
/// directory="..."/&gt; entry in gameSettings.xml and falls back to the default
/// "Documents\My Games\FarmingSimulator2025\mods" folder.
/// </summary>
public sealed class ModsFolderResolver
{
    private readonly string _userProfilePath;

    public ModsFolderResolver()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    /// <summary>Creates a resolver rooted at a custom user profile folder (useful for tests).</summary>
    public ModsFolderResolver(string userProfilePath)
    {
        _userProfilePath = userProfilePath;
    }

    /// <summary>
    /// Returns the effective mods folder. Returns the default path when gameSettings.xml
    /// is missing, unreadable, or has no active override — never throws.
    /// </summary>
    public string ResolveModsFolder()
    {
        var gameFolder = Path.Combine(_userProfilePath, "Documents", "My Games", "FarmingSimulator2025");
        var defaultModsFolder = Path.Combine(gameFolder, "mods");

        try
        {
            var settingsPath = Path.Combine(gameFolder, "gameSettings.xml");
            if (!File.Exists(settingsPath))
            {
                return defaultModsFolder;
            }

            var doc = XDocument.Load(settingsPath);
            var overrideElement = doc.Descendants("modsDirectoryOverride").FirstOrDefault();
            var active = overrideElement?.Attribute("active")?.Value;
            var directory = overrideElement?.Attribute("directory")?.Value;

            if (string.Equals(active, "true", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }
        }
        catch (Exception)
        {
            // Malformed XML or unreadable file — fall through to the default.
        }

        return defaultModsFolder;
    }
}
