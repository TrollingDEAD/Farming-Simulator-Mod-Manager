namespace FsModManager.Core.ModScanning.Conflicts;

/// <summary>
/// FS25 loads mods from the mods folder strictly by zip filename order — comparison only ever
/// looks at the filename, not any containing folder in a full path.
/// </summary>
/// <remarks>
/// IMPORTANT — this was NOT implemented as a plain case-sensitive <see cref="StringComparer.Ordinal"/>
/// compare, even though that's what "ordinal, not culture-aware" would naively suggest. Verified
/// against the real forum-reported case this class is unit-tested against
/// ("FS25_zLiftablePalletsBales.zip" vs "FS25_ZZGuaranteedCropPrices.zip", where modders confirmed
/// the deliberate "ZZ" prefix successfully forced that mod to load last and win the override):
/// under case-SENSITIVE ordinal comparison, ASCII lowercase letters (97-122) sort AFTER all
/// uppercase letters (65-90), so "FS25_z..." would already sort after "FS25_ZZ..." even without the
/// "ZZ" prefix trick — which would make the deliberate zz-prefixing pointless and contradicts the
/// observed real-world outcome. Using <see cref="StringComparer.OrdinalIgnoreCase"/> instead (still
/// byte-value/ordinal-based, still NOT culture-aware — just case-insensitive) reproduces the
/// observed winner correctly. See <c>LoadOrderPriorityCalculatorTests</c> for the exact scenario.
/// </remarks>
public sealed class LoadOrderPriorityCalculator : ILoadOrderPriorityCalculator
{
    public IReadOnlyList<string> OrderByLoadOrder(IReadOnlyList<string> zipFileNames) =>
        zipFileNames
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public string GetLaterLoadingMod(string zipFileNameA, string zipFileNameB)
    {
        var comparison = StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(zipFileNameA), Path.GetFileName(zipFileNameB));
        return comparison >= 0 ? zipFileNameA : zipFileNameB;
    }
}
