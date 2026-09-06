using FsModManager.Core.Models;

namespace FsModManager.Core.Diagnostics;

/// <summary>The comparison signal that decided (or contributed to) a candidate's recommendation.</summary>
public enum ResolutionSignal
{
    Version,
    LastModified,
    StoreItemCount,
    FunctionCount,
    FileSize,
    ParseWarnings,
}

/// <summary>One candidate's outcome within a <see cref="DuplicateGroupRecommendation"/>: its
/// human-readable reasons (for UI display) and which signals produced them.</summary>
public sealed record CandidateAssessment(
    ModFileInfo Candidate,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<ResolutionSignal> WinningSignals);

/// <summary>
/// Result of analyzing one already-detected duplicate group. When <see cref="IsConfident"/> is
/// false, no single candidate should be presented as "recommended" — <see cref="UnclearReason"/>
/// explains why, and every candidate's own reasons are still available for the user to compare.
/// </summary>
public sealed record DuplicateGroupRecommendation(
    bool IsConfident,
    ModFileInfo? RecommendedKeeper,
    IReadOnlyList<ModFileInfo> RecommendedToRemove,
    IReadOnlyList<CandidateAssessment> CandidateAssessments,
    string? UnclearReason);

/// <summary>
/// Scores the candidates within one duplicate-mod group (as already detected by
/// <see cref="DuplicateModVersionDetector"/>) and produces a keeper recommendation with visible
/// per-signal reasoning — never auto-deletes, only recommends.
/// </summary>
public static class DuplicateModResolutionAnalyzer
{
    /// <summary>
    /// Analyzes one duplicate group. <paramref name="scannedContentByZipPath"/> should contain only
    /// ALREADY-cached <see cref="IModContentScanner"/> results (keyed by <see cref="ModFileInfo.ZipPath"/>) —
    /// callers must never force a fresh scan just to feed this method. If a candidate's zip path is
    /// missing from the dictionary (or the dictionary itself is null), the content-richness signals
    /// are omitted entirely for the whole group rather than treated as "0 items".
    /// </summary>
    public static DuplicateGroupRecommendation Analyze(
        IReadOnlyList<ModFileInfo> duplicateGroup,
        IReadOnlyDictionary<string, IReadOnlyList<Models.StoreItemDetail>>? scannedContentByZipPath = null)
    {
        if (duplicateGroup is null || duplicateGroup.Count < 2)
        {
            var trivialAssessments = (duplicateGroup ?? Array.Empty<ModFileInfo>())
                .Select(c => new CandidateAssessment(c, Array.Empty<string>(), Array.Empty<ResolutionSignal>()))
                .ToList();
            return new DuplicateGroupRecommendation(false, null, Array.Empty<ModFileInfo>(), trivialAssessments,
                "Not enough candidates to compare.");
        }

        var reasons = duplicateGroup.ToDictionary(c => c, _ => new List<string>());
        var winningSignals = duplicateGroup.ToDictionary(c => c, _ => new List<ResolutionSignal>());
        var decidedBy = new HashSet<ModFileInfo>();

        void RecordWin(ModFileInfo winner, ResolutionSignal signal, string reason)
        {
            reasons[winner].Add(reason);
            winningSignals[winner].Add(signal);
            decidedBy.Add(winner);
        }

        EvaluateVersionSignal(duplicateGroup, RecordWin);
        EvaluateLastModifiedSignal(duplicateGroup, RecordWin);
        EvaluateContentRichnessSignals(duplicateGroup, scannedContentByZipPath, RecordWin);
        EvaluateParseWarningsSignal(duplicateGroup, RecordWin);

        // File size is a last-resort weak signal, only consulted when nothing else differentiated the group.
        if (decidedBy.Count == 0)
        {
            EvaluateFileSizeSignal(duplicateGroup, RecordWin);
        }

        var assessments = duplicateGroup
            .Select(c => new CandidateAssessment(c, reasons[c].AsReadOnly(), winningSignals[c].AsReadOnly()))
            .ToList();

        if (decidedBy.Count == 0)
        {
            return new DuplicateGroupRecommendation(false, null, Array.Empty<ModFileInfo>(), assessments,
                "No signal could differentiate these copies — review them manually.");
        }

        if (decidedBy.Count > 1)
        {
            return new DuplicateGroupRecommendation(false, null, Array.Empty<ModFileInfo>(), assessments,
                "Unclear — signals disagree between candidates. Review each copy's reasons before deciding.");
        }

        var keeper = decidedBy.Single();
        var toRemove = duplicateGroup.Where(candidate => !candidate.Equals(keeper)).ToList();
        return new DuplicateGroupRecommendation(true, keeper, toRemove, assessments, null);
    }

    private static void EvaluateVersionSignal(
        IReadOnlyList<ModFileInfo> group,
        Action<ModFileInfo, ResolutionSignal, string> recordWin)
    {
        var parts = new Dictionary<ModFileInfo, int[]>();
        foreach (var candidate in group)
        {
            if (!TryParseVersionParts(candidate.Version, out var candidateParts))
            {
                // Any unparseable version anywhere in the group disqualifies this signal entirely
                // rather than guessing at a comparison for the rest.
                return;
            }
            parts[candidate] = candidateParts;
        }

        var ordered = group.OrderByDescending(c => parts[c], VersionPartsComparer.Instance).ToList();
        var best = ordered[0];
        var tie = group.Count(c => VersionPartsComparer.Instance.Compare(parts[c], parts[best]) == 0) > 1;
        if (tie)
        {
            return;
        }

        var runnerUp = ordered[1];
        recordWin(best, ResolutionSignal.Version, $"Newer version ({best.Version} vs {runnerUp.Version})");
    }

    private static void EvaluateLastModifiedSignal(
        IReadOnlyList<ModFileInfo> group,
        Action<ModFileInfo, ResolutionSignal, string> recordWin)
    {
        if (group.Any(c => c.LastModifiedUtc == default))
        {
            return;
        }

        var ordered = group.OrderByDescending(c => c.LastModifiedUtc).ToList();
        var best = ordered[0];
        var tie = group.Count(c => c.LastModifiedUtc == best.LastModifiedUtc) > 1;
        if (tie)
        {
            return;
        }

        var runnerUp = ordered[1];
        recordWin(best, ResolutionSignal.LastModified,
            $"More recently modified ({best.LastModifiedUtc.ToLocalTime():d} vs {runnerUp.LastModifiedUtc.ToLocalTime():d})");
    }

    private static void EvaluateContentRichnessSignals(
        IReadOnlyList<ModFileInfo> group,
        IReadOnlyDictionary<string, IReadOnlyList<Models.StoreItemDetail>>? scannedContentByZipPath,
        Action<ModFileInfo, ResolutionSignal, string> recordWin)
    {
        if (scannedContentByZipPath is null || group.Any(c => !scannedContentByZipPath.ContainsKey(c.ZipPath)))
        {
            // Not scanned for one or more candidates yet — omit the signal rather than treat it as 0-0.
            return;
        }

        var storeItemCounts = group.ToDictionary(c => c, c => scannedContentByZipPath[c.ZipPath].Count);
        if (storeItemCounts.Values.Distinct().Count() > 1)
        {
            var bestCount = storeItemCounts.Values.Max();
            var bestCandidates = storeItemCounts.Where(kv => kv.Value == bestCount).Select(kv => kv.Key).ToList();
            if (bestCandidates.Count == 1)
            {
                var winner = bestCandidates[0];
                var runnerUpCount = storeItemCounts.Where(kv => !kv.Key.Equals(winner)).Max(kv => kv.Value);
                recordWin(winner, ResolutionSignal.StoreItemCount,
                    $"More store items detected ({bestCount} vs {runnerUpCount})");
            }
        }

        var functionCounts = group.ToDictionary(c => c, c => scannedContentByZipPath[c.ZipPath].Sum(d => d.Functions.Count));
        if (functionCounts.Values.Distinct().Count() > 1)
        {
            var bestFunctions = functionCounts.Values.Max();
            var bestCandidates = functionCounts.Where(kv => kv.Value == bestFunctions).Select(kv => kv.Key).ToList();
            if (bestCandidates.Count == 1)
            {
                var winner = bestCandidates[0];
                var runnerUpFunctions = functionCounts.Where(kv => !kv.Key.Equals(winner)).Max(kv => kv.Value);
                recordWin(winner, ResolutionSignal.FunctionCount,
                    $"More functionality detected ({bestFunctions} vs {runnerUpFunctions} functions)");
            }
        }
    }

    private static void EvaluateParseWarningsSignal(
        IReadOnlyList<ModFileInfo> group,
        Action<ModFileInfo, ResolutionSignal, string> recordWin)
    {
        var warningCounts = group.ToDictionary(c => c, c => c.ParseWarnings.Count);
        if (warningCounts.Values.Distinct().Count() <= 1)
        {
            return;
        }

        var bestWarningCount = warningCounts.Values.Min();
        var bestCandidates = warningCounts.Where(kv => kv.Value == bestWarningCount).Select(kv => kv.Key).ToList();
        if (bestCandidates.Count != 1)
        {
            return;
        }

        var winner = bestCandidates[0];
        var reasonText = bestWarningCount == 0
            ? "No parsing warnings"
            : $"Fewer parsing warnings ({bestWarningCount} vs {warningCounts.Where(kv => !kv.Key.Equals(winner)).Min(kv => kv.Value)})";
        recordWin(winner, ResolutionSignal.ParseWarnings, reasonText);
    }

    private static void EvaluateFileSizeSignal(
        IReadOnlyList<ModFileInfo> group,
        Action<ModFileInfo, ResolutionSignal, string> recordWin)
    {
        var sizes = group.ToDictionary(c => c, c => c.FileSizeBytes);
        if (sizes.Values.Distinct().Count() <= 1)
        {
            return;
        }

        var bestSize = sizes.Values.Max();
        var bestCandidates = sizes.Where(kv => kv.Value == bestSize).Select(kv => kv.Key).ToList();
        if (bestCandidates.Count != 1)
        {
            return;
        }

        var winner = bestCandidates[0];
        var runnerUpSize = sizes.Where(kv => !kv.Key.Equals(winner)).Max(kv => kv.Value);
        recordWin(winner, ResolutionSignal.FileSize,
            $"Larger, more complete file ({FormatFileSize(bestSize)} vs {FormatFileSize(runnerUpSize)})");
    }

    /// <summary>
    /// Tries to parse a version string into comparable numeric parts, first via <see cref="Version.TryParse"/>
    /// (requires at least major.minor), then falling back to a manual dotted-number split (e.g. a bare "3").
    /// Returns false — never guesses — for anything that doesn't cleanly parse as numeric dotted segments.
    /// </summary>
    private static bool TryParseVersionParts(string? version, out int[] parts)
    {
        parts = Array.Empty<int>();
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var trimmed = version.Trim().TrimStart('v', 'V');

        if (Version.TryParse(trimmed, out var systemVersion))
        {
            parts = new[]
            {
                systemVersion.Major,
                Math.Max(systemVersion.Minor, 0),
                Math.Max(systemVersion.Build, 0),
                Math.Max(systemVersion.Revision, 0),
            };
            return true;
        }

        var segments = trimmed.Split('.');
        var result = new int[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!int.TryParse(segments[i], out result[i]))
            {
                return false;
            }
        }

        if (result.Length == 0)
        {
            return false;
        }

        parts = result;
        return true;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "—";
        }

        string[] sizes = ["B", "KB", "MB", "GB"];
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    private sealed class VersionPartsComparer : IComparer<int[]>
    {
        public static readonly VersionPartsComparer Instance = new();

        public int Compare(int[]? x, int[]? y)
        {
            x ??= Array.Empty<int>();
            y ??= Array.Empty<int>();
            var length = Math.Max(x.Length, y.Length);
            for (var i = 0; i < length; i++)
            {
                var xv = i < x.Length ? x[i] : 0;
                var yv = i < y.Length ? y[i] : 0;
                var cmp = xv.CompareTo(yv);
                if (cmp != 0)
                {
                    return cmp;
                }
            }

            return 0;
        }
    }
}
