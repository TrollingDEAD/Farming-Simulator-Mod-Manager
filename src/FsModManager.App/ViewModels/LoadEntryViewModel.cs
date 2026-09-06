using CommunityToolkit.Mvvm.ComponentModel;
using FsModManager.Core.Models;

namespace FsModManager.App.ViewModels;

/// <summary>One editable row in the load-order list: a mod name plus its active toggle.</summary>
public sealed partial class LoadEntryViewModel : ObservableObject
{
    public LoadEntryViewModel(string internalModName, bool active)
    {
        InternalModName = internalModName;
        _active = active;
    }

    public string InternalModName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveText))]
    private bool _active;

    public string ActiveText => Active ? "Active" : "Inactive";

    public ModLoadEntry ToEntry(int order) => new(InternalModName, Active, order);
}
