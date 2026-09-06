namespace FsModManager.Core.Diagnostics;

/// <summary>Severity classification of a single log.txt line.</summary>
public enum LogLevel
{
    Info,
    Warning,
    Error,
    Unknown,
}

/// <summary>
/// One parsed line from log.txt. <see cref="Timestamp"/> is nullable because a real log.txt's
/// startup diagnostic block (hardware/render/physics info, roughly the first few hundred lines)
/// is never timestamped - timestamps only appear once the session is actually running.
/// </summary>
public sealed record LogEntry
{
    public DateTime? Timestamp { get; init; }

    public LogLevel Level { get; init; }

    public required string RawLine { get; init; }

    /// <summary>Filled in by <see cref="ModAttributionMatcher"/> during analysis; null if no mod could be matched.</summary>
    public string? AttributedModInternalName { get; init; }
}
