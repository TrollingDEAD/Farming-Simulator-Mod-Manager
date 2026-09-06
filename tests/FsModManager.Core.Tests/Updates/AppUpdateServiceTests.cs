using FsModManager.Core.Updates;
using Xunit;

namespace FsModManager.Core.Tests.Updates;

/// <summary>
/// Covers <see cref="AppUpdateService"/>'s own logic (not-installed guard, failure swallowing,
/// result shaping) via a fake gateway — Velopack's internals are deliberately not mocked; the
/// SDK has its own upstream test coverage.
/// </summary>
public sealed class AppUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdateAsync_NotInstalled_ShortCircuitsBeforeTouchingTheFeed()
    {
        var gateway = new FakeGateway { IsInstalled = false };
        var service = new AppUpdateService(gateway);

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Null(result.NewVersion);
        Assert.Null(result.ReleaseNotesSummary);
        Assert.False(gateway.CheckCalled);
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_NotInstalled_DoesNothing()
    {
        var gateway = new FakeGateway { IsInstalled = false };
        var service = new AppUpdateService(gateway);

        await service.DownloadAndApplyUpdateAsync(_ => { });

        Assert.False(gateway.DownloadCalled);
        Assert.False(gateway.ApplyCalled);
    }

    [Fact]
    public async Task CheckForUpdateAsync_CheckThrows_IsSwallowedAsNoUpdate()
    {
        var gateway = new FakeGateway
        {
            IsInstalled = true,
            ThrowOnCheck = new HttpRequestException("offline"),
        };
        var service = new AppUpdateService(gateway);

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.UpdateAvailable);
        Assert.Null(result.NewVersion);
        Assert.True(gateway.CheckCalled);
    }

    [Fact]
    public async Task CheckForUpdateAsync_UpToDate_ReportsNoUpdate()
    {
        var gateway = new FakeGateway { IsInstalled = true, NextUpdate = null };
        var service = new AppUpdateService(gateway);

        var result = await service.CheckForUpdateAsync();

        Assert.False(result.UpdateAvailable);
        Assert.True(gateway.CheckCalled);
    }

    [Fact]
    public async Task CheckForUpdateAsync_UpdateAvailable_SurfacesVersionAndCollapsedNotes()
    {
        var gateway = new FakeGateway
        {
            IsInstalled = true,
            NextUpdate = new AvailableAppUpdate("1.2.0", "line one\nline   two"),
        };
        var service = new AppUpdateService(gateway);

        var result = await service.CheckForUpdateAsync();

        Assert.True(result.UpdateAvailable);
        Assert.Equal("1.2.0", result.NewVersion);
        Assert.Equal("line one line two", result.ReleaseNotesSummary);
    }

    [Fact]
    public async Task DownloadAndApplyUpdateAsync_AfterSuccessfulCheck_DownloadsThenApplies()
    {
        var gateway = new FakeGateway
        {
            IsInstalled = true,
            NextUpdate = new AvailableAppUpdate("1.2.0", null),
        };
        var service = new AppUpdateService(gateway);
        await service.CheckForUpdateAsync();

        var progress = new List<int>();
        await service.DownloadAndApplyUpdateAsync(progress.Add);

        Assert.True(gateway.DownloadCalled);
        Assert.True(gateway.ApplyCalled);
        Assert.True(gateway.DownloadedBeforeApply);
    }

    private sealed class FakeGateway : IAppUpdateGateway
    {
        private readonly List<string> _callOrder = new();

        public bool IsInstalled { get; set; }

        public AvailableAppUpdate? NextUpdate { get; set; }

        public Exception? ThrowOnCheck { get; set; }

        public bool CheckCalled { get; private set; }

        public bool DownloadCalled { get; private set; }

        public bool ApplyCalled { get; private set; }

        public bool DownloadedBeforeApply =>
            _callOrder.IndexOf("download") >= 0 &&
            _callOrder.IndexOf("apply") > _callOrder.IndexOf("download");

        public Task<AvailableAppUpdate?> CheckForUpdatesAsync()
        {
            CheckCalled = true;
            if (ThrowOnCheck is not null)
            {
                throw ThrowOnCheck;
            }

            return Task.FromResult(NextUpdate);
        }

        public Task DownloadPendingUpdateAsync(Action<int>? progressCallback)
        {
            DownloadCalled = true;
            _callOrder.Add("download");
            progressCallback?.Invoke(100);
            return Task.CompletedTask;
        }

        public void ApplyPendingUpdateAndRestart()
        {
            ApplyCalled = true;
            _callOrder.Add("apply");
        }
    }
}
