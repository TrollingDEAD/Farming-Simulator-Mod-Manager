using System.Windows;

namespace FsModManager.App.Views;

/// <summary>
/// Small themed replacement for MessageBox.Show (never used in this app so dialogs stay on-brand):
/// a confirm/cancel prompt or an info dialog with an optional list of detail lines.
/// </summary>
public partial class AppDialog : Window
{
    private AppDialog()
    {
        InitializeComponent();
    }

    /// <summary>Shows a Yes/No-style confirmation. Returns true only if the user confirms.</summary>
    public static bool Confirm(string title, string message, string confirmText = "Confirm")
    {
        // Capture BEFORE constructing the dialog: if no window exists yet (e.g. this runs during
        // startup before MainWindow.Show()), WPF auto-promotes the first Window it constructs to
        // Application.MainWindow - reading it after `new AppDialog()` would resolve to the dialog
        // itself and throw "Cannot set Owner to itself".
        var owner = Application.Current?.MainWindow;
        var dialog = new AppDialog
        {
            Title = title,
            Owner = owner,
        };
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirmText;

        return dialog.ShowDialog() == true;
    }

    /// <summary>Shows an info-only dialog (single OK button), optionally listing detail lines.</summary>
    public static void ShowInfo(string title, string message, IReadOnlyList<string>? lines = null)
    {
        var owner = Application.Current?.MainWindow;
        var dialog = new AppDialog
        {
            Title = title,
            Owner = owner,
        };
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = "OK";
        dialog.CancelButton.Visibility = Visibility.Collapsed;

        if (lines is { Count: > 0 })
        {
            dialog.LinesList.ItemsSource = lines;
            dialog.LinesList.Visibility = Visibility.Visible;
        }

        dialog.ShowDialog();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
