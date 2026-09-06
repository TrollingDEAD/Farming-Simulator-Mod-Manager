using System.IO;
using System.Linq;
using System.Windows;
using FsModManager.App.Services;
using FsModManager.App.ViewModels;
using FsModManager.Core.Backups;
using FsModManager.Core.Data;
using FsModManager.Core.Detection;
using FsModManager.Core.Diagnostics;
using FsModManager.Core.Editing;
using FsModManager.Core.Launching;
using FsModManager.Core.ModScanning;
using FsModManager.Core.ModScanning.Conflicts;
using FsModManager.Core.ModScanning.Parsing;
using FsModManager.Core.ModScanning.Repository;
using FsModManager.Core.Multiplayer;
using FsModManager.Core.Resolution;
using FsModManager.Core.Savegames;
using FsModManager.Core.Updates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace FsModManager.App;

public partial class App : Application
{
    private IHost? _host;
    private IServiceScope? _appScope;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Velopack must be the literal first thing that runs: it may need to intercept this
        // process for install/update/uninstall hooks, and can exit it before startup continues.
        Velopack.VelopackApp.Build().Run();

        base.OnStartup(e);

        // Startup can show dialogs (e.g. the leftover Clean Test session prompt below) before
        // MainWindow exists. The default ShutdownMode (OnLastWindowClose) would treat closing that
        // dialog as "the last window closed" and shut the whole app down right there - restored back
        // to the normal behavior right before MainWindow.Show().
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FsModManager");
        Directory.CreateDirectory(appDataFolder);
        var dbPath = Path.Combine(appDataFolder, "fsmodmanager.db");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(appDataFolder, "log-.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddDbContext<FsModManagerDbContext>(options =>
                    options.UseSqlite($"Data Source={dbPath}"));

                // --- Core: scanning ---
                services.AddSingleton<IModDescParser, ModDescParser>();
                services.AddSingleton<IModDirectoryScanner, ModDirectoryScanner>();
                services.AddSingleton<IModContentScanner, ModContentScanner>();
                services.AddSingleton<ILoadOrderPriorityCalculator, LoadOrderPriorityCalculator>();
                services.AddSingleton(sp => ConflictDetectionPipeline.CreateDefault(loadOrderCalculator: sp.GetRequiredService<ILoadOrderPriorityCalculator>()));

                // --- Core: diagnostics (log analyzer) ---
                services.AddSingleton<KnownErrorPatternMatcher>();
                services.AddSingleton<LogAnalyzer>();
                services.AddSingleton<L10nFixService>();

                // --- Core: detection ---
                services.AddSingleton<IGameInstallDetector>(_ => CompositeGameInstallDetector.CreateDefault());
                services.AddSingleton<ModsFolderResolver>();

                // --- Core: savegames / load order / backups ---
                services.AddSingleton<SavegameDiscovery>();
                services.AddSingleton<ModLoadOrderReader>();
                services.AddSingleton<ModLoadOrderWriter>();
                services.AddSingleton<IGameProcessChecker, GameProcessChecker>();
                // Explicit factory: the two-ctor service (test ctor takes a string override) would
                // otherwise be an ambiguous activation for the DI container.
                services.AddSingleton(sp => new SavegameSnapshotService(sp.GetRequiredService<IGameProcessChecker>()));
                services.AddSingleton(sp => new CleanTestModeService(sp.GetRequiredService<IGameProcessChecker>()));
                services.AddSingleton<IModFileEditor>(sp => new ModFileEditor(
                    sp.GetRequiredService<IGameProcessChecker>(),
                    sp.GetRequiredService<IModDescParser>(),
                    sp.GetRequiredService<IModContentScanner>()));

                // --- Core: conflict auto-fix (storeItem rename + shared-data-file merge) ---
                services.AddSingleton(sp => new StoreItemRenameResolver(
                    sp.GetRequiredService<IModFileEditor>(),
                    sp.GetRequiredService<ILoadOrderPriorityCalculator>()));
                services.AddSingleton<SharedDataFileMergeAnalyzer>();
                services.AddSingleton(sp => new MergeableSharedFileFix(sp.GetRequiredService<IModFileEditor>()));

                // --- Core: launching ---
                services.AddSingleton<IProcessStarter, ProcessStarter>();
                services.AddSingleton<IGameLauncher, GameLauncher>();

                // --- Core: multiplayer manifests ---
                services.AddSingleton<ManifestGenerator>();
                services.AddSingleton<ManifestSerializer>();
                services.AddSingleton<ManifestComparer>();

                // --- Core: self-updates (Velopack over this repo's GitHub Releases) ---
                services.AddSingleton<IAppUpdateGateway>(_ => new VelopackUpdateGateway());
                services.AddSingleton<IAppUpdateService, AppUpdateService>();

                // --- Core: persistence (used by future download/source features) ---
                services.AddScoped<IModRepository, ModRepository>();

                // --- App services ---
                services.AddSingleton<AppState>();
                services.AddSingleton<NavigationService>();
                services.AddSingleton<INotificationService, NotificationService>();
                services.AddSingleton<WindowSettingsService>();
                services.AddSingleton<AppSettingsService>();
                services.AddSingleton<ChangelogService>();
                services.AddSingleton<AppUpdateCoordinator>();

                // Scoped (not Singleton): resolved from a single long-lived scope below rather than
                // the root container, so scoped services like IModRepository/DbContext stay consumable.
                services.AddScoped<InstallSelectionViewModel>();
                services.AddScoped<ModListViewModel>();
                services.AddScoped<LoadOrderViewModel>();
                services.AddScoped<MultiplayerSyncViewModel>();
                services.AddScoped<ConflictsViewModel>();
                services.AddScoped<LogAnalyzerViewModel>();
                services.AddScoped<DiagnosticsViewModel>();
                services.AddScoped<SettingsViewModel>();
                services.AddScoped<CleanTestWizardViewModel>();
                services.AddScoped<MainViewModel>();
                services.AddScoped<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        _appScope = _host.Services.CreateScope();

        var dbContext = _appScope.ServiceProvider.GetRequiredService<FsModManagerDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        // Load persisted preferences before any ViewModel reads them, and apply the backup
        // retention count to the singleton writer so it applies to the very first save.
        var appSettings = _appScope.ServiceProvider.GetRequiredService<AppSettingsService>();
        appSettings.Load();
        _appScope.ServiceProvider.GetRequiredService<ModLoadOrderWriter>().AutomaticSnapshotKeepCount =
            appSettings.AutomaticSnapshotKeepCount;

        // Late-inject the navigation service into LoadOrderViewModel (for its multiplayer hint
        // button) without a ctor change — DI can't do it for us since MainViewModel owns the VMs.
        _appScope.ServiceProvider.GetRequiredService<LoadOrderViewModel>()
            .SetNavigation(_appScope.ServiceProvider.GetRequiredService<NavigationService>());

        // Before anything else, check for a Clean Test session left over from a crash/ungraceful
        // exit — the mods folder may currently be sitting emptied out, so this must never get buried.
        await CheckForLeftoverCleanTestSessionAsync();

        var mainWindow = _appScope.ServiceProvider.GetRequiredService<MainWindow>();
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        mainWindow.Show();

        // Runs after MainWindow exists so a "what's new" dialog has a safe Owner (see the
        // Owner-capture lesson on AppDialog/ChangelogDialog for why this must not run earlier).
        ShowWhatsNewIfUpdated();

        // Once-per-session background self-update check. Fire-and-forget on purpose: it must not
        // delay startup, and the coordinator catches every failure internally. When an update is
        // available the user is prompted via a banner - nothing downloads or installs on its own.
        _ = _appScope.ServiceProvider.GetRequiredService<AppUpdateCoordinator>().CheckForUpdateOnStartupAsync();
    }

    private void ShowWhatsNewIfUpdated()
    {
        if (_appScope is null)
        {
            return;
        }

        var appSettings = _appScope.ServiceProvider.GetRequiredService<AppSettingsService>();
        var currentVersion = AppVersionInfo.Current;
        var previousVersion = appSettings.LastSeenVersion;

        if (!string.IsNullOrEmpty(previousVersion) &&
            !string.Equals(previousVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
        {
            var changelog = _appScope.ServiceProvider.GetRequiredService<ChangelogService>();
            var newReleases = changelog.GetReleasesSince(previousVersion);
            if (newReleases.Count > 0)
            {
                Views.ChangelogDialog.Show($"What's new in v{currentVersion}", newReleases);
            }
        }

        appSettings.SaveLastSeenVersion(currentVersion);
    }

    private async Task CheckForLeftoverCleanTestSessionAsync()
    {
        if (_appScope is null)
        {
            return;
        }

        var cleanTestMode = _appScope.ServiceProvider.GetRequiredService<CleanTestModeService>();
        var leftover = cleanTestMode.TryLoadPersistedSession();
        if (leftover is not { IsActive: true })
        {
            return;
        }

        var restoreNow = Views.AppDialog.Confirm(
            "Unfinished Clean Test session found",
            "A previous Clean Test session was not fully restored (the app may have closed or " +
            $"crashed while it was active). {leftover.MovedFileNames.Count} mod file(s) are currently " +
            $"sitting in a holding folder instead of the mods folder:\n\n{leftover.HoldingFolderPath}\n\n" +
            "Restore them to the mods folder now?",
            "Restore Now");

        if (!restoreNow)
        {
            _appScope.ServiceProvider.GetRequiredService<INotificationService>()
                .Warning("An unfinished Clean Test session is still pending — open Troubleshoot: Clean Test to restore it.");
            return;
        }

        try
        {
            await cleanTestMode.ExitCleanTestModeAsync(leftover);
            _appScope.ServiceProvider.GetRequiredService<INotificationService>()
                .Info("Clean Test session restored — all mods are back in the mods folder.");
        }
        catch (CleanTestRestoreVerificationException ex)
        {
            var lines = ex.MissingFiles.Select(f => $"Missing: {f}")
                .Concat(ex.LeftoverInHolding.Select(f => $"Still in holding folder: {f}"))
                .ToList();
            Views.AppDialog.ShowInfo(
                "Restore verification failed",
                "The mods folder is now in an inconsistent state and needs your attention before " +
                $"doing anything else:\n{ex.Session.HoldingFolderPath}",
                lines);
        }
        catch (GameAlreadyRunningException ex)
        {
            Views.AppDialog.ShowInfo("Restore failed", ex.Message);
        }
        catch (Exception ex)
        {
            Views.AppDialog.ShowInfo("Restore failed", $"Could not restore the Clean Test session: {ex.Message}");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _appScope?.Dispose();

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }
}
