namespace FsModManager.Core.Updates;

/// <summary>
/// Self-update checks against the app's release feed (GitHub Releases, via Velopack).
/// Every member is safe to call on any build: when the app isn't running from a Velopack-managed
/// installation (e.g. <c>dotnet run</c> during development, or raw <c>dotnet publish</c> output
/// that was never installed), checks short-circuit to "no update available" and download/apply
/// calls no-op.
/// </summary>
public interface IAppUpdateService
{
    /// <summary>
    /// True when the app is running from a Velopack-managed installation and can therefore
    /// actually download/apply updates. False for development runs.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>
    /// Checks the release feed for a newer version. Never throws — any network/API failure is
    /// swallowed and reported as "no update available".
    /// </summary>
    Task<AppUpdateCheckResult> CheckForUpdateAsync();

    /// <summary>
    /// Downloads the update found by the last <see cref="CheckForUpdateAsync"/> call that returned
    /// an update (reporting 0–100 progress via <paramref name="progressCallback"/>), then exits
    /// the app, applies the update, and relaunches it. No-ops when no update was found or the app
    /// isn't installed. May throw when the download itself fails — the caller surfaces that and
    /// the app keeps running unchanged.
    /// </summary>
    Task DownloadAndApplyUpdateAsync(Action<int>? progressCallback = null);
}
