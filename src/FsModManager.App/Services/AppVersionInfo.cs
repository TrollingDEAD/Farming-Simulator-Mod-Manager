using System.Reflection;

namespace FsModManager.App.Services;

/// <summary>
/// The one place the rest of the app asks "what version am I". Sourced from the
/// &lt;Version&gt; property in FsModManager.App.csproj (the single source of truth) - never
/// duplicate the version number anywhere else.
/// </summary>
public static class AppVersionInfo
{
    private static readonly Lazy<string> LazyCurrent = new(ReadVersion);

    /// <summary>Current app version as a clean SemVer string (e.g. "1.0.0"), no trailing ".0" padding.</summary>
    public static string Current => LazyCurrent.Value;

    private static string ReadVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();

        // AssemblyInformationalVersion mirrors <Version> from the csproj exactly. AssemblyVersion/
        // FileVersion instead get zero-padded to four parts (e.g. "1.0.0.0"), which isn't a clean SemVer string.
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Deterministic builds can append "+<sourcerevisionid>" - strip it if present.
            var plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
