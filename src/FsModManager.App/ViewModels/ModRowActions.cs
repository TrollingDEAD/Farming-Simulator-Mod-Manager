using FsModManager.Core.Models;

namespace FsModManager.App.ViewModels;

/// <summary>
/// Callbacks a <see cref="ModRowViewModel"/> needs to fulfil its context-menu actions without
/// taking direct dependencies on every Core service itself — supplied by the owning
/// <see cref="ModListViewModel"/> and shared across all rows.
/// </summary>
public sealed class ModRowActions
{
    public required Func<IReadOnlyList<SavegameInfo>> DiscoverSavegames { get; init; }

    public required Func<string, IReadOnlyList<ModLoadEntry>> ReadLoadOrder { get; init; }

    public required Func<ModFileInfo, Task<IReadOnlyList<StoreItemDetail>>> ScanContent { get; init; }

    public required Action RequestRescan { get; init; }

    public required Action<string> NotifyInfo { get; init; }

    public required Action<string> NotifyWarning { get; init; }

    public required Action<string> NotifyError { get; init; }
}
