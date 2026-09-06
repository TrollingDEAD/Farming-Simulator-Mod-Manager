using FsModManager.Core.Models;

namespace FsModManager.Core.Diagnostics;

/// <summary>
/// One Warning/Error log entry after analysis: its plain-language explanation, matched category,
/// and whether it corresponds to a conflict already flagged by static scanning.
/// </summary>
public sealed record AnalyzedLogEntry(
    LogEntry Entry,
    ErrorCategory Category,
    string Explanation,
    bool MatchesKnownConflict,
    FixabilityTier Fixability,
    string? PatternId,
    IReadOnlyDictionary<string, string> Captures);

/// <summary>All analyzed entries attributed to one specific installed mod.</summary>
public sealed record ModLogGroup(
    string ModInternalName,
    string ModDisplayTitle,
    IReadOnlyList<AnalyzedLogEntry> Entries);

/// <summary>
/// Aggregated result of a full log analysis pass: totals, per-mod groupings, and an
/// "unattributed" bucket for entries that couldn't be tied to a specific mod.
/// </summary>
public sealed record LogAnalysisResult(
    bool LogFileExists,
    int TotalErrorCount,
    int TotalWarningCount,
    int AttributedCount,
    int UnattributedCount,
    IReadOnlyList<ModLogGroup> ModGroups,
    IReadOnlyList<AnalyzedLogEntry> UnattributedEntries)
{
    public static LogAnalysisResult Empty(bool logFileExists) => new(
        logFileExists, 0, 0, 0, 0, Array.Empty<ModLogGroup>(), Array.Empty<AnalyzedLogEntry>());
}

/// <summary>
/// Orchestrates the log analysis pipeline: parse log.txt, attribute each Warning/Error to a mod,
/// match it against known error patterns, and cross-reference against already-detected
/// <see cref="ModConflict"/>s so the UI can distinguish "this matches a conflict we already
/// detected" from "this is new information only visible from the log".
/// </summary>
public sealed class LogAnalyzer
{
    private readonly KnownErrorPatternMatcher _patternMatcher;

    public LogAnalyzer(KnownErrorPatternMatcher? patternMatcher = null)
    {
        _patternMatcher = patternMatcher ?? new KnownErrorPatternMatcher();
    }

    /// <summary>Reads and analyzes log.txt from <paramref name="logFilePath"/> (defaults to the standard FS25 location).</summary>
    public LogAnalysisResult Analyze(
        string? logFilePath,
        IReadOnlyList<ModMetadata> installedMods,
        IReadOnlyList<ModConflict>? knownConflicts = null)
    {
        var path = logFilePath ?? LogFileLocator.GetLogFilePath();
        if (!File.Exists(path))
        {
            return LogAnalysisResult.Empty(logFileExists: false);
        }

        var entries = LogFileParser.ParseFile(path);
        return AnalyzeEntries(entries, installedMods, knownConflicts);
    }

    /// <summary>Analyzes an already-parsed set of log entries (used directly by tests).</summary>
    public LogAnalysisResult AnalyzeEntries(
        IReadOnlyList<LogEntry> entries,
        IReadOnlyList<ModMetadata> installedMods,
        IReadOnlyList<ModConflict>? knownConflicts = null)
    {
        knownConflicts ??= Array.Empty<ModConflict>();
        var conflictedModNames = new HashSet<string>(
            knownConflicts.SelectMany(c => new[] { c.ModA, c.ModB }),
            StringComparer.OrdinalIgnoreCase);

        var groupedByMod = new Dictionary<string, List<AnalyzedLogEntry>>(StringComparer.OrdinalIgnoreCase);
        var unattributed = new List<AnalyzedLogEntry>();
        var errorCount = 0;
        var warningCount = 0;

        foreach (var entry in entries)
        {
            if (entry.Level is not (LogLevel.Warning or LogLevel.Error))
            {
                continue;
            }

            if (entry.Level == LogLevel.Error)
            {
                errorCount++;
            }
            else
            {
                warningCount++;
            }

            var (attributedName, analyzed) = AnalyzeEntry(entry, installedMods, conflictedModNames);
            AddToResultBucket(groupedByMod, unattributed, attributedName, analyzed);
        }

        var attributedCount = groupedByMod.Sum(kvp => kvp.Value.Count);

        var modGroups = groupedByMod
            .Select(kvp =>
            {
                var mod = installedMods.FirstOrDefault(m => string.Equals(m.InternalName, kvp.Key, StringComparison.OrdinalIgnoreCase));
                return new ModLogGroup(kvp.Key, mod?.DisplayTitle ?? kvp.Key, kvp.Value);
            })
            .OrderByDescending(g => g.Entries.Count)
            .ToList();

        return new LogAnalysisResult(
            LogFileExists: true,
            TotalErrorCount: errorCount,
            TotalWarningCount: warningCount,
            AttributedCount: attributedCount,
            UnattributedCount: unattributed.Count,
            ModGroups: modGroups,
            UnattributedEntries: unattributed);
    }

    private (string? AttributedName, AnalyzedLogEntry Analyzed) AnalyzeEntry(
        LogEntry entry,
        IReadOnlyList<ModMetadata> installedMods,
        IReadOnlySet<string> conflictedModNames)
    {
        var match = _patternMatcher.Match(entry);
        var attributedName = match.GetCapture("mod") ?? ModAttributionMatcher.Attribute(entry, installedMods);

        // Graphics/VRAM errors are explicitly NOT a mod issue, even if a path substring matches.
        if (match.Category == ErrorCategory.GraphicsUnrelated)
        {
            attributedName = null;
        }

        var matchesKnownConflict = attributedName is not null && conflictedModNames.Contains(attributedName);
        var analyzed = new AnalyzedLogEntry(
            entry with { AttributedModInternalName = attributedName },
            match.Category,
            match.Explanation,
            matchesKnownConflict,
            match.Fixability,
            match.PatternId,
            match.Captures ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        return (attributedName, analyzed);
    }

    private static void AddToResultBucket(
        Dictionary<string, List<AnalyzedLogEntry>> groupedByMod,
        List<AnalyzedLogEntry> unattributed,
        string? attributedName,
        AnalyzedLogEntry analyzed)
    {
        if (attributedName is null)
        {
            unattributed.Add(analyzed);
            return;
        }

        if (!groupedByMod.TryGetValue(attributedName, out var entries))
        {
            entries = new List<AnalyzedLogEntry>();
            groupedByMod[attributedName] = entries;
        }

        entries.Add(analyzed);
    }
}
