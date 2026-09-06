namespace FsModManager.Core.Models;

public enum ConflictSeverity
{
    Critical,
    Likely,
    Possible,
}

/// <summary>
/// A detected incompatibility between two mods. <see cref="ObjectAXmlFilename"/>/
/// <see cref="ObjectBXmlFilename"/> attribute the conflict to a specific storeItem on each side
/// when the detector can determine that (e.g. duplicate storeItem xmlFilename); they're null for
/// conflict types that are inherently mod-wide (e.g. duplicate internal mod name).
/// </summary>
public sealed record ModConflict(
    string ModA,
    string? ObjectAXmlFilename,
    string ModB,
    string? ObjectBXmlFilename,
    ConflictSeverity Severity,
    string Description,
    string? SharedDataFileName = null);
