using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FsModManager.Core.Editing;
using FsModManager.Core.Models;
using FsModManager.Core.ModScanning.Conflicts;

namespace FsModManager.Core.Resolution;

/// <summary>
/// The rename that <see cref="StoreItemRenameResolver"/> would apply (or already applied) for a
/// duplicate-storeItem-filename conflict, shown to the user in a confirmation dialog before it runs.
/// </summary>
/// <param name="ModZipPath">The zip file that will be edited (the mod being renamed).</param>
/// <param name="ModInternalName">Internal name of the mod being renamed.</param>
/// <param name="OldEntryPath">The colliding entry path shared by both mods.</param>
/// <param name="NewEntryPath">The new, unique entry path the file will be moved to.</param>
public sealed record StoreItemRenamePlan(
    string ModZipPath,
    string ModInternalName,
    string OldEntryPath,
    string NewEntryPath);

/// <summary>
/// Fixes a "two mods coincidentally declare a &lt;storeItem&gt; with the same xmlFilename" conflict
/// (<see cref="DuplicateStoreItemDetector"/>) by renaming the colliding file inside ONE of the two
/// mods' zips to something unique and repointing that mod's own modDesc.xml storeItem reference at
/// it. Safe because the two mods' actual file content is different — they just happened to pick the
/// same filename — so renaming doesn't change either mod's own behavior, only which name it uses.
/// </summary>
public sealed class StoreItemRenameResolver
{
    private readonly IModFileEditor _editor;
    private readonly ILoadOrderPriorityCalculator _loadOrderCalculator;

    public StoreItemRenameResolver(IModFileEditor editor, ILoadOrderPriorityCalculator? loadOrderCalculator = null)
    {
        _editor = editor;
        _loadOrderCalculator = loadOrderCalculator ?? new LoadOrderPriorityCalculator();
    }

    /// <summary>
    /// Determines which mod would be renamed and to what, without touching any files. Returns null
    /// if <paramref name="conflict"/> isn't the duplicate-storeItem-filename shape this resolver
    /// handles, or if the mod that would be renamed has no known source zip path.
    /// </summary>
    public StoreItemRenamePlan? BuildPlan(ModConflict conflict, ModMetadata modA, ModMetadata modB)
    {
        if (string.IsNullOrWhiteSpace(conflict.ObjectAXmlFilename) ||
            string.IsNullOrWhiteSpace(conflict.ObjectBXmlFilename) ||
            !string.Equals(conflict.ObjectAXmlFilename, conflict.ObjectBXmlFilename, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var collidingPath = conflict.ObjectAXmlFilename!;

        // Always rename the alphabetically-second-loading mod (per LoadOrderPriorityCalculator's
        // established, forum-verified ordinal-ignore-case rule) so the outcome is predictable and
        // repeatable rather than an arbitrary pick between the two.
        var zipA = modA.SourceFileName ?? modA.InternalName;
        var zipB = modB.SourceFileName ?? modB.InternalName;
        var laterZip = _loadOrderCalculator.GetLaterLoadingMod(zipA, zipB);
        var modToRename = string.Equals(laterZip, zipA, StringComparison.OrdinalIgnoreCase) ? modA : modB;

        if (string.IsNullOrWhiteSpace(modToRename.SourceFileName))
        {
            return null;
        }

        var normalizedOld = NormalizeZipEntryPath(collidingPath);
        var dir = Path.GetDirectoryName(normalizedOld)?.Replace('\\', '/') ?? string.Empty;
        var extension = Path.GetExtension(normalizedOld);
        var baseName = Path.GetFileNameWithoutExtension(normalizedOld);
        var suffix = SanitizeForFilename(modToRename.InternalName);
        var newFileName = $"{baseName}_{suffix}{extension}";
        var newEntryPath = string.IsNullOrEmpty(dir) ? newFileName : $"{dir}/{newFileName}";

        return new StoreItemRenamePlan(modToRename.SourceFileName!, modToRename.InternalName, collidingPath, newEntryPath);
    }

    /// <summary>
    /// Applies the rename plan as a single atomic batch: writes the old entry's content under the
    /// new path, deletes the old entry, and updates modDesc.xml's storeItem xmlFilename reference —
    /// all in one <see cref="IModFileEditor.ApplyEditBatchAsync"/> call, so a validation failure
    /// rolls back the whole rename rather than leaving the mod half-renamed.
    /// </summary>
    public async Task<EditResult> ApplyAsync(
        ModConflict conflict,
        ModMetadata modA,
        ModMetadata modB,
        CancellationToken cancellationToken = default)
    {
        var plan = BuildPlan(conflict, modA, modB);
        if (plan is null)
        {
            return new EditResult(
                Success: false,
                FailureReason: "This conflict is not a duplicate storeItem filename collision, or the affected mod's source file could not be resolved.",
                BackupPath: string.Empty,
                WarningsBefore: Array.Empty<string>(),
                WarningsAfter: Array.Empty<string>());
        }

        if (!File.Exists(plan.ModZipPath))
        {
            return new EditResult(
                Success: false,
                FailureReason: $"Mod file not found: {plan.ModZipPath}",
                BackupPath: string.Empty,
                WarningsBefore: Array.Empty<string>(),
                WarningsAfter: Array.Empty<string>());
        }

        string oldEntryContent;
        string modDescContent;
        try
        {
            using var archive = ZipFile.OpenRead(plan.ModZipPath);
            var oldEntry = FindEntry(archive, plan.OldEntryPath);
            if (oldEntry is null)
            {
                return new EditResult(
                    Success: false,
                    FailureReason: $"Entry '{plan.OldEntryPath}' was not found in '{plan.ModZipPath}'.",
                    BackupPath: string.Empty,
                    WarningsBefore: Array.Empty<string>(),
                    WarningsAfter: Array.Empty<string>());
            }

            var modDescEntry = FindEntry(archive, "modDesc.xml");
            if (modDescEntry is null)
            {
                return new EditResult(
                    Success: false,
                    FailureReason: $"modDesc.xml was not found in '{plan.ModZipPath}'.",
                    BackupPath: string.Empty,
                    WarningsBefore: Array.Empty<string>(),
                    WarningsAfter: Array.Empty<string>());
            }

            using (var reader = new StreamReader(oldEntry.Open()))
            {
                oldEntryContent = await reader.ReadToEndAsync(cancellationToken);
            }

            using (var reader = new StreamReader(modDescEntry.Open()))
            {
                modDescContent = await reader.ReadToEndAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            return new EditResult(
                Success: false,
                FailureReason: $"Failed to read '{plan.ModZipPath}' before renaming: {ex.Message}",
                BackupPath: string.Empty,
                WarningsBefore: Array.Empty<string>(),
                WarningsAfter: Array.Empty<string>());
        }

        string updatedModDescContent;
        try
        {
            var doc = XDocument.Parse(modDescContent);
            var storeItems = doc.Root?.Element("storeItems")?.Elements("storeItem") ?? Enumerable.Empty<XElement>();
            var updatedAny = false;
            foreach (var storeItem in storeItems)
            {
                var xmlFilenameAttr = storeItem.Attribute("xmlFilename");
                if (xmlFilenameAttr is not null &&
                    string.Equals(NormalizeZipEntryPath(xmlFilenameAttr.Value), NormalizeZipEntryPath(plan.OldEntryPath), StringComparison.OrdinalIgnoreCase))
                {
                    xmlFilenameAttr.Value = plan.NewEntryPath;
                    updatedAny = true;
                }
            }

            if (!updatedAny)
            {
                return new EditResult(
                    Success: false,
                    FailureReason: $"No storeItem in modDesc.xml references '{plan.OldEntryPath}' — nothing to update.",
                    BackupPath: string.Empty,
                    WarningsBefore: Array.Empty<string>(),
                    WarningsAfter: Array.Empty<string>());
            }

            using var stringWriter = new Utf8StringWriter();
            doc.Save(stringWriter, SaveOptions.None);
            updatedModDescContent = stringWriter.ToString();
        }
        catch (Exception ex)
        {
            return new EditResult(
                Success: false,
                FailureReason: $"Failed to update modDesc.xml storeItem reference: {ex.Message}",
                BackupPath: string.Empty,
                WarningsBefore: Array.Empty<string>(),
                WarningsAfter: Array.Empty<string>());
        }

        var edits = new List<ModFileEdit>
        {
            new(plan.NewEntryPath, EditOperation.ReplaceEntryContent, oldEntryContent),
            new(plan.OldEntryPath, EditOperation.DeleteEntry),
            new("modDesc.xml", EditOperation.ReplaceEntryContent, updatedModDescContent),
        };

        return await _editor.ApplyEditBatchAsync(plan.ModZipPath, edits, "storeitem-rename-fix", cancellationToken);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string entryPath)
    {
        var normalized = NormalizeZipEntryPath(entryPath);
        return archive.Entries.FirstOrDefault(e => NormalizeZipEntryPath(e.FullName).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeZipEntryPath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string SanitizeForFilename(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
        var sanitized = new string(chars);
        return string.IsNullOrEmpty(sanitized) ? "renamed" : sanitized;
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
