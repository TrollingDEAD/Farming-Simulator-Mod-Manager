namespace FsModManager.Core.Editing;

/// <summary>
/// The type of atomic edit operation to perform on a specific entry inside a mod zip archive.
/// </summary>
public enum EditOperation
{
    /// <summary>Replaces the target entry's content with the provided new content, creating the entry if it didn't exist.</summary>
    ReplaceEntryContent,

    /// <summary>Deletes the target entry from the archive if present.</summary>
    DeleteEntry,
}
