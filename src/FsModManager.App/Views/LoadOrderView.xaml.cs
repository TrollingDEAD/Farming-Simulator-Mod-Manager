using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FsModManager.App.ViewModels;

namespace FsModManager.App.Views;

/// <summary>
/// Load-order view with simple mouse-drag reordering of the entry list: press on a row, drag past
/// the system drag threshold, drop on the target row to move the entry there. Reordering is applied
/// to the bound <see cref="LoadOrderViewModel"/> via <see cref="LoadOrderViewModel.MoveEntry"/>.
/// </summary>
public partial class LoadOrderView : UserControl
{
    private const string DragDataFormat = "FsModManager.LoadEntryIndex";

    private Point _dragStartPoint;

    public LoadOrderView()
    {
        InitializeComponent();
    }

    private void OnEntryPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void OnEntryPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not ListBox listBox)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not { } draggedItem)
        {
            return;
        }

        var index = listBox.ItemContainerGenerator.IndexFromContainer(draggedItem);
        if (index >= 0)
        {
            DragDrop.DoDragDrop(draggedItem, new DataObject(DragDataFormat, index), DragDropEffects.Move);
        }
    }

    private void OnEntryDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragDataFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnEntryDrop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox
            || DataContext is not LoadOrderViewModel viewModel
            || !e.Data.GetDataPresent(DragDataFormat))
        {
            return;
        }

        var fromIndex = (int)e.Data.GetData(DragDataFormat)!;
        var targetItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var toIndex = targetItem is not null
            ? listBox.ItemContainerGenerator.IndexFromContainer(targetItem)
            : viewModel.Entries.Count - 1;

        viewModel.MoveEntry(fromIndex, toIndex);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
