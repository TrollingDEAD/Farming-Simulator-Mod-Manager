using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FsModManager.Core.Diagnostics;

/// <summary>Category a matched known error/warning pattern falls into.</summary>
public enum ErrorCategory
{
    Unknown,
    SpecializationCollision,
    MissingFileReference,
    TextureFillTypeIssue,
    ManagerUnavailable,
    GraphicsUnrelated,
    DuplicateL10nEntry,
    MissingL10nEntry,
    TextParameterCountMismatch,
    MissingGuiProfile,
    FieldGeometryIssue,
    PerformanceMemoryPressure,
}

public enum FixabilityTier
{
    NotFixable,
    NotModFileFixable,
    SuggestOnly,
    Partial,
    Safe,
}

/// <summary>
/// One community-editable pattern rule: a regex matched (case-insensitive) against a log line's
/// raw text, mapped to a category and a plain-language explanation template. "{groupName}"
/// placeholders in <see cref="Explanation"/> are substituted with the matching regex group's
/// captured value (see <see cref="KnownErrorPatternMatcher"/>).
/// </summary>
public sealed record KnownErrorPatternDefinition(
    [property: JsonPropertyName("Id")] string Id,
    [property: JsonPropertyName("Pattern")] string Pattern,
    [property: JsonPropertyName("Category")] ErrorCategory Category,
    [property: JsonPropertyName("Explanation")] string Explanation,
    [property: JsonPropertyName("Fixability")] FixabilityTier Fixability = FixabilityTier.NotFixable);

/// <summary>
/// Loads the community-editable list of known log error/warning patterns. Bundled as a plain JSON
/// file (KnownErrorPatterns.json, copied next to the app executable) rather than a hardcoded C#
/// list - same approach as <see cref="FsModManager.Core.ModScanning.Conflicts.SharedDataFileDefinitions"/> -
/// so new patterns can be added as the community reports more, without a code change/rebuild.
/// </summary>
public static class KnownErrorPatternDefinitions
{
    private const string FileName = "KnownErrorPatterns.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Built-in fallback, mirroring the real-world patterns from the BACKGROUND research.</summary>
    private static readonly IReadOnlyList<KnownErrorPatternDefinition> BuiltInDefaults = new List<KnownErrorPatternDefinition>
    {
        new(
            "specialization-collision",
            @"specialization\s+'(?<name>[^']+)'\s+could not be added because variable\s+'spec_\k<name>'\s+already exists",
            ErrorCategory.SpecializationCollision,
            "Duplicate vehicle specialization name conflict: two installed mods both declare a specialization named '{name}', so FS25 rejected the second one at load time."),
        new(
            "missing-xml-file",
            @"Failed to open xml file '(?<path>[^']+)'",
            ErrorCategory.MissingFileReference,
            "Broken/missing file reference: the game tried to load '{path}' and couldn't. This usually means a corrupted mod zip, or a mod referencing a file that isn't actually included in its own package."),
        new(
            "missing-storeitem",
            @"Unable to find vehicle storeitem for '(?<path>[^']+)'",
            ErrorCategory.MissingFileReference,
            "Broken/missing file reference: the game couldn't find the vehicle storeItem '{path}'. This usually means a corrupted mod zip, or a mod referencing a file that isn't actually included in its own package."),
        new(
            "texture-array-layer",
            @"Texture array layer '[^']*' couldn't be loaded",
            ErrorCategory.TextureFillTypeIssue,
            "Texture/fillType conflict: a texture array layer couldn't be loaded. This usually means two mods disagree about a shared fillTypes.xml or texture array definition."),
        new(
            "manager-unavailable",
            @"is not available anymore",
            ErrorCategory.ManagerUnavailable,
            "A game system was referenced after it was no longer available. This often happens when a mod references game state before it's initialized, or when a mod that's since been removed is still referenced somewhere (e.g. in a savegame). The exact cause varies case by case."),
        new(
            "graphics-allocation",
            @"failed to create placed resource|BufferAllocation|ImageAllocation",
            ErrorCategory.GraphicsUnrelated,
            "This is a graphics/VRAM allocation error, not a mod issue. It usually points to a GPU driver or VRAM problem - removing mods is very unlikely to fix it."),
    };

    /// <summary>
    /// Loads definitions from <paramref name="jsonFilePath"/> (defaults to KnownErrorPatterns.json
    /// next to the running assembly). Never throws - falls back to <see cref="BuiltInDefaults"/>
    /// on any missing file/parse failure.
    /// </summary>
    public static IReadOnlyList<KnownErrorPatternDefinition> Load(string? jsonFilePath = null)
    {
        var path = jsonFilePath ?? Path.Combine(AppContext.BaseDirectory, "Diagnostics", FileName);

        try
        {
            if (!File.Exists(path))
            {
                return BuiltInDefaults;
            }

            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<List<KnownErrorPatternDefinition>>(json, JsonOptions);
            return parsed is { Count: > 0 } ? parsed : BuiltInDefaults;
        }
        catch
        {
            // Malformed/unreadable JSON must never crash log analysis - fall back to the seed list.
            return BuiltInDefaults;
        }
    }
}

/// <summary>Result of matching a <see cref="LogEntry"/> against the known error pattern list.</summary>
public sealed record KnownErrorMatch(
    ErrorCategory Category,
    string Explanation,
    string? PatternId,
    FixabilityTier Fixability = FixabilityTier.NotFixable,
    IReadOnlyDictionary<string, string>? Captures = null)
{
    public string? GetCapture(string name) => Captures is not null && Captures.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// Matches Warning/Error log lines against the extensible pattern list from
/// <see cref="KnownErrorPatternDefinitions"/>, returning a plain-language explanation instead of
/// the raw line. Falls back to a generic "unrecognized" result (never null, never dropped) when
/// no pattern matches - this is deliberate: it makes gaps in the pattern list visible over time
/// rather than silently hiding unrecognized errors.
/// </summary>
public sealed class KnownErrorPatternMatcher
{
    private static readonly KnownErrorMatch Fallback = new(
        ErrorCategory.Unknown,
        "Unrecognized error/warning - no known pattern matched this line yet.",
        null);

    private readonly IReadOnlyList<(KnownErrorPatternDefinition Definition, Regex Regex)> _compiled;

    public KnownErrorPatternMatcher(IReadOnlyList<KnownErrorPatternDefinition>? patterns = null)
    {
        var definitions = patterns ?? KnownErrorPatternDefinitions.Load();
        var compiled = new List<(KnownErrorPatternDefinition, Regex)>();
        foreach (var definition in definitions)
        {
            try
            {
                compiled.Add((definition, new Regex(definition.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled)));
            }
            catch (ArgumentException)
            {
                // A malformed regex in the community-editable JSON must never crash analysis - skip it.
            }
        }

        _compiled = compiled;
    }

    public KnownErrorMatch Match(LogEntry entry)
    {
        foreach (var (definition, regex) in _compiled)
        {
            var match = regex.Match(entry.RawLine);
            if (match.Success)
            {
                var captures = match.Groups
                    .Cast<Group>()
                    .Where(group => group.Name is not "0" && group.Success)
                    .ToDictionary(group => group.Name, group => group.Value, StringComparer.OrdinalIgnoreCase);
                return new KnownErrorMatch(
                    definition.Category,
                    ExpandExplanation(definition.Explanation, match),
                    definition.Id,
                    definition.Fixability,
                    captures);
            }
        }

        return Fallback;
    }

    private static string ExpandExplanation(string template, Match match)
    {
        return Regex.Replace(template, @"\{(?<group>\w+)\}", replacementMatch =>
        {
            var group = match.Groups[replacementMatch.Groups["group"].Value];
            return group.Success ? group.Value : replacementMatch.Value;
        });
    }
}
