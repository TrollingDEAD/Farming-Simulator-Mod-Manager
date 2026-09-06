namespace FsModManager.Core.ModScanning.Conflicts;

internal static class ConflictDetectionUtil
{
    /// <summary>Yields every unordered pair of distinct items exactly once.</summary>
    public static IEnumerable<(T, T)> DistinctPairs<T>(IReadOnlyList<T> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            for (var j = i + 1; j < items.Count; j++)
            {
                yield return (items[i], items[j]);
            }
        }
    }
}
