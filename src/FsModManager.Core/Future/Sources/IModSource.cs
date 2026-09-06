using FsModManager.Core.Data.Entities;

namespace FsModManager.Core.Sources;

/// <summary>Result of checking a mod source for a newer version.</summary>
public sealed record ModSourceCheckResult(
    bool IsNewVersionAvailable,
    string? LatestVersion,
    string? DownloadUrl,
    string? Message);

/// <summary>
/// Pluggable strategy for checking/fetching mod updates from a given source.
/// Implementations should never throw for expected "no update"/"can't determine" cases;
/// reserve exceptions for programmer errors or truly unsupported operations (see <see cref="ModHubSource"/>).
/// </summary>
public interface IModSource
{
    ModSourceType SourceType { get; }

    /// <summary>Checks whether a newer version than <paramref name="currentVersion"/> is available.</summary>
    Task<ModSourceCheckResult> CheckForUpdateAsync(
        ModSource source,
        string? currentVersion,
        CancellationToken cancellationToken = default);
}
