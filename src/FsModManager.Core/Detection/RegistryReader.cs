using System.Runtime.Versioning;
using Microsoft.Win32;

namespace FsModManager.Core.Detection;

/// <summary>Default <see cref="IRegistryReader"/> backed by the real Windows registry. Never throws.</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryReader : IRegistryReader
{
    public string? ReadString(RegistryHive hive, string subKeyPath, string valueName)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(subKeyPath);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception)
        {
            // Missing keys, access denied, non-string value types — all mean "not found".
            return null;
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string subKeyPath)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Default).OpenSubKey(subKeyPath);
            return key?.GetSubKeyNames() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
