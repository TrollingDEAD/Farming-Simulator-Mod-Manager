using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning.Parsing;

/// <summary>Parses a Farming Simulator mod's modDesc.xml out of its .zip archive.</summary>
public interface IModDescParser
{
    /// <summary>
    /// Parses the given mod zip file. Never throws for malformed/missing modDesc.xml —
    /// returns a <see cref="ModMetadata"/> with <c>IsValid = false</c> and parse warnings instead.
    /// </summary>
    Task<ModMetadata> ParseAsync(string zipFilePath, CancellationToken cancellationToken = default);
}
