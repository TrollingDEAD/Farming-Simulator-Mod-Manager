using System.Windows;

namespace FsModManager.App.Views;

/// <summary>
/// Reusable confirmation dialog for any auto-fix or mod-file modification feature.
/// Clearly displays which mod file is affected, the proposed change, and the automatic backup location.
/// </summary>
public partial class ModEditConfirmationDialog : Window
{
    public ModEditConfirmationDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows a modal confirmation dialog requesting permission to edit a mod file.
    /// Returns true only if the user confirms the operation.
    /// </summary>
    public static bool ShowConfirmation(
        string modFileName,
        string changeDescription,
        string backupLocationDescription,
        string confirmButtonText = "Apply Fix",
        string dialogTitle = "Confirm Mod File Modification",
        Window? owner = null)
    {
        var dialog = new ModEditConfirmationDialog
        {
            Title = dialogTitle,
            Owner = owner ?? Application.Current?.MainWindow,
        };

        dialog.ModFileText.Text = modFileName;
        dialog.ChangeDescriptionText.Text = changeDescription;
        dialog.BackupLocationText.Text = backupLocationDescription;
        dialog.ConfirmButton.Content = confirmButtonText;

        return dialog.ShowDialog() == true;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
