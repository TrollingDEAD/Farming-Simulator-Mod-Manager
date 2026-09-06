using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning.Conflicts;

/// <summary>
/// Runs a sequence of <see cref="IConflictDetector"/>s over a scanned mod set and returns one
/// aggregated, severity-sorted list (Critical first, then Likely, then Possible).
/// </summary>
public sealed class ConflictDetectionPipeline
{
    private readonly IReadOnlyList<IConflictDetector> _detectors;

    public ConflictDetectionPipeline(IReadOnlyList<IConflictDetector> detectors)
    {
        _detectors = detectors;
    }

    /// <summary>
    /// Builds the standard pipeline: duplicate internal names, duplicate storeItems, duplicate
    /// custom types, shared base-game data file overrides, duplicate specialization names, and
    /// (optionally) the slower Lua global-function-collision heuristic.
    /// </summary>
    public static ConflictDetectionPipeline CreateDefault(bool includeLuaDetector = true, ILoadOrderPriorityCalculator? loadOrderCalculator = null)
    {
        var calculator = loadOrderCalculator ?? new LoadOrderPriorityCalculator();

        var detectors = new List<IConflictDetector>
        {
            new DuplicateInternalNameDetector(),
            new DuplicateStoreItemDetector(),
            new DuplicateCustomTypeDetector(),
            new SharedDataFileOverrideDetector(calculator),
            new DuplicateSpecializationNameDetector(),
        };

        if (includeLuaDetector)
        {
            detectors.Add(new LuaGlobalFunctionCollisionDetector());
        }

        return new ConflictDetectionPipeline(detectors);
    }

    public IReadOnlyList<ModConflict> Detect(IReadOnlyList<ModMetadata> mods)
    {
        var conflicts = new List<ModConflict>();
        foreach (var detector in _detectors)
        {
            conflicts.AddRange(detector.Detect(mods));
        }

        return conflicts
            .OrderBy(c => c.Severity)
            .ToList();
    }
}
