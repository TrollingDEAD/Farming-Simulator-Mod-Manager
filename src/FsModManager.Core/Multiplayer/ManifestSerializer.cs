using System.Text.Json;
using FsModManager.Core.Models;

namespace FsModManager.Core.Multiplayer;

/// <summary>
/// Writes/reads a <see cref="ModManifest"/> as a single portable, human-readable JSON file the
/// user can send to a friend (Discord/email/...) — e.g. "MyFarm_manifest.json".
///
/// Unlike the never-throw parsers elsewhere in Core (where a silent empty result is a safe
/// fallback), <see cref="LoadAsync"/> deliberately throws <see cref="InvalidDataException"/> for
/// files that aren't manifests or are malformed: silently "succeeding" with an empty or wrong
/// manifest would misreport the multiplayer comparison ("your friend has no mods"), which is far
/// worse than an honest error the UI can show.
/// </summary>
public sealed class ManifestSerializer
{
    /// <summary>Identifies the file as one of ours, so a wrong file pick fails with a clear message.</summary>
    public const string FormatMarker = "fsmodmanager-mod-manifest";

    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task SaveAsync(ModManifest manifest, string filePath, CancellationToken cancellationToken = default)
    {
        var dto = new ManifestDto
        {
            Format = FormatMarker,
            FormatVersion = CurrentFormatVersion,
            GeneratedUtc = manifest.GeneratedUtc,
            Label = manifest.Label,
            Entries = manifest.Entries.Select(e => new EntryDto
            {
                InternalModName = e.InternalModName,
                DisplayTitle = e.DisplayTitle,
                Version = e.Version,
                ContentHash = e.ContentHash,
                FileSizeBytes = e.FileSizeBytes,
            }).ToList(),
        };

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, dto, WriteOptions, cancellationToken);
    }

    /// <summary>Throws <see cref="InvalidDataException"/> for non-manifest/malformed/newer-version files.</summary>
    public async Task<ModManifest> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ManifestDto? dto;
        try
        {
            await using var stream = File.OpenRead(filePath);
            dto = await JsonSerializer.DeserializeAsync<ManifestDto>(stream, ReadOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"'{Path.GetFileName(filePath)}' is not a valid manifest file: {ex.Message}", ex);
        }

        if (dto is null || !string.Equals(dto.Format, FormatMarker, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"'{Path.GetFileName(filePath)}' is not an FS Mod Manager manifest file.");
        }

        if (dto.FormatVersion > CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"This manifest uses a newer format (v{dto.FormatVersion}) than this app understands (v{CurrentFormatVersion}). " +
                "Update FS Mod Manager, or ask your friend to re-export with a matching version.");
        }

        // Entries missing a name or a content hash can't participate in a comparison — skip them
        // rather than failing the load, so a hand-edited file still mostly works.
        var entries = (dto.Entries ?? new List<EntryDto>())
            .Where(e => !string.IsNullOrWhiteSpace(e.InternalModName) && !string.IsNullOrWhiteSpace(e.ContentHash))
            .Select(e => new ModManifestEntry(
                e.InternalModName!,
                string.IsNullOrWhiteSpace(e.DisplayTitle) ? e.InternalModName! : e.DisplayTitle!,
                e.Version,
                e.ContentHash!,
                e.FileSizeBytes))
            .ToList();

        return new ModManifest(dto.GeneratedUtc, dto.Label, entries);
    }

    // Wire-format DTOs, kept separate from the domain records so the file schema can evolve
    // (format markers, version bumps) without touching the records the rest of the app uses.
    private sealed class ManifestDto
    {
        public string? Format { get; set; }
        public int FormatVersion { get; set; }
        public DateTime GeneratedUtc { get; set; }
        public string? Label { get; set; }
        public List<EntryDto>? Entries { get; set; }
    }

    private sealed class EntryDto
    {
        public string? InternalModName { get; set; }
        public string? DisplayTitle { get; set; }
        public string? Version { get; set; }
        public string? ContentHash { get; set; }
        public long FileSizeBytes { get; set; }
    }
}
