using System.Text.Json;
using System.Text.Json.Serialization;

namespace FsModManager.Core.ModScanning.Conflicts;

/// <summary>
/// One shared base-game data file that FS25 lets multiple mods override — matched by filename
/// only (mods place these at slightly different relative paths, e.g. "maps/.../fillTypes.xml" vs
/// "data/fillTypes.xml"), regardless of containing folder.
/// </summary>
public sealed record SharedDataFileDefinition(
    [property: JsonPropertyName("FileName")] string FileName,
    [property: JsonPropertyName("Description")] string Description);

/// <summary>
/// Loads the community-editable list of shared base-game data files that silently get
/// last-loaded-wins behavior when more than one mod ships its own copy. Bundled as a plain JSON
/// file (SharedDataFiles.json, copied next to the app executable) rather than a hardcoded C# list
/// specifically so new entries can be added as more shared-file conflicts get reported by the
/// community, without a code change/rebuild.
/// </summary>
public static class SharedDataFileDefinitions
{
    private const string FileName = "SharedDataFiles.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Built-in fallback used when the JSON file is missing or malformed, so detection never
    /// silently loses its seed entries just because the bundled file got corrupted/deleted.
    /// </summary>
    private static readonly IReadOnlyList<SharedDataFileDefinition> BuiltInDefaults = new List<SharedDataFileDefinition>
    {
        new("fillTypes.xml", "fruit/crop type definitions"),
        new("densityHeights.xml", "terrain/bulk material registration"),
    };

    /// <summary>
    /// Loads definitions from <paramref name="jsonFilePath"/> (defaults to SharedDataFiles.json
    /// next to the running assembly). Never throws — falls back to <see cref="BuiltInDefaults"/>
    /// on any missing file/parse failure.
    /// </summary>
    public static IReadOnlyList<SharedDataFileDefinition> Load(string? jsonFilePath = null)
    {
        var path = jsonFilePath ?? Path.Combine(AppContext.BaseDirectory, FileName);

        try
        {
            if (!File.Exists(path))
            {
                return BuiltInDefaults;
            }

            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<List<SharedDataFileDefinition>>(json, JsonOptions);
            return parsed is { Count: > 0 } ? parsed : BuiltInDefaults;
        }
        catch
        {
            // Malformed/unreadable JSON must never crash a mod scan - fall back to the seed list.
            return BuiltInDefaults;
        }
    }
}
