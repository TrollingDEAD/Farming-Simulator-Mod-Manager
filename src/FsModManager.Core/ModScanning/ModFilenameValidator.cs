using System.Text.RegularExpressions;

namespace FsModManager.Core.ModScanning;

/// <summary>
/// Result of validating a mod zip filename against Farming Simulator 25's naming rules.
/// </summary>
public sealed record ModFilenameValidationResult(
    bool IsValid,
    string OriginalName,
    string BaseName,
    IReadOnlyList<char> InvalidCharacters,
    string? Reason,
    string? SuggestedFilename = null);

/// <summary>
/// Validates mod zip filenames against Farming Simulator 25 rules:
/// mod zip filenames must contain only ASCII letters, numbers, and underscores (no spaces, dashes, dots, or symbols).
/// </summary>
public static partial class ModFilenameValidator
{
    [GeneratedRegex(@"_+")]
    private static partial Regex MultipleUnderscoresRegex();

    /// <summary>
    /// Validates a mod zip filename or file path.
    /// </summary>
    public static ModFilenameValidationResult Validate(string? zipFileName)
    {
        if (string.IsNullOrWhiteSpace(zipFileName))
        {
            return new ModFilenameValidationResult(
                IsValid: false,
                OriginalName: string.Empty,
                BaseName: string.Empty,
                InvalidCharacters: Array.Empty<char>(),
                Reason: "Filename is empty.",
                SuggestedFilename: "FS25_Mod.zip");
        }

        var fileName = Path.GetFileName(zipFileName);
        var baseName = fileName;
        if (baseName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^4];
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            return new ModFilenameValidationResult(
                IsValid: false,
                OriginalName: fileName,
                BaseName: string.Empty,
                InvalidCharacters: Array.Empty<char>(),
                Reason: "Filename has no name component before .zip.",
                SuggestedFilename: "FS25_Mod.zip");
        }

        var invalidChars = new List<char>();
        foreach (var c in baseName)
        {
            if (!IsValidCharacter(c) && !invalidChars.Contains(c))
            {
                invalidChars.Add(c);
            }
        }

        if (invalidChars.Count == 0)
        {
            return new ModFilenameValidationResult(
                IsValid: true,
                OriginalName: fileName,
                BaseName: baseName,
                InvalidCharacters: Array.Empty<char>(),
                Reason: null,
                SuggestedFilename: fileName);
        }

        var reason = DescribeInvalidCharacters(invalidChars);
        var suggested = SanitizeFilename(fileName);

        return new ModFilenameValidationResult(
            IsValid: false,
            OriginalName: fileName,
            BaseName: baseName,
            InvalidCharacters: invalidChars,
            Reason: reason,
            SuggestedFilename: suggested);
    }

    /// <summary>
    /// Checks whether a single character is allowed in FS25 mod zip filenames (a-z, A-Z, 0-9, _).
    /// </summary>
    public static bool IsValidCharacter(char c) =>
        (c >= 'a' && c <= 'z')
        || (c >= 'A' && c <= 'Z')
        || (c >= '0' && c <= '9')
        || c == '_';

    /// <summary>
    /// Produces a sanitized, valid FS25 mod zip filename by replacing invalid characters with underscores.
    /// </summary>
    public static string SanitizeFilename(string? zipFileName)
    {
        if (string.IsNullOrWhiteSpace(zipFileName))
        {
            return "FS25_Mod.zip";
        }

        var fileName = Path.GetFileName(zipFileName);
        var baseName = fileName;
        if (baseName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^4];
        }

        var sanitizedChars = new char[baseName.Length];
        for (var i = 0; i < baseName.Length; i++)
        {
            sanitizedChars[i] = IsValidCharacter(baseName[i]) ? baseName[i] : '_';
        }

        var sanitized = new string(sanitizedChars);
        sanitized = MultipleUnderscoresRegex().Replace(sanitized, "_").Trim('_');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "FS25_Mod";
        }

        return sanitized + ".zip";
    }

    private static string DescribeInvalidCharacters(IReadOnlyList<char> chars)
    {
        var descriptions = new List<string>();
        foreach (var c in chars)
        {
            descriptions.Add(c switch
            {
                ' ' => "space (' ')",
                '(' => "parenthesis '('",
                ')' => "parenthesis ')'",
                '-' => "hyphen/dash ('-')",
                '.' => "period ('.')",
                '!' => "exclamation mark ('!')",
                '@' => "at sign ('@')",
                '#' => "hash ('#')",
                '$' => "dollar sign ('$')",
                '%' => "percent ('%')",
                '&' => "ampersand ('&')",
                '+' => "plus ('+')",
                '=' => "equals ('=')",
                '[' => "bracket '['",
                ']' => "bracket ']'",
                '{' => "brace '{'",
                '}' => "brace '}'",
                _ => $"character '{c}'",
            });
        }

        var charList = string.Join(", ", descriptions);
        return $"Contains invalid { (chars.Count > 1 ? "characters" : "character") }: {charList} (FS25 mod filenames must only contain letters, numbers, and underscores).";
    }
}
