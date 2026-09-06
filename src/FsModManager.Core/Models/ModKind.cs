namespace FsModManager.Core.Models;

/// <summary>The broad kind of content a mod primarily contains.</summary>
public enum ModKind
{
    /// <summary>modDesc.xml declares a &lt;maps&gt;&lt;map .../&gt; entry — confirmed reliable signal.</summary>
    Map,

    /// <summary>Every scanned storeItem is a <see cref="StoreItemKind.Vehicle"/>/<see cref="StoreItemKind.Implement"/>.</summary>
    VehiclePack,

    /// <summary>Every scanned storeItem is a <see cref="StoreItemKind.Placeable"/>.</summary>
    PlaceablePack,

    /// <summary>No storeItems and no map signal (e.g. a pure script/global-function mod).</summary>
    ScriptOnly,

    /// <summary>Content spans more than one of the buckets above.</summary>
    Mixed,

    /// <summary>Content hasn't been scanned yet and no map signal was found in modDesc.xml alone.</summary>
    Unknown,
}

/// <summary>
/// Classifies <see cref="ModKind"/> from the one confirmed modDesc.xml signal (a &lt;maps&gt; block)
/// plus, once available, the distribution of a mod's scanned storeItem kinds.
/// </summary>
public static class ModKindClassifier
{
    /// <summary>
    /// <paramref name="contentKinds"/> is null when the mod's content hasn't been scanned yet
    /// (parse-time only) — in that case the result is <see cref="ModKind.Map"/> when a map signal
    /// was found, else <see cref="ModKind.Unknown"/>. Once storeItem kinds are known, pass them in
    /// to refine further into VehiclePack/PlaceablePack/Mixed/ScriptOnly.
    /// </summary>
    public static ModKind Classify(bool hasMapSignal, IReadOnlyList<StoreItemKind>? contentKinds)
    {
        if (hasMapSignal)
        {
            return ModKind.Map;
        }

        if (contentKinds is null)
        {
            return ModKind.Unknown;
        }

        if (contentKinds.Count == 0)
        {
            return ModKind.ScriptOnly;
        }

        var buckets = contentKinds
            .Select(k => k switch
            {
                StoreItemKind.Vehicle or StoreItemKind.Implement => "vehicle",
                StoreItemKind.Placeable => "placeable",
                _ => "unknown",
            })
            .Distinct()
            .ToList();

        if (buckets.Count > 1)
        {
            return ModKind.Mixed;
        }

        return buckets[0] switch
        {
            "vehicle" => ModKind.VehiclePack,
            "placeable" => ModKind.PlaceablePack,
            _ => ModKind.Unknown,
        };
    }
}
