using System.IO;
using System.Text.Json;

namespace FsModManager.App.Services;

/// <summary>Persisted window bounds/state (the "restore" geometry, never the maximized-fill size).</summary>
public sealed class WindowPlacement
{
    public double Width { get; set; } = 1100;
    public double Height { get; set; } = 700;
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public bool IsMaximized { get; set; }
}

/// <summary>
/// Loads/saves <see cref="WindowPlacement"/> as JSON under %LOCALAPPDATA%\FsModManager, matching
/// where the app already keeps its db/logs. On load, a saved position that no longer overlaps any
/// connected monitor (e.g. a second monitor got unplugged) falls back to a centered default instead
/// of restoring an off-screen window.
/// </summary>
public sealed class WindowSettingsService
{
    private readonly string _filePath;

    public WindowSettingsService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FsModManager");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "window-settings.json");
    }

    public WindowPlacement Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var placement = JsonSerializer.Deserialize<WindowPlacement>(json);
                if (placement is not null && IsOnConnectedMonitor(placement))
                {
                    return placement;
                }
            }
        }
        catch
        {
            // Missing/corrupt settings file - fall through to the centered default below.
        }

        return new WindowPlacement();
    }

    public void Save(WindowPlacement placement)
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(placement));
        }
        catch
        {
            // Best-effort persistence; a failed save shouldn't block app shutdown.
        }
    }

    private static bool IsOnConnectedMonitor(WindowPlacement placement)
    {
        if (double.IsNaN(placement.Left) || double.IsNaN(placement.Top)
            || placement.Width <= 0 || placement.Height <= 0)
        {
            return false;
        }

        var rect = new NativeMethods.RECT
        {
            Left = (int)placement.Left,
            Top = (int)placement.Top,
            Right = (int)(placement.Left + placement.Width),
            Bottom = (int)(placement.Top + placement.Height),
        };

        return NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONULL) != IntPtr.Zero;
    }
}
