namespace FsModManager.Core.Updates;

/// <summary>
/// Result of a self-update check against the release feed. A failed check (offline, GitHub API
/// rate limit, running a non-installed build, ...) is reported as
/// <see cref="UpdateAvailable"/> = false rather than thrown — an update check must never block
/// or crash the app.
/// </summary>
/// <param name="UpdateAvailable">True only when a newer release was found on the feed.</param>
/// <param name="NewVersion">The available version (e.g. "1.1.0"), when one was found.</param>
/// <param name="ReleaseNotesSummary">
/// A short single-line blurb of what changed, sourced from the published GitHub release body
/// when available. Null when the release has no notes (the app falls back to its bundled
/// changelog in that case).
/// </param>
public sealed record AppUpdateCheckResult(bool UpdateAvailable, string? NewVersion, string? ReleaseNotesSummary);
