namespace FsModManager.Core.Editing;

/// <summary>
/// Describes a single atomic modification to be applied to an entry inside a mod zip archive.
/// </summary>
/// <param name="EntryPathInsideZip">Relative path of the entry inside the zip (e.g. "modDesc.xml", "vehicles/tractor.xml").</param>
/// <param name="Operation">The operation to perform (<see cref="EditOperation.ReplaceEntryContent"/> or <see cref="EditOperation.DeleteEntry"/>).</param>
/// <param name="NewContent">The new text content when replacing an entry's content; null when deleting.</param>
public sealed record ModFileEdit(
    string EntryPathInsideZip,
    EditOperation Operation,
    string? NewContent = null);
