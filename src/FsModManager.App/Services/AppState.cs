using CommunityToolkit.Mvvm.ComponentModel;
using FsModManager.Core.Models;

namespace FsModManager.App.Services;

/// <summary>
/// App-wide shared state, set by the install-selection flow and consumed by the mods/load-order views.
/// </summary>
public sealed partial class AppState : ObservableObject
{
    [ObservableProperty]
    private GameInstallation? _selectedInstallation;

    /// <summary>The resolved FS25 mods folder (set when a mod scan runs).</summary>
    [ObservableProperty]
    private string? _modsFolderPath;
}
