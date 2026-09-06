using System.Windows;
using FsModManager.App.Services;

namespace FsModManager.App.Views;

/// <summary>
/// Themed "changelog / what's new" viewer. Shows a list of <see cref="ChangelogRelease"/> entries -
/// either the full changelog or just the releases newer than the user's last-seen version.
/// </summary>
public partial class ChangelogDialog : Window
{
    private ChangelogDialog()
    {
        InitializeComponent();
    }

    public static void Show(string title, IReadOnlyList<ChangelogRelease> releases)
    {
        // Capture BEFORE constructing the dialog - see AppDialog for why (WPF auto-promotes the
        // first Window it ever constructs to Application.MainWindow if nothing has claimed that slot yet).
        var owner = Application.Current?.MainWindow;
        var dialog = new ChangelogDialog
        {
            Title = title,
            Owner = owner,
        };
        dialog.HeaderText.Text = title;

        if (releases.Count == 0)
        {
            dialog.EmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            dialog.ReleasesList.ItemsSource = releases;
        }

        dialog.ShowDialog();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
