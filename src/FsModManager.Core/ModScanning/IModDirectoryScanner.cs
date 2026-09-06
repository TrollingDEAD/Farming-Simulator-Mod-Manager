using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning;

/// <summary>Scans a Farming Simulator mods folder and parses every mod zip found in it.</summary>
public interface IModDirectoryScanner
{
    /// <summary>
    /// Enumerates the .zip files directly inside <paramref name="modsFolderPath"/> (non-recursive —
    /// FS never nests mods) and parses each one. Non-zip files are skipped silently.
    /// </summary>
    Task<IReadOnlyList<ModMetadata>> ScanAsync(string modsFolderPath, CancellationToken cancellationToken = default);
}
