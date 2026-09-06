using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FsModManager.App.Services;
using FsModManager.App.ViewModels;

namespace FsModManager.App;

public partial class MainWindow : Window
{
    private const int WM_GETMINMAXINFO = 0x0024;

    private readonly WindowSettingsService _windowSettingsService;

    public MainWindow(MainViewModel viewModel, WindowSettingsService windowSettingsService)
    {
        InitializeComponent();
        DataContext = viewModel;
        _windowSettingsService = windowSettingsService;

        ApplySavedPlacement();

        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
    }

    private void ApplySavedPlacement()
    {
        var placement = _windowSettingsService.Load();

        if (!double.IsNaN(placement.Left) && !double.IsNaN(placement.Top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = placement.Left;
            Top = placement.Top;
        }

        Width = Math.Max(placement.Width, MinWidth);
        Height = Math.Max(placement.Height, MinHeight);

        // Applied after the initial Show() via the StateChanged-driven glyph update in TitleBar;
        // setting it here just restores "was maximized last time".
        WindowState = placement.IsMaximized ? WindowState.Maximized : WindowState.Normal;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Save the restore bounds (not the maximized-fill/minimized geometry) so next launch
        // reopens at the size the user actually resized to.
        var restoreBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;

        _windowSettingsService.Save(new WindowPlacement
        {
            Left = restoreBounds.Left,
            Top = restoreBounds.Top,
            Width = restoreBounds.Width,
            Height = restoreBounds.Height,
            IsMaximized = WindowState == WindowState.Maximized,
        });
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
        {
            hwndSource.AddHook(WndProc);
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            ApplyMaxSizeForMonitorWorkArea(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// WindowChrome + WindowStyle=None windows otherwise maximize to the full monitor bounds
    /// (including the taskbar). Patching WM_GETMINMAXINFO to the monitor's work area fixes content
    /// extending under the taskbar when maximized, and stays correct per-monitor when dragged
    /// across monitors of different size/DPI.
    /// </summary>
    private static void ApplyMaxSizeForMonitorWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var monitorInfo = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var workArea = monitorInfo.rcWork;
        var monitorArea = monitorInfo.rcMonitor;

        var minMaxInfo = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
        minMaxInfo.ptMaxPosition.X = workArea.Left - monitorArea.Left;
        minMaxInfo.ptMaxPosition.Y = workArea.Top - monitorArea.Top;
        minMaxInfo.ptMaxSize.X = workArea.Right - workArea.Left;
        minMaxInfo.ptMaxSize.Y = workArea.Bottom - workArea.Top;
        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }
}
