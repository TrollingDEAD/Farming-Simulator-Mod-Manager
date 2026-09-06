using System.Globalization;
using System.Text.RegularExpressions;

namespace FsModManager.Core.Diagnostics;

/// <summary>
/// Parses raw log.txt lines into <see cref="LogEntry"/> values. Classification rules below were
/// verified against a real log.txt on a dev machine, NOT guessed from forum excerpts alone:
/// - Timestamp: "yyyy-MM-dd HH:mm:ss.fff" at the start of the line, optionally followed by a
///   game-time float and a "[--- N]" thread marker before the actual message (e.g.
///   "2026-08-24 22:15:21.029 8346.328 [--- 74] [ERROR] ..."). Startup diagnostic lines have no
///   timestamp at all.
/// - Errors are marked with a bracketed, uppercase "[ERROR]" tag.
/// - Warnings use an unbracketed "Warning:" or "Warning (path):" prefix (no "[WARNING]" form was
///   observed in the real file, but it's matched too defensively in case some warnings use it).
/// - Info lines use an unbracketed "Info:" prefix. Everything else (plain diagnostic lines with
///   no marker at all) classifies as Unknown, and is still kept (never dropped).
/// </summary>
public static class LogFileParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex TimestampRegex = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\s+",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex ErrorRegex = new(@"\[ERROR\]|\berror\b", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex WarningRegex = new(@"\[WARNING\]|\bwarning\b", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex InfoRegex = new(@"\binfo\b", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);

    /// <summary>Reads and parses every line of the file at <paramref name="path"/>. Empty list if the file doesn't exist.</summary>
    public static IReadOnlyList<LogEntry> ParseFile(string path)
    {
        return File.Exists(path) ? Parse(File.ReadLines(path)) : Array.Empty<LogEntry>();
    }

    public static IReadOnlyList<LogEntry> Parse(IEnumerable<string> lines)
    {
        var entries = new List<LogEntry>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            entries.Add(ParseLine(line));
        }

        return entries;
    }

    public static LogEntry ParseLine(string line)
    {
        DateTime? timestamp = null;
        var tsMatch = TimestampRegex.Match(line);
        if (tsMatch.Success &&
            DateTime.TryParse(tsMatch.Groups["ts"].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            timestamp = parsed;
        }

        return new LogEntry
        {
            Timestamp = timestamp,
            Level = ClassifyLevel(line),
            RawLine = line,
        };
    }

    private static LogLevel ClassifyLevel(string line)
    {
        if (ErrorRegex.IsMatch(line))
        {
            return LogLevel.Error;
        }

        if (WarningRegex.IsMatch(line))
        {
            return LogLevel.Warning;
        }

        if (InfoRegex.IsMatch(line))
        {
            return LogLevel.Info;
        }

        return LogLevel.Unknown;
    }
}
