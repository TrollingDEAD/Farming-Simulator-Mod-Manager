using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using FsModManager.Core.Launching;
using FsModManager.Core.Models;
using FsModManager.Core.ModScanning;
using FsModManager.Core.ModScanning.Parsing;
using FsModManager.Core.Savegames;

namespace FsModManager.Core.Editing;

/// <inheritdoc cref="IModFileEditor"/>
public sealed partial class ModFileEditor : IModFileEditor
{
    public const string DefaultBackupsFolderName = "mod_backups";

    [GeneratedRegex(@"[-_\s]v?\d+(?:[._]\d+){0,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffixRegex();

    [GeneratedRegex(@"^.+_(?<date>\d{4}-\d{2}-\d{2})_(?<time>\d{9}|\d{6})_(?<reason>.+)\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex BackupFileNameRegex();

    private static readonly string[] TimestampFormats = ["yyyy-MM-dd_HHmmssfff", "yyyy-MM-dd_HHmmss"];

    private readonly IGameProcessChecker _gameProcessChecker;
    private readonly IModDescParser _modDescParser;
    private readonly IModContentScanner? _contentScanner;
    private readonly string? _backupsRootOverride;

    // Count of mid-flight edit/revert operations (Interlocked-guarded) - backs IsEditInProgress,
    // which the self-update flow checks before applying an update + restarting the app.
    private int _activeEditCount;

    public ModFileEditor(
        IGameProcessChecker gameProcessChecker,
        IModDescParser modDescParser,
        IModContentScanner? contentScanner = null,
        string? backupsRootOverride = null)
    {
        _gameProcessChecker = gameProcessChecker;
        _modDescParser = modDescParser;
        _contentScanner = contentScanner;
        _backupsRootOverride = backupsRootOverride;
    }

    /// <inheritdoc />
    public bool IsEditInProgress => Volatile.Read(ref _activeEditCount) > 0;

    /// <inheritdoc />
    public Task<EditResult> ApplyEditAsync(
        string modZipPath,
        ModFileEdit edit,
        string reason = "auto-fix",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return ApplyEditBatchAsync(modZipPath, new[] { edit }, reason, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EditResult> ApplyEditBatchAsync(
        string modZipPath,
        IReadOnlyList<ModFileEdit> edits,
        string reason = "auto-fix",
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _activeEditCount);
        try
        {
            return await ApplyEditBatchCoreAsync(modZipPath, edits, reason, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _activeEditCount);
        }
    }

    private async Task<EditResult> ApplyEditBatchCoreAsync(
        string modZipPath,
        IReadOnlyList<ModFileEdit> edits,
        string reason = "auto-fix",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modZipPath);
        ArgumentNullException.ThrowIfNull(edits);

        if (edits.Count == 0)
        {
            return new EditResult(
                Success: false,
                FailureReason: "No edits were supplied.",
                BackupPath: string.Empty,
                WarningsBefore: Array.Empty<string>(),
                WarningsAfter: Array.Empty<string>());
        }

        EnsureGameNotRunning();

        if (!File.Exists(modZipPath))
        {
            return new EditResult(
                Success: false,
                FailureReason: $"Mod file not found: {modZipPath}",
                BackupPath: string.Empty,
                WarningsBefore: Array.Empty<string>(),
                WarningsAfter: Array.Empty<string>());
        }

        // 1. Gather baseline metadata and warnings before modification
        var beforeMetadata = await _modDescParser.ParseAsync(modZipPath, cancellationToken);
        var warningsBefore = new List<string>(beforeMetadata.Warnings);

        var isStoreItemFile = edits.Any(e => IsStoreItemReferencedFile(beforeMetadata, e.EntryPathInsideZip));
        if (isStoreItemFile && _contentScanner is not null)
        {
            var beforeModFileInfo = CreateModFileInfo(modZipPath, beforeMetadata);
            var beforeStoreItems = await _contentScanner.ScanContentAsync(beforeModFileInfo, cancellationToken);
            warningsBefore.AddRange(beforeStoreItems.SelectMany(s => s.Warnings));
        }

        // 2. Create timestamped backup of the original zip BEFORE touching anything
        var internalName = beforeMetadata.InternalName;
        var backupFolder = GetBackupFolderPath(modZipPath, internalName);
        var safeReason = SanitizeReason(reason);
        var baseFileName = Path.GetFileNameWithoutExtension(modZipPath);
        var timestamp = DateTime.Now;

        string backupPath;
        while (true)
        {
            backupPath = Path.Combine(backupFolder, $"{baseFileName}_{FormatTimestamp(timestamp)}_{safeReason}.zip");
            if (!File.Exists(backupPath))
            {
                break;
            }
            timestamp = timestamp.AddMilliseconds(1);
        }

        try
        {
            Directory.CreateDirectory(backupFolder);
            File.Copy(modZipPath, backupPath, overwrite: false);
        }
        catch (Exception ex)
        {
            // If the backup fails for ANY reason (disk full, permissions), abort the entire edit
            return new EditResult(
                Success: false,
                FailureReason: $"Failed to create backup before editing: {ex.Message}",
                BackupPath: string.Empty,
                WarningsBefore: warningsBefore,
                WarningsAfter: Array.Empty<string>());
        }

        // 3. Perform the edit on a temporary copy — never modify the live file in place
        var safeBaseName = new string(baseFileName.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        if (string.IsNullOrEmpty(safeBaseName)) safeBaseName = "mod";
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{safeBaseName}_{Guid.NewGuid():N}.zip");
        try
        {
            File.Copy(modZipPath, tempZipPath, overwrite: true);

            using (var archive = ZipFile.Open(tempZipPath, ZipArchiveMode.Update))
            {
                // Every edit in the batch is applied to the SAME open archive/temp copy, so a
                // multi-step change (e.g. write-new-entry + delete-old-entry + update a
                // cross-reference) is one atomic unit — validated and committed (or rolled back)
                // together, not as separate backup/validation cycles.
                foreach (var edit in edits)
                {
                    var normalizedTarget = NormalizeZipEntryPath(edit.EntryPathInsideZip);
                    var existingEntry = archive.Entries.FirstOrDefault(e =>
                        NormalizeZipEntryPath(e.FullName).Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase));

                    switch (edit.Operation)
                    {
                        case EditOperation.ReplaceEntryContent:
                            existingEntry?.Delete();
                            var newEntry = archive.CreateEntry(edit.EntryPathInsideZip, CompressionLevel.Optimal);
                            using (var writer = new StreamWriter(newEntry.Open(), new UTF8Encoding(false)))
                            {
                                writer.Write(edit.NewContent ?? string.Empty);
                            }
                            break;

                        case EditOperation.DeleteEntry:
                            existingEntry?.Delete();
                            break;

                        default:
                            throw new NotSupportedException($"Unsupported edit operation: {edit.Operation}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TryDeleteFile(tempZipPath);
            return new EditResult(
                Success: false,
                FailureReason: $"Failed to apply edit to archive: {ex.Message}",
                BackupPath: backupPath,
                WarningsBefore: warningsBefore,
                WarningsAfter: Array.Empty<string>());
        }

        // 4. Validate the modified temp zip copy (re-run ModDescParser and IModContentScanner)
        ModMetadata afterMetadata;
        try
        {
            afterMetadata = await _modDescParser.ParseAsync(tempZipPath, cancellationToken);
        }
        catch (Exception ex)
        {
            TryDeleteFile(tempZipPath);
            return new EditResult(
                Success: false,
                FailureReason: $"Validation failed: mod became unparseable after edit ({ex.Message})",
                BackupPath: backupPath,
                WarningsBefore: warningsBefore,
                WarningsAfter: Array.Empty<string>());
        }

        var warningsAfter = new List<string>(afterMetadata.Warnings);

        var isStoreItemFileAfter = isStoreItemFile || edits.Any(e => IsStoreItemReferencedFile(afterMetadata, e.EntryPathInsideZip));
        if (isStoreItemFileAfter && _contentScanner is not null)
        {
            _contentScanner.InvalidateCache(tempZipPath);
            var tempModFileInfo = CreateModFileInfo(tempZipPath, afterMetadata);
            var afterStoreItems = await _contentScanner.ScanContentAsync(tempModFileInfo, cancellationToken);
            _contentScanner.InvalidateCache(tempZipPath);
            warningsAfter.AddRange(afterStoreItems.SelectMany(s => s.Warnings));
        }

        // Check for parse errors or newly introduced warnings (ignoring temporary filename warnings)
        var newWarnings = warningsAfter
            .Where(w => !IsFilenameWarning(w))
            .Where(w => !warningsBefore.Contains(w, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var becameInvalid = !afterMetadata.IsValid && beforeMetadata.IsValid;

        if (becameInvalid || newWarnings.Count > 0)
        {
            TryDeleteFile(tempZipPath);
            var failureDetails = becameInvalid && newWarnings.Count == 0
                ? (afterMetadata.Warnings.Count > 0 ? string.Join("; ", afterMetadata.Warnings) : "Mod metadata is invalid.")
                : string.Join("; ", newWarnings);

            return new EditResult(
                Success: false,
                FailureReason: $"Validation failed: edit introduced new parse errors or warnings ({failureDetails})",
                BackupPath: backupPath,
                WarningsBefore: warningsBefore,
                WarningsAfter: warningsAfter);
        }

        // 5. Atomically replace the live mod zip with the validated temp copy
        try
        {
            AtomicallyReplaceFile(tempZipPath, modZipPath);
        }
        catch (Exception ex)
        {
            return new EditResult(
                Success: false,
                FailureReason: $"Failed to replace mod file: {ex.Message}",
                BackupPath: backupPath,
                WarningsBefore: warningsBefore,
                WarningsAfter: warningsAfter);
        }
        finally
        {
            TryDeleteFile(tempZipPath);
        }

        _contentScanner?.InvalidateCache(modZipPath);

        return new EditResult(
            Success: true,
            FailureReason: null,
            BackupPath: backupPath,
            WarningsBefore: warningsBefore,
            WarningsAfter: warningsAfter);
    }

    /// <inheritdoc />
    public async Task<EditResult> RevertToBackupAsync(
        string modZipPath,
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _activeEditCount);
        try
        {
            return await RevertToBackupCoreAsync(modZipPath, backupPath, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _activeEditCount);
        }
    }

    private async Task<EditResult> RevertToBackupCoreAsync(
        string modZipPath,
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modZipPath);
        ArgumentNullException.ThrowIfNull(backupPath);

        EnsureGameNotRunning();

        if (!File.Exists(backupPath))
        {
            return new EditResult(
                Success: false,
                FailureReason: $"Backup file not found: {backupPath}",
                BackupPath: backupPath,
                WarningsBefore: Array.Empty<string>(),
                WarningsAfter: Array.Empty<string>());
        }

        var beforeMetadata = File.Exists(modZipPath) ? await _modDescParser.ParseAsync(modZipPath, cancellationToken) : null;
        var warningsBefore = beforeMetadata?.Warnings ?? Array.Empty<string>();

        var backupMetadata = await _modDescParser.ParseAsync(backupPath, cancellationToken);
        var warningsAfter = backupMetadata.Warnings;

        try
        {
            AtomicallyReplaceFile(backupPath, modZipPath);
        }
        catch (Exception ex)
        {
            return new EditResult(
                Success: false,
                FailureReason: $"Failed to revert mod file to backup: {ex.Message}",
                BackupPath: backupPath,
                WarningsBefore: warningsBefore,
                WarningsAfter: warningsAfter);
        }

        _contentScanner?.InvalidateCache(modZipPath);

        return new EditResult(
            Success: true,
            FailureReason: null,
            BackupPath: backupPath,
            WarningsBefore: warningsBefore,
            WarningsAfter: warningsAfter);
    }

    /// <inheritdoc />
    public async Task<EditResult> ApplyXmlTransformAsync(
        string modZipPath,
        string entryPathInsideZip,
        Func<XDocument, XDocument> transform,
        string reason = "auto-fix",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modZipPath);
        ArgumentNullException.ThrowIfNull(entryPathInsideZip);
        ArgumentNullException.ThrowIfNull(transform);

        EnsureGameNotRunning();

        if (!File.Exists(modZipPath))
        {
            return new EditResult(
                Success: false,
                FailureReason: $"Mod file not found: {modZipPath}",
                BackupPath: string.Empty,
                WarningsBefore: Array.Empty<string>(),
                WarningsAfter: Array.Empty<string>());
        }

        XDocument doc;
        try
        {
            using var archive = ZipFile.OpenRead(modZipPath);
            var normalizedTarget = NormalizeZipEntryPath(entryPathInsideZip);
            var entry = archive.Entries.FirstOrDefault(e =>
                NormalizeZipEntryPath(e.FullName).Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                return new EditResult(
                    Success: false,
                    FailureReason: $"Entry '{entryPathInsideZip}' was not found in mod archive.",
                    BackupPath: string.Empty,
                    WarningsBefore: Array.Empty<string>(),
                    WarningsAfter: Array.Empty<string>());
            }

            using var stream = entry.Open();
            doc = XDocument.Load(stream);
        }
        catch (XmlException ex)
        {
            return new EditResult(
                Success: false,
                FailureReason: $"Entry '{entryPathInsideZip}' contains invalid XML: {ex.Message}",
                BackupPath: string.Empty,
                WarningsBefore: Array.Empty<string>(),
                WarningsAfter: Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new EditResult(
                Success: false,
                FailureReason: $"Failed to read entry '{entryPathInsideZip}': {ex.Message}",
                BackupPath: string.Empty,
                WarningsBefore: Array.Empty<string>(),
                WarningsAfter: Array.Empty<string>());
        }

        XDocument transformedDoc;
        try
        {
            transformedDoc = transform(doc);
            if (transformedDoc is null)
            {
                return new EditResult(
                    Success: false,
                    FailureReason: "XML transformation returned null.",
                    BackupPath: string.Empty,
                    WarningsBefore: Array.Empty<string>(),
                    WarningsAfter: Array.Empty<string>());
            }
        }
        catch (Exception ex)
        {
            return new EditResult(
                Success: false,
                FailureReason: $"XML transformation threw an exception: {ex.Message}",
                BackupPath: string.Empty,
                WarningsBefore: Array.Empty<string>(),
                WarningsAfter: Array.Empty<string>());
        }

        var stringWriter = new Utf8StringWriter();
        transformedDoc.Save(stringWriter, SaveOptions.None);
        var newContent = stringWriter.ToString();

        var edit = new ModFileEdit(entryPathInsideZip, EditOperation.ReplaceEntryContent, newContent);
        return await ApplyEditAsync(modZipPath, edit, reason, cancellationToken);
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    private static bool IsFilenameWarning(string warning) =>
        warning.StartsWith("Invalid mod filename", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyList<ModBackupInfo> ListBackups(string? internalModName = null, string? modsDirectory = null)
    {
        var root = _backupsRootOverride ?? ResolveBackupsRoot(modsDirectory);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return Array.Empty<ModBackupInfo>();
        }

        var results = new List<ModBackupInfo>();

        if (!string.IsNullOrWhiteSpace(internalModName))
        {
            var modFolder = Path.Combine(root, internalModName);
            if (Directory.Exists(modFolder))
            {
                EnumerateBackupsInFolder(modFolder, internalModName, modsDirectory, results);
            }
        }
        else
        {
            foreach (var modDir in Directory.EnumerateDirectories(root))
            {
                var modName = Path.GetFileName(modDir);
                EnumerateBackupsInFolder(modDir, modName, modsDirectory, results);
            }
        }

        return results
            .OrderByDescending(b => b.CreatedUtc)
            .ThenByDescending(b => b.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <inheritdoc />
    public string GetBackupFolderPath(string modZipPath, string? internalModName = null)
    {
        ArgumentNullException.ThrowIfNull(modZipPath);
        var modName = internalModName ?? DeriveInternalName(modZipPath);
        var root = _backupsRootOverride ?? ResolveDefaultBackupsRootFromModPath(modZipPath);
        return Path.Combine(root, modName);
    }

    /// <summary>
    /// Atomically replaces <paramref name="destinationPath"/> with <paramref name="sourcePath"/>.
    /// Stages a temporary file in the destination's directory first so that the final rename/move
    /// occurs on the same filesystem volume, ensuring atomic replacement even if the source is on a different drive.
    /// </summary>
    public static void AtomicallyReplaceFile(string sourcePath, string destinationPath)
    {
        var targetDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (string.IsNullOrEmpty(targetDirectory))
        {
            throw new InvalidOperationException($"Cannot determine target directory for '{destinationPath}'.");
        }

        Directory.CreateDirectory(targetDirectory);

        var stagedPath = Path.Combine(targetDirectory, $"{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(sourcePath, stagedPath, overwrite: true);
            File.Move(stagedPath, destinationPath, overwrite: true);
        }
        catch
        {
            TryDeleteFile(stagedPath);
            throw;
        }
    }

    public static string DeriveInternalName(string zipFilePath)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(zipFilePath);
        var match = VersionSuffixRegex().Match(nameWithoutExtension);
        return match.Success ? nameWithoutExtension[..match.Index] : nameWithoutExtension;
    }

    private void EnsureGameNotRunning()
    {
        if (_gameProcessChecker.IsGameRunning())
        {
            throw new GameAlreadyRunningException();
        }
    }

    private static bool IsStoreItemReferencedFile(ModMetadata metadata, string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath)) return false;
        var normalized = NormalizeZipEntryPath(entryPath);
        return metadata.StoreItems.Any(s =>
            !string.IsNullOrWhiteSpace(s.XmlFilename) &&
            string.Equals(NormalizeZipEntryPath(s.XmlFilename!), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static ModFileInfo CreateModFileInfo(string zipPath, ModMetadata metadata) =>
        new(
            ZipPath: zipPath,
            InternalModName: metadata.InternalName,
            Version: metadata.Version,
            Author: metadata.Author,
            DescVersionParsed: metadata.DescVersionParsed,
            ParseWarnings: metadata.Warnings,
            DisplayTitle: metadata.DisplayTitle,
            IconImageData: metadata.IconImageData,
            MultiplayerSupported: metadata.MultiplayerSupported,
            Categories: metadata.Categories,
            CrossplayStatus: metadata.CrossplayStatus,
            Description: metadata.Description,
            DescVersion: metadata.DescVersion,
            FileSizeBytes: metadata.FileSizeBytes,
            LastModifiedUtc: metadata.LastModifiedUtc,
            ModKind: metadata.ModKind,
            IsFilenameValid: metadata.IsFilenameValid,
            InvalidFilenameReason: metadata.InvalidFilenameReason);

    private string? ResolveBackupsRoot(string? modsDirectory)
    {
        if (_backupsRootOverride is not null)
        {
            return _backupsRootOverride;
        }

        if (string.IsNullOrWhiteSpace(modsDirectory))
        {
            return null;
        }

        var trimmed = Path.TrimEndingDirectorySeparator(modsDirectory);
        var parent = Path.GetDirectoryName(trimmed);
        return Path.Combine(parent ?? trimmed, DefaultBackupsFolderName);
    }

    private static string ResolveDefaultBackupsRootFromModPath(string modZipPath)
    {
        var modsFolder = Path.GetDirectoryName(Path.GetFullPath(modZipPath));
        if (string.IsNullOrEmpty(modsFolder))
        {
            return Path.Combine(AppContext.BaseDirectory, DefaultBackupsFolderName);
        }

        var gameFolder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(modsFolder));
        return Path.Combine(gameFolder ?? modsFolder, DefaultBackupsFolderName);
    }

    private static void EnumerateBackupsInFolder(
        string folder,
        string internalModName,
        string? modsDirectory,
        List<ModBackupInfo> destination)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            if (TryParseFileName(fileName, out var createdLocal, out var reason))
            {
                string? originalModZipPath = null;
                if (!string.IsNullOrWhiteSpace(modsDirectory))
                {
                    var candidate = Path.Combine(modsDirectory, $"{internalModName}.zip");
                    if (File.Exists(candidate))
                    {
                        originalModZipPath = candidate;
                    }
                }

                destination.Add(new ModBackupInfo(
                    FilePath: file,
                    InternalModName: internalModName,
                    CreatedUtc: createdLocal.ToUniversalTime(),
                    Reason: reason,
                    SizeBytes: new FileInfo(file).Length,
                    OriginalModZipPath: originalModZipPath));
            }
        }
    }

    private static string NormalizeZipEntryPath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static string SanitizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "backup";
        }

        var chars = reason.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray();
        return new string(chars);
    }

    private static string FormatTimestamp(DateTime timestamp) =>
        timestamp.ToString("yyyy-MM-dd_HHmmssfff", CultureInfo.InvariantCulture);

    private static bool TryParseFileName(string fileName, out DateTime createdLocal, out string reason)
    {
        createdLocal = default;
        reason = string.Empty;

        var match = BackupFileNameRegex().Match(fileName);
        if (!match.Success)
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                $"{match.Groups["date"].Value}_{match.Groups["time"].Value}",
                TimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out createdLocal))
        {
            return false;
        }

        reason = match.Groups["reason"].Value;
        return true;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
