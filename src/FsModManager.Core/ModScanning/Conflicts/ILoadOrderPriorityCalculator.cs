namespace FsModManager.Core.ModScanning.Conflicts;

/// <summary>
/// Answers "who loads later" for FS25 mod zip filenames, so conflict detectors can name a
/// concrete winner instead of just reporting that an overlap exists.
/// </summary>
public interface ILoadOrderPriorityCalculator
{
    /// <summary>Orders the given zip file names/paths the way FS25 actually loads them.</summary>
    IReadOnlyList<string> OrderByLoadOrder(IReadOnlyList<string> zipFileNames);

    /// <summary>Returns whichever of the two given zip file names/paths loads later (wins).</summary>
    string GetLaterLoadingMod(string zipFileNameA, string zipFileNameB);
}
