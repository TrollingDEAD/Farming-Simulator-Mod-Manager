using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FsModManager.App.Services;

/// <summary>One changelog entry for a single released version, as read from changelog.json.</summary>
public sealed record ChangelogRelease(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("entries")] IReadOnlyList<string> Entries);

/// <summary>
/// Reads changelog.json - a lightweight, app-readable mirror of CHANGELOG.md (version -> bullet
/// strings) kept manually in sync with it. The app deliberately does not parse CHANGELOG.md's
/// markdown directly; this is the one source of truth for what the UI displays.
/// </summary>
public sealed class ChangelogService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Lazy<IReadOnlyList<ChangelogRelease>> _releases;

    public ChangelogService()
    {
        _releases = new Lazy<IReadOnlyList<ChangelogRelease>>(Load);
    }

    /// <summary>All releases, newest first (matches changelog.json's own ordering).</summary>
    public IReadOnlyList<ChangelogRelease> Releases => _releases.Value;

    public ChangelogRelease? GetRelease(string version) =>
        Releases.FirstOrDefault(r => string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Releases newer than <paramref name="previousVersion"/> (exclusive), for a "what's new since
    /// your last version" view. Returns everything if the previous version isn't found/known.
    /// </summary>
    public IReadOnlyList<ChangelogRelease> GetReleasesSince(string? previousVersion)
    {
        if (string.IsNullOrWhiteSpace(previousVersion))
        {
            return Releases;
        }

        var releases = Releases;
        var index = -1;
        for (var i = 0; i < releases.Count; i++)
        {
            if (string.Equals(releases[i].Version, previousVersion, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        return index < 0 ? releases : releases.Take(index).ToList();
    }

    private static IReadOnlyList<ChangelogRelease> Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "changelog.json");
            if (!File.Exists(path))
            {
                return Array.Empty<ChangelogRelease>();
            }

            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<List<ChangelogRelease>>(json, JsonOptions);
            return parsed is null ? Array.Empty<ChangelogRelease>() : parsed;
        }
        catch (Exception)
        {
            // A missing/malformed changelog file must never break app startup.
            return Array.Empty<ChangelogRelease>();
        }
    }
}
