namespace FsModManager.Core.Data.Entities;

public enum ModSourceType
{
    Manual,
    ModHub,
    Generic,
}

/// <summary>Configuration record describing where an <see cref="InstalledMod"/> can be re-checked/downloaded from.</summary>
public sealed class ModSource
{
    public int Id { get; set; }

    public ModSourceType Type { get; set; }

    public required string Url { get; set; }

    /// <summary>
    /// Optional CSS/XPath selector or regex pattern used by <see cref="FsModManager.Core.Sources.ManualModSource"/>
    /// to scrape the current version string from <see cref="Url"/>.
    /// </summary>
    public string? VersionSelector { get; set; }

    public ICollection<InstalledMod> InstalledMods { get; set; } = new List<InstalledMod>();
}
