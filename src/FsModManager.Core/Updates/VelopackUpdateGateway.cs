using Velopack;
using Velopack.Sources;

namespace FsModManager.Core.Updates;

/// <summary>
/// Velopack-backed <see cref="IAppUpdateGateway"/>, checking this project's GitHub Releases.
/// The repo is public, so no access token is needed — unauthenticated GitHub API requests are
/// rate-limited to 60/hour per IP, which is plenty for a desktop app's occasional startup check.
/// </summary>
public sealed class VelopackUpdateGateway : IAppUpdateGateway
{
    /// <summary>The GitHub repository whose Releases feed the update checks.</summary>
    public const string GitHubRepoUrl = "https://github.com/TrollingDEAD/Farming-Simulator-Mod-Manager";

    private readonly Lazy<UpdateManager?> _updateManager;
    private UpdateInfo? _pendingUpdate;

    public VelopackUpdateGateway()
        : this(GitHubRepoUrl)
    {
    }

    public VelopackUpdateGateway(string repoUrl)
    {
        // Lazy on purpose: constructing UpdateManager resolves the Velopack locator for the current
        // process, which is only meaningful for an installed build — never pay for (or risk) that
        // work on startup when running from source.
        _updateManager = new Lazy<UpdateManager?>(() =>
        {
            try
            {
                var source = new GithubSource(repoUrl, accessToken: null, prerelease: false);
                return new UpdateManager(source);
            }
            catch (Exception)
            {
                // Not an installed build (no Velopack locator could be resolved) — treat as
                // "not installed", so every update path in the app cleanly no-ops.
                return null;
            }
        });
    }

    /// <inheritdoc />
    public bool IsInstalled => _updateManager.Value?.IsInstalled == true;

    /// <inheritdoc />
    public async Task<AvailableAppUpdate?> CheckForUpdatesAsync()
    {
        var manager = _updateManager.Value;
        if (manager is null)
        {
            return null;
        }

        var update = await manager.CheckForUpdatesAsync();
        if (update is null)
        {
            return null;
        }

        _pendingUpdate = update;
        return new AvailableAppUpdate(
            update.TargetFullRelease.Version.ToString(),
            NullIfWhiteSpace(update.TargetFullRelease.NotesMarkdown));
    }

    /// <inheritdoc />
    public Task DownloadPendingUpdateAsync(Action<int>? progressCallback)
    {
        var manager = _updateManager.Value;
        if (manager is null || _pendingUpdate is null)
        {
            return Task.CompletedTask;
        }

        return manager.DownloadUpdatesAsync(_pendingUpdate, progressCallback);
    }

    /// <inheritdoc />
    public void ApplyPendingUpdateAndRestart()
    {
        var manager = _updateManager.Value;
        if (manager is null || _pendingUpdate is null)
        {
            return;
        }

        // Velopack 1.x API note: ApplyUpdatesAndRestart takes the target VelopackAsset (not the
        // UpdateInfo wrapper) — the asset carries the package to apply.
        manager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
