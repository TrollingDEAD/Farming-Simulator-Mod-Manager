using Microsoft.Win32;

namespace FsModManager.Core.Detection;

/// <summary>
/// Minimal read-only registry access. Exists so registry-dependent detectors
/// can be mocked in tests later without touching real registry hives.
/// Implementations must not throw for missing keys/values.
/// </summary>
public interface IRegistryReader
{
    /// <summary>Returns the string value, or null if the key/value is missing or unreadable.</summary>
    string? ReadString(RegistryHive hive, string subKeyPath, string valueName);

    /// <summary>Returns the names of direct subkeys, or an empty list if the key is missing/unreadable.</summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string subKeyPath);
}
