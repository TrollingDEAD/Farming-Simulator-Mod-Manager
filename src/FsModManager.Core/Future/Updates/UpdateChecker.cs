using FsModManager.Core.Data;
using FsModManager.Core.Data.Entities;
using FsModManager.Core.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FsModManager.Core.Updates;

/// <summary>
/// Simple timer-driven background service (not full ASP.NET hosting — just
/// <see cref="BackgroundService"/> from Microsoft.Extensions.Hosting) that periodically checks
/// every installed mod with a configured source for a newer version.
/// </summary>
public sealed class UpdateChecker : BackgroundService, IUpdateChecker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UpdateCheckerOptions _options;
    private readonly ILogger<UpdateChecker> _logger;

    public UpdateChecker(
        IServiceScopeFactory scopeFactory,
        IOptions<UpdateCheckerOptions> options,
        ILogger<UpdateChecker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public event EventHandler<ModUpdateAvailableEventArgs>? NewVersionAvailable;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.CheckInterval);
        do
        {
            try
            {
                await CheckAllAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update check cycle failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckAllAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FsModManagerDbContext>();
        var modSources = scope.ServiceProvider.GetServices<IModSource>().ToDictionary(s => s.SourceType);

        var mods = await dbContext.InstalledMods
            .Include(m => m.ModSource)
            .Where(m => m.ModSource != null)
            .ToListAsync(cancellationToken);

        foreach (var mod in mods)
        {
            if (mod.ModSource is null || !modSources.TryGetValue(mod.ModSource.Type, out var source))
            {
                continue;
            }

            try
            {
                var result = await source.CheckForUpdateAsync(mod.ModSource, mod.Version, cancellationToken);
                mod.LastCheckedDate = DateTimeOffset.UtcNow;

                if (result.IsNewVersionAvailable && result.LatestVersion is not null)
                {
                    NewVersionAvailable?.Invoke(this, new ModUpdateAvailableEventArgs(mod, result.LatestVersion, result.DownloadUrl));
                }
            }
            catch (NotImplementedException)
            {
                // Expected for ModHubSource until scraping is implemented; skip quietly.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check for updates for mod {InternalName}", mod.InternalName);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
