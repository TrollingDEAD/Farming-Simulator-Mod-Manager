using System.Windows.Controls;

namespace FsModManager.App.Views;

public partial class ModListView : UserControl
{
    public ModListView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Scrolls the newly selected row into view — needed because the "jump to mod" action from the
    /// Conflicts overview tab sets SelectedMod via binding, which doesn't auto-scroll on its own.
    /// </summary>
    private void ModsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0)
        {
            ModsListBox.ScrollIntoView(e.AddedItems[0]);
        }
    }
}
