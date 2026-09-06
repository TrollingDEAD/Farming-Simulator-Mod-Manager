namespace FsModManager.Core.Data.Entities;

/// <summary>An installed Farming Simulator mod tracked by the mod manager.</summary>
public sealed class InstalledMod
{
    public int Id { get; set; }

    /// <summary>The mod's internal name, i.e. its zip/folder name convention (unique per FS install).</summary>
    public required string InternalName { get; set; }

    public string? Version { get; set; }

    public int? ModSourceId { get; set; }

    public ModSource? ModSource { get; set; }

    public DateTimeOffset? LastCheckedDate { get; set; }

    /// <summary>SHA-256 hash of the mod's zip file, used to detect file changes without re-parsing.</summary>
    public string? FileHash { get; set; }

    /// <summary>Absolute path to the mod zip file inside the FS25 mods folder.</summary>
    public required string FilePath { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Comma-separated storeItem ids extracted from modDesc.xml, used for conflict detection.</summary>
    public string? StoreItemIdsCsv { get; set; }

    public IReadOnlyList<string> StoreItemIds =>
        string.IsNullOrWhiteSpace(StoreItemIdsCsv)
            ? Array.Empty<string>()
            : StoreItemIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

}
