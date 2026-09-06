using System.Xml.Linq;
using FsModManager.Core.Models;

namespace FsModManager.Core.Savegames;

/// <summary>Reads a savegame's mods.xml load order.</summary>
public sealed class ModLoadOrderReader
{
    /// <summary>
    /// Returns each &lt;mod&gt; entry's name, active state, and position in the file as Order.
    /// Returns an empty list when mods.xml is missing, empty, or unreadable — never throws.
    /// </summary>
    public IReadOnlyList<ModLoadEntry> ReadLoadOrder(string savegameFolderPath)
    {
        var modsFile = Path.Combine(savegameFolderPath, "mods.xml");
        if (!File.Exists(modsFile))
        {
            return Array.Empty<ModLoadEntry>();
        }

        try
        {
            var doc = XDocument.Load(modsFile);
            var modElements = doc.Descendants("mod").ToList();

            var entries = new List<ModLoadEntry>(modElements.Count);
            for (var i = 0; i < modElements.Count; i++)
            {
                var element = modElements[i];
                var name = element.Attribute("modName")?.Value ?? element.Value.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var active = string.Equals(element.Attribute("active")?.Value, "true", StringComparison.OrdinalIgnoreCase);
                entries.Add(new ModLoadEntry(name, active, i));
            }

            return entries;
        }
        catch (Exception)
        {
            // Malformed/unreadable mods.xml — treat as no known load order rather than throwing.
            return Array.Empty<ModLoadEntry>();
        }
    }
}
