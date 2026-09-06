using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning.Conflicts;

/// <summary>Inspects a set of parsed mods and reports conflicts between them.</summary>
public interface IConflictDetector
{
    IReadOnlyList<ModConflict> Detect(IReadOnlyList<ModMetadata> mods);
}
