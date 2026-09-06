using System.IO.Compression;
using System.Text.RegularExpressions;
using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning.Conflicts;

/// <summary>
/// Flags Possible when two or more mods declare a Lua function with the same name at global scope.
/// FS mods share one Lua environment, so two mods defining "function DoStuff(...)" means the
/// second one loaded clobbers the first's implementation.
/// </summary>
/// <remarks>
/// This is a heuristic, not a Lua parser: it regex-scans for lines that look like a top-level
/// "function Name(" declaration, anchored at the start of the line so indented/nested (i.e. local
/// or table-member) functions are skipped. It cannot detect functions assigned via
/// "Name = function(...)" or functions built up dynamically. Because it has to unzip and scan
/// every .lua file in every mod, it's the slowest and least precise detector, so it's opt-in.
/// </remarks>
public sealed partial class LuaGlobalFunctionCollisionDetector : IConflictDetector
{
    [GeneratedRegex(@"^function\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Multiline)]
    private static partial Regex TopLevelFunctionRegex();

    private readonly bool _enabled;

    /// <param name="enabled">
    /// Set to false to skip this detector entirely (e.g. for very large mod sets where scanning
    /// every .lua file would be slow) — Detect then returns an empty list immediately.
    /// </param>
    public LuaGlobalFunctionCollisionDetector(bool enabled = true)
    {
        _enabled = enabled;
    }

    public IReadOnlyList<ModConflict> Detect(IReadOnlyList<ModMetadata> mods)
    {
        if (!_enabled)
        {
            return Array.Empty<ModConflict>();
        }

        var functionNameToMods = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var mod in mods)
        {
            foreach (var functionName in GetGlobalFunctionNames(mod))
            {
                if (!functionNameToMods.TryGetValue(functionName, out var declaringMods))
                {
                    declaringMods = new List<string>();
                    functionNameToMods[functionName] = declaringMods;
                }

                if (!declaringMods.Contains(mod.InternalName, StringComparer.OrdinalIgnoreCase))
                {
                    declaringMods.Add(mod.InternalName);
                }
            }
        }

        var conflicts = new List<ModConflict>();
        foreach (var (functionName, declaringMods) in functionNameToMods)
        {
            if (declaringMods.Count < 2)
            {
                continue;
            }

            foreach (var (modA, modB) in ConflictDetectionUtil.DistinctPairs(declaringMods))
            {
                conflicts.Add(new ModConflict(
                    modA,
                    null,
                    modB,
                    null,
                    ConflictSeverity.Possible,
                    $"Both mods declare a global Lua function named \"{functionName}\"."));
            }
        }

        return conflicts;
    }

    private static IEnumerable<string> GetGlobalFunctionNames(ModMetadata mod)
    {
        if (string.IsNullOrEmpty(mod.SourceFileName) || !File.Exists(mod.SourceFileName))
        {
            yield break;
        }

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(mod.SourceFileName);
        }
        catch
        {
            yield break;
        }

        using (archive)
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string content;
                try
                {
                    using var reader = new StreamReader(entry.Open());
                    content = reader.ReadToEnd();
                }
                catch
                {
                    continue;
                }

                foreach (Match match in TopLevelFunctionRegex().Matches(content))
                {
                    yield return match.Groups[1].Value;
                }
            }
        }
    }
}
