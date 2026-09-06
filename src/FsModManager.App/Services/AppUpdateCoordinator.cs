using FsModManager.Core.Diagnostics;
using FsModManager.Core.Editing;
using FsModManager.Core.Updates;

namespace FsModManager.App.Services;

/// <summary>
/// Orchestrates the user-facing self-update flow: a once-per-session background check on startup,
/// a manual "check for updates now" from Settings → About, and a non-intrusive actionable banner
/// (never a blocking dialog) when an update exists. Nothing ever downloads or installs without the
/// user explicitly choosing "Update now".
/// </summary>
public sealed class AppUpdateCoordinator
{
    private const int MaxBannerNotesLength = 220;

    private readonly IAppUpdateService _updateService;
    private readonly INotificationService _notifications;
    private readonly ChangelogService _changelog;
    private readonly CleanTestModeService _cleanTestMode;
    private readonly IModFileEditor _modFileEditor;

    private bool _checkedThisSession;
    private bool _applyInProgress;

    public AppUpdateCoordinator(
        IAppUpdateService updateService,
        INotificationService notifications,
        ChangelogService changelog,
        CleanTestModeService cleanTestMode,
        IModFileEditor modFileEditor)
    {
        _updateService = updateService;
        _notifications = notifications;
        _changelog = changelog;
        _cleanTestMode = cleanTestMode;
        _modFileEditor = modFileEditor;
    }

    /// <summary>
    /// Automatic background check after startup. Runs at most once per app session — no nagging
    /// when the app stays open for a long time.
    /// </summary>
    public async Task CheckForUpdateOnStartupAsync()
    {
        if (_checkedThisSession)
        {
            return;
        }

        _checkedThisSession = true;
        await CheckCoreAsync(userInitiated: false);
    }

    /// <summary>Manual "Check for updates now" from Settings → About. Always reports the outcome.</summary>
    public async Task CheckForUpdateNowAsync()
    {
        _checkedThisSession = true;
        await CheckCoreAsync(userInitiated: true);
    }

    private async Task CheckCoreAsync(bool userInitiated)
    {
        try
        {
            if (!_updateService.IsInstalled)
            {
                if (userInitiated)
                {
                    _notifications.Info(
                        "Self-update is only available in an installed build — not when running " +
                        "from source (dotnet run / raw publish output).");
                }

                return;
            }

            var result = await _updateService.CheckForUpdateAsync();
            if (!result.UpdateAvailable || result.NewVersion is null)
            {
                if (userInitiated)
                {
                    _notifications.Info($"You're up to date (v{AppVersionInfo.Current}).");
                }

                return;
            }

            ShowUpdateBanner(result.NewVersion, result.ReleaseNotesSummary);
        }
        catch (Exception ex)
        {
            // Belt and braces — the service already swallows check failures; nothing here may ever
            // surface as an unhandled error on the startup path.
            if (userInitiated)
            {
                _notifications.Warning($"Couldn't check for updates: {ex.Message}");
            }
        }
    }

    private void ShowUpdateBanner(string newVersion, string? releaseNotesSummary)
    {
        var notes = releaseNotesSummary ?? GetBundledChangelogSummary(newVersion);
        var message = $"Update available: v{newVersion} is ready to install." +
                      (notes is null ? string.Empty : $" {notes}");

        _notifications.Show(
            message,
            NotificationKind.Info,
            actionLabel: "Update now",
            action: () => _ = DownloadAndApplyAsync(newVersion));
    }

    private async Task DownloadAndApplyAsync(string newVersion)
    {
        if (_applyInProgress)
        {
            return;
        }

        // Applying an update restarts the app — that must never interrupt an in-progress risky
        // operation. The check itself is fine; only the apply-and-restart is deferred.
        if (_cleanTestMode.TryLoadPersistedSession() is { IsActive: true })
        {
            _notifications.Warning(
                $"v{newVersion} is ready to install, but a Clean Test session is currently active. " +
                "Finish or restore it first, then update from Settings → About.");
            return;
        }

        if (_modFileEditor.IsEditInProgress)
        {
            _notifications.Warning(
                $"v{newVersion} is ready to install, but a mod file edit is currently in progress. " +
                "Wait for it to finish, then update from Settings → About.");
            return;
        }

        _applyInProgress = true;
        var progressBanner = _notifications.Show($"Downloading update v{newVersion} — 0%", NotificationKind.Info);
        try
        {
            await _updateService.DownloadAndApplyUpdateAsync(
                progress => progressBanner.Message = $"Downloading update v{newVersion} — {progress}%");
            // ApplyUpdatesAndRestart exits the process — anything after this line only runs when
            // there was nothing to apply (e.g. the pending update disappeared between check and
            // download).
            _notifications.Dismiss(progressBanner);
        }
        catch (Exception ex)
        {
            _notifications.Dismiss(progressBanner);
            _notifications.Error(
                $"Couldn't download the update: {ex.Message} — the app is unchanged; you can retry from Settings → About.");
        }
        finally
        {
            _applyInProgress = false;
        }
    }

    /// <summary>
    /// Fallback summary from the bundled changelog when the GitHub release itself carries no notes
    /// (the release body is preferred whenever it exists, since it's guaranteed to match what was
    /// actually published).
    /// </summary>
    private string? GetBundledChangelogSummary(string version)
    {
        var release = _changelog.GetRelease(version);
        if (release is not { Entries.Count: > 0 })
        {
            return null;
        }

        var joined = string.Join(' ', release.Entries);
        return joined.Length <= MaxBannerNotesLength
            ? joined
            : string.Concat(joined.AsSpan(0, MaxBannerNotesLength), "…");
    }
}
