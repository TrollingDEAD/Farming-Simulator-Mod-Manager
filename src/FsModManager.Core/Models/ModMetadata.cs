namespace FsModManager.Core.Models;

/// <summary>A &lt;storeItem&gt; declaration from modDesc.xml.</summary>
public sealed record ModStoreItem(string? XmlFilename, string? Name);

/// <summary>
/// Whether a mod is compatible with crossplay (e.g. Steam/Epic/Gog/console players in the same
/// session). <see cref="Unknown"/> is the default for every mod today — see the remarks on
/// <see cref="ModMetadata.CrossplayStatus"/> for why.
/// </summary>
public enum CrossplayStatus
{
    Unknown,
    Supported,
    NotSupported,
}

/// <summary>The kind of custom type a mod can declare (fillType/fruitType/vehicleType).</summary>
public enum ModCustomTypeKind
{
    FillType,
    FruitType,
    VehicleType,
}

/// <summary>A custom fillType/fruitType/vehicleType declared by a mod.</summary>
public sealed record ModCustomType(ModCustomTypeKind Kind, string Name);

/// <summary>
/// Strongly typed, immutable representation of the data extracted from a mod's modDesc.xml.
/// Parsing never throws on malformed input; instead <see cref="IsValid"/> is set to false and
/// <see cref="Warnings"/> is populated with a human-readable explanation of what went wrong.
/// </summary>
public sealed record ModMetadata
{
    /// <summary>
    /// The mod's internal name, derived from the zip filename with any trailing version
    /// suffix and extension stripped (e.g. "CoolTractorMod_v1.2.3.zip" -&gt; "CoolTractorMod").
    /// This is the identity FS uses when loading mods, so two mods sharing it silently collide.
    /// </summary>
    public required string InternalName { get; init; }

    public string? DescVersion { get; init; }

    /// <summary>
    /// True only when the &lt;modDesc descVersion="..."&gt; attribute was present and parsed
    /// as an integer. False for missing/unparsable descVersion (see <see cref="Warnings"/>).
    /// </summary>
    public bool DescVersionParsed { get; init; }

    public string? Version { get; init; }

    /// <summary>
    /// The best available display name for the mod: the &lt;title&gt; entry matching the current
    /// UI culture, falling back to "en", then to whatever single &lt;title&gt; entry exists, then
    /// to <see cref="InternalName"/> if there's no &lt;title&gt; element at all. Always non-null.
    /// </summary>
    public required string DisplayTitle { get; init; }

    /// <summary>
    /// Raw bytes of the mod's icon, decoded from the DDS file referenced by &lt;iconFilename&gt;
    /// into a BMP byte buffer any UI framework can decode natively. Null when there's no
    /// &lt;iconFilename&gt;, the referenced file isn't in the zip, or decoding fails for any reason
    /// - callers should show a placeholder icon in that case. Intentionally byte[] (not a WPF
    /// type) so Core stays UI-framework-agnostic.
    /// </summary>
    public byte[]? IconImageData { get; init; }

    /// <summary>
    /// The best available description text: same &lt;lang&gt; dictionary/fallback selection as
    /// <see cref="DisplayTitle"/> (real modDesc.xml &lt;description&gt; elements confirmed to follow
    /// the identical &lt;en&gt;/&lt;de&gt;/... shape). Null when there's no usable &lt;description&gt;.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>Size in bytes of the source zip file (<see cref="FileInfo.Length"/>).</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>Last write time (UTC) of the source zip file.</summary>
    public DateTime LastModifiedUtc { get; init; }

    /// <summary>
    /// Best-effort classification of what this mod primarily contains. At parse time (before this
    /// mod's content has been scanned) this can only ever be <see cref="Models.ModKind.Map"/> (a
    /// confirmed &lt;maps&gt; block was found) or <see cref="Models.ModKind.Unknown"/> — refine with
    /// <see cref="ModKindClassifier.Classify"/> once storeItem kinds are available from a content scan.
    /// </summary>
    public ModKind ModKind { get; init; } = ModKind.Unknown;

    public string? Author { get; init; }

    public bool? MultiplayerSupported { get; init; }

    /// <summary>
    /// UNVERIFIED — needs manual confirmation against real GIANTS modding documentation before
    /// being trusted. There is no confirmed, documented modDesc.xml attribute or element for
    /// per-mod crossplay compatibility: it appears to be a ModHub/session-level classification
    /// (the ModHub website has a "crossplay" filter) rather than something mod authors declare
    /// in modDesc.xml. Inspecting several real installed FS25 mods' modDesc.xml files found no
    /// crossplay-related attribute or element anywhere. Parsing therefore always leaves this at
    /// <see cref="Models.CrossplayStatus.Unknown"/> — do not wire this to a guessed attribute name.
    /// </summary>
    public CrossplayStatus CrossplayStatus { get; init; } = CrossplayStatus.Unknown;

    /// <summary>
    /// Distinct storeItem category values (e.g. "tractorsMedium", "harvesters", "decoration"),
    /// verbatim as declared by the mod — these are GIANTS' internal identifiers, not an
    /// officially documented enum, so no validation/normalization is applied. In practice a
    /// storeItem's category isn't an attribute on &lt;storeItem&gt; itself but a &lt;category&gt;
    /// element inside the storeData of the XML file that storeItem's xmlFilename points to;
    /// mods with no storeItem, or storeItems whose category can't be resolved, contribute
    /// nothing here — this list is empty, never null, in that case.
    /// </summary>
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    /// <summary>Flat list of storeItem xmlFilename/id values, kept for simple lookups.</summary>
    public IReadOnlyList<string> StoreItemIds { get; init; } = Array.Empty<string>();

    /// <summary>Full storeItem declarations (xmlFilename + name), used for conflict detection.</summary>
    public IReadOnlyList<ModStoreItem> StoreItems { get; init; } = Array.Empty<ModStoreItem>();

    /// <summary>Custom fillType/fruitType/vehicleType names declared by this mod, if any.</summary>
    public IReadOnlyList<ModCustomType> CustomTypes { get; init; } = Array.Empty<ModCustomType>();

    /// <summary>
    /// &lt;specialization name="..."/&gt; entries declared under modDesc.xml's
    /// &lt;specializations&gt; block. Empty for the vast majority of mods that don't declare custom
    /// vehicle specializations. Two mods declaring the same name is a hard load-time crash in FS25
    /// (see DuplicateSpecializationNameDetector), not a subtle bug.
    /// </summary>
    public IReadOnlyList<string> SpecializationNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Filenames (matched by name only, any relative path) of this mod's zip entries that match a
    /// <see cref="FsModManager.Core.ModScanning.Conflicts.SharedDataFileDefinition"/> — shared base-game data files like
    /// fillTypes.xml/densityHeights.xml where FS25 silently applies only the last-loaded mod's copy.
    /// </summary>
    public IReadOnlyList<string> SharedDataFileMatches { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Declared dependency mod names from a non-standard &lt;dependencies&gt; block. This is not
    /// part of the official FS modDesc schema, so an empty list here is normal, not an error.
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Names of every top-level element found under &lt;modDesc&gt;. Used later for
    /// heuristic conflict/compatibility detection between mods.
    /// </summary>
    public IReadOnlyList<string> TopLevelElementNames { get; init; } = Array.Empty<string>();

    /// <summary>Source zip file this metadata was extracted from.</summary>
    public string? SourceFileName { get; init; }

    /// <summary>SHA-256 hash of the source zip file, used for duplicate/change detection.</summary>
    public string? FileHash { get; init; }

    public bool IsValid { get; init; } = true;

    /// <summary>Whether the source zip filename adheres to FS25 naming rules (letters, numbers, underscores only).</summary>
    public bool IsFilenameValid { get; init; } = true;

    /// <summary>Descriptive explanation if the filename contains invalid characters, spaces, or formatting.</summary>
    public string? InvalidFilenameReason { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static ModMetadata Invalid(string internalName, IReadOnlyList<string> warnings, string? sourceFileName = null)
    {
        var filenameValidation = !string.IsNullOrWhiteSpace(sourceFileName)
            ? FsModManager.Core.ModScanning.ModFilenameValidator.Validate(sourceFileName)
            : null;

        return new()
        {
            InternalName = internalName,
            DisplayTitle = internalName,
            IsValid = false,
            DescVersionParsed = false,
            Warnings = warnings,
            SourceFileName = sourceFileName,
            IsFilenameValid = filenameValidation?.IsValid ?? true,
            InvalidFilenameReason = filenameValidation?.Reason,
        };
    }
}
