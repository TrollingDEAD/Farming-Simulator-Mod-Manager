namespace FsModManager.Core.Updates;

/// <inheritdoc cref="IAppUpdateService"/>
public sealed class AppUpdateService : IAppUpdateService
{
    private const int MaxSummaryLength = 220;

    private readonly IAppUpdateGateway _gateway;

    public AppUpdateService(IAppUpdateGateway gateway)
    {
        _gateway = gateway;
    }

    /// <inheritdoc />
    public bool IsInstalled => _gateway.IsInstalled;

    /// <inheritdoc />
    public async Task<AppUpdateCheckResult> CheckForUpdateAsync()
    {
        // Non-installed builds (dotnet run, raw publish output) can't self-update — short-circuit
        // before any update logic (or network traffic) runs.
        if (!_gateway.IsInstalled)
        {
            return new AppUpdateCheckResult(false, null, null);
        }

        try
        {
            var update = await _gateway.CheckForUpdatesAsync();
            return update is null
                ? new AppUpdateCheckResult(false, null, null)
                : new AppUpdateCheckResult(true, update.Version, Summarize(update.ReleaseNotes));
        }
        catch (Exception)
        {
            // A failed update check (offline, GitHub rate-limited, malformed feed, ...) must never
            // block or crash the app — it just means "no update we know about".
            return new AppUpdateCheckResult(false, null, null);
        }
    }

    /// <inheritdoc />
    public async Task DownloadAndApplyUpdateAsync(Action<int>? progressCallback = null)
    {
        if (!_gateway.IsInstalled)
        {
            return;
        }

        await _gateway.DownloadPendingUpdateAsync(progressCallback);

        // Exits the process, swaps in the downloaded version, and relaunches the app.
        _gateway.ApplyPendingUpdateAndRestart();
    }

    /// <summary>
    /// Collapses a (usually markdown, usually multi-line) release body into a short single-line
    /// blurb suitable for a notification banner.
    /// </summary>
    private static string? Summarize(string? releaseNotes)
    {
        if (string.IsNullOrWhiteSpace(releaseNotes))
        {
            return null;
        }

        var collapsed = string.Join(' ',
            releaseNotes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length == 0)
        {
            return null;
        }

        return collapsed.Length <= MaxSummaryLength
            ? collapsed
            : string.Concat(collapsed.AsSpan(0, MaxSummaryLength), "…");
    }
}
