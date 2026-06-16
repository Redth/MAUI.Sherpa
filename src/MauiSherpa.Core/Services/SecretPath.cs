using System.Text.RegularExpressions;

namespace MauiSherpa.Core.Services;

public readonly record struct SecretPath
{
    private static readonly Regex InvalidSegmentCharacters = new(@"[\u0000-\u001F]", RegexOptions.Compiled);

    public SecretPath(string folderPath, string key)
    {
        FolderPath = NormalizeFolderPath(folderPath);
        Key = NormalizeKey(key);
    }

    public string FolderPath { get; }

    public string Key { get; }

    public static SecretPath FromFlatKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Secret key cannot be empty.", nameof(key));

        var trimmed = key.Trim();
        var lastSeparator = trimmed.LastIndexOf('/');
        if (lastSeparator < 0)
            return new SecretPath("/", trimmed);

        if (lastSeparator == trimmed.Length - 1)
            throw new ArgumentException("Secret key cannot end with a path separator.", nameof(key));

        var folder = lastSeparator == 0 ? "/" : trimmed[..lastSeparator];
        var leaf = trimmed[(lastSeparator + 1)..];
        return new SecretPath(folder, leaf);
    }

    public string ToFlatKey()
    {
        return FolderPath == "/"
            ? Key
            : $"{FolderPath.TrimStart('/')}/{Key}";
    }

    public static string NormalizeFolderPath(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || folderPath == "/")
            return "/";

        var normalized = folderPath.Trim().Replace('\\', '/');
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;

        if (normalized.EndsWith('/') && normalized.Length > 1)
            normalized = normalized.TrimEnd('/');

        if (normalized.Contains("//", StringComparison.Ordinal))
            throw new ArgumentException("Secret folder paths cannot contain duplicate separators.", nameof(folderPath));

        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            ValidateSegment(segment, "Secret folder paths cannot contain empty, '.', '..', or control-character segments.");
        }

        return normalized;
    }

    public static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Secret key cannot be empty.", nameof(key));

        var normalized = key.Trim();
        ValidateSegment(normalized, "Secret keys cannot be '.', '..', or contain control characters.");

        if (normalized.Contains('/', StringComparison.Ordinal) || normalized.Contains('\\', StringComparison.Ordinal))
            throw new ArgumentException("Secret keys cannot contain path separators. Put hierarchy in the folder path.", nameof(key));

        return normalized;
    }

    private static void ValidateSegment(string segment, string message)
    {
        if (segment.Length == 0 ||
            segment == "." ||
            segment == ".." ||
            InvalidSegmentCharacters.IsMatch(segment))
        {
            throw new ArgumentException(message);
        }
    }
}
