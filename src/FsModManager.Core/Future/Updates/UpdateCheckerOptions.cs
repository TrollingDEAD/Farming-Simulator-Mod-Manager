namespace FsModManager.Core.Updates;

/// <summary>Configuration for <see cref="UpdateChecker"/>.</summary>
public sealed class UpdateCheckerOptions
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(6);
}
