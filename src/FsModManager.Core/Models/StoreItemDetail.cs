namespace FsModManager.Core.Models;

/// <summary>The broad kind of object a storeItem's referenced XML describes.</summary>
public enum StoreItemKind
{
    /// <summary>A drivable/motorized vehicle (e.g. &lt;vehicle type="combineDrivable"&gt;).</summary>
    Vehicle,

    /// <summary>A non-drivable attached implement (e.g. &lt;vehicle type="cutter"&gt;).</summary>
    Implement,

    /// <summary>A &lt;placeable&gt; (buildings, decoration, production points, etc.).</summary>
    Placeable,

    /// <summary>Root element/type didn't match any of the known shapes above.</summary>
    Unknown,
}

/// <summary>
/// One pre-formatted label/value spec pair (e.g. "Power" / "451", "Working Width" / "12"), taken
/// verbatim from a storeItem's &lt;specs&gt; element — see remarks on <see cref="StoreItemDetail"/>.
/// </summary>
public sealed record SpecEntry(string Label, string Value);

/// <summary>
/// Extracted shop/spec details for a single storeItem, read from the XML file its modDesc.xml
/// entry's xmlFilename points to.
/// </summary>
/// <remarks>
/// Real FS25 store XML (confirmed against several installed mods) does NOT pre-format spec values
/// with units the way this feature originally assumed — e.g. &lt;specs&gt;&lt;power&gt;451&lt;/power&gt;
/// &lt;maxSpeed&gt;40&lt;/maxSpeed&gt;&lt;/specs&gt; has raw numbers and no unit strings. Placeables
/// have no &lt;specs&gt; element at all. <see cref="SpecEntry.Label"/> is therefore a humanized
/// version of the spec's element name (e.g. "maxSpeed" -&gt; "Max Speed"), and
/// <see cref="SpecEntry.Value"/> is the element's raw text content with no unit guessing applied.
/// </remarks>
public sealed record StoreItemDetail(
    string XmlFilename,
    string ObjectDisplayName,
    decimal? Price,
    byte[]? ShopImageData,
    IReadOnlyList<SpecEntry> Specs,
    string? Category,
    StoreItemKind Kind)
{
    /// <summary>Best-effort parse warnings for this specific object, never fails the whole scan.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// &lt;storeData&gt;&lt;brand&gt; — confirmed present (sibling of &lt;price&gt;/&lt;category&gt;)
    /// on real vehicle storeData in several installed mods. Null when absent (e.g. most placeables).
    /// </summary>
    public string? Brand { get; init; }

    /// <summary>
    /// Sum of &lt;base&gt;&lt;components&gt;&lt;component mass="..."/&gt; — confirmed present on real
    /// vehicle/implement XML. Null when there's no &lt;components&gt; block (e.g. most placeables) or
    /// no component has a parseable mass.
    /// </summary>
    public decimal? MassKg { get; init; }

    /// <summary>
    /// &lt;storeData&gt;&lt;functions&gt;&lt;function&gt; — confirmed present on real storeData.
    /// Values are frequently untranslated l10n keys (e.g. "$l10n_function_decoration") rather than
    /// human-readable text; shown verbatim. Empty (never null) when absent.
    /// </summary>
    public IReadOnlyList<string> Functions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Raw numeric fill capacities gathered from every &lt;fillUnit fillTypes/fillTypeCategories
    /// capacity="..."/&gt; found anywhere in the object's XML — confirmed present (nested under
    /// &lt;fillUnitConfigurations&gt;&lt;fillUnitConfiguration&gt;&lt;fillUnits&gt;) but structure varies
    /// per configuration variant, so this is a flattened, best-effort list, not scoped to a single
    /// "default" configuration. Empty (never null) when no fillUnit elements are found.
    /// </summary>
    public IReadOnlyList<(string FillTypeName, decimal LiterCapacity)> RawFillCapacities { get; init; } =
        Array.Empty<(string FillTypeName, decimal LiterCapacity)>();

    /// <summary>
    /// UNVERIFIED — no consistent "required tractor power" element/attribute was found across the
    /// sample implement mods inspected (checked several real towed implements' XML). Always null;
    /// left in place per the "best-effort nullable field" guidance rather than removed, in case a
    /// future mod sample reveals a real pattern worth wiring up.
    /// </summary>
    public decimal? RequiredPowerHp { get; init; }

    /// <summary>
    /// Configuration groups (e.g. ("Design", 3), ("Wheel", 2)) — confirmed present as a generic,
    /// recurring XML pattern: any &lt;xConfigurations&gt; element containing one or more
    /// &lt;xConfiguration&gt; children (e.g. &lt;designConfigurations&gt;, &lt;wheelConfigurations&gt;,
    /// &lt;motorConfigurations&gt;). GroupName is the humanized element name with the "Configurations"
    /// suffix stripped. Empty (never null) when no such elements are found.
    /// </summary>
    public IReadOnlyList<(string GroupName, int OptionCount)> ConfigurationGroupCounts { get; init; } =
        Array.Empty<(string GroupName, int OptionCount)>();
}
