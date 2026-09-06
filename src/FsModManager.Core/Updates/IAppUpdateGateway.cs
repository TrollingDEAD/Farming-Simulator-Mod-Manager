namespace FsModManager.Core.Updates;

/// <summary>A single available update, expressed in update-engine-agnostic terms.</summary>
public sealed record AvailableAppUpdate(string Version, string? ReleaseNotes);

/// <summary>
/// Thin seam over the concrete update engine (Velopack). Exists so <see cref="AppUpdateService"/>'s
/// not-installed guard and failure handling can be unit-tested without mocking Velopack's
/// internals (which have their own test coverage upstream).
/// </summary>
public interface IAppUpdateGateway
{
    /// <summary>True when running from a Velopack-managed installation.</summary>
    bool IsInstalled { get; }

    /// <summary>Returns the newest available update, or null when already up to date.</summary>
    Task<AvailableAppUpdate?> CheckForUpdatesAsync();

    /// <summary>
    /// Downloads the update found by the last <see cref="CheckForUpdatesAsync"/> call, reporting
    /// 0–100 progress via <paramref name="progressCallback"/>. No-ops when no check found an update.
    /// </summary>
    Task DownloadPendingUpdateAsync(Action<int>? progressCallback);

    /// <summary>
    /// Exits the app, applies the downloaded update, and relaunches. No-ops when no update was
    /// downloaded. Only call this after <see cref="DownloadPendingUpdateAsync"/> completed.
    /// </summary>
    void ApplyPendingUpdateAndRestart();
}
