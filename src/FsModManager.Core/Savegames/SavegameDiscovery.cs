using System.Text.RegularExpressions;
using System.Xml.Linq;
using FsModManager.Core.Models;

namespace FsModManager.Core.Savegames;

/// <summary>Finds FS25 savegame folders under Documents\My Games\FarmingSimulator2025.</summary>
public sealed partial class SavegameDiscovery
{
    private readonly string _userProfilePath;

    public SavegameDiscovery()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    /// <summary>Creates a discovery instance rooted at a custom user profile folder (useful for tests).</summary>
    public SavegameDiscovery(string userProfilePath)
    {
        _userProfilePath = userProfilePath;
    }

    /// <summary>
    /// Enumerates savegame* folders and returns their slot index and display name.
    /// Never throws — unreadable folders/files are skipped or fall back to a generic name.
    /// </summary>
    public IReadOnlyList<SavegameInfo> FindSavegames()
    {
        var results = new List<SavegameInfo>();

        var gameFolder = Path.Combine(_userProfilePath, "Documents", "My Games", "FarmingSimulator2025");
        if (!Directory.Exists(gameFolder))
        {
            return results;
        }

        foreach (var folder in Directory.EnumerateDirectories(gameFolder, "savegame*"))
        {
            var folderName = Path.GetFileName(folder);
            var match = SavegameFolderRegex().Match(folderName);
            if (!match.Success)
            {
                continue;
            }

            var slotIndex = int.Parse(match.Groups[1].Value);
            var displayName = ReadDisplayName(folder) ?? $"Savegame {slotIndex}";

            results.Add(new SavegameInfo(folder, displayName, slotIndex));
        }

        return results.OrderBy(s => s.SlotIndex).ToList();
    }

    // FS25 stores the display name in careerSavegame.xml under settings/savegameName.
    private static string? ReadDisplayName(string savegameFolder)
    {
        var careerFile = Path.Combine(savegameFolder, "careerSavegame.xml");
        if (!File.Exists(careerFile))
        {
            return null;
        }

        try
        {
            var doc = XDocument.Load(careerFile);
            var name = doc.Descendants("savegameName").FirstOrDefault()?.Value;
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch (Exception)
        {
            // Malformed/unreadable careerSavegame.xml — fall back to the generic slot name.
            return null;
        }
    }

    [GeneratedRegex(@"^savegame(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SavegameFolderRegex();
}
