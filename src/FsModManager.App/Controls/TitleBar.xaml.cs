using System.Windows;
using System.Windows.Shell;

namespace FsModManager.App.Controls;

/// <summary>
/// Custom window chrome title bar. The bar itself lives in the WindowChrome caption region
/// (so it drags the window and double-click toggles maximize); the buttons opt back into
/// hit-testing via WindowChrome.IsHitTestVisibleInChrome.
/// </summary>
public partial class TitleBar : System.Windows.Controls.UserControl
{
    private const string MaximizeGlyph = "\uE922";
    private const string RestoreGlyph = "\uE923";

    public TitleBar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is null)
        {
            return;
        }

        window.StateChanged += (_, _) => UpdateMaximizeGlyph(window);
        UpdateMaximizeGlyph(window);
    }

    private void UpdateMaximizeGlyph(Window window)
    {
        var maximized = window.WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? RestoreGlyph : MaximizeGlyph;
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            SystemCommands.MinimizeWindow(window);
        }
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not { } window)
        {
            return;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(window);
        }
        else
        {
            SystemCommands.MaximizeWindow(window);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            SystemCommands.CloseWindow(window);
        }
    }
}
