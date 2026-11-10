using System.Text;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public static class ProvisioningProfileExportService
{
    public static async Task<string> SaveAsync(
        string directory,
        AppleProfile profile,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0)
            throw new InvalidOperationException($"Apple returned no data for '{profile.Name}'.");

        var identifier = string.IsNullOrWhiteSpace(profile.Uuid) ? profile.Id : profile.Uuid;
        if (string.IsNullOrWhiteSpace(identifier))
            throw new InvalidOperationException("The profile has no identifier to use for export.");

        var name = SanitizeFileNamePart(profile.Name, "Profile", 120);
        var safeIdentifier = SanitizeFileNamePart(identifier, "Profile", 64);
        var extension = profile.Platform?.ToUpperInvariant() switch
        {
            "MACOS" or "MAC_OS" or "OSX" => ".provisionprofile",
            _ => ".mobileprovision"
        };
        var stem = $"{name} - {safeIdentifier}";
        Directory.CreateDirectory(directory);

        for (var copyNumber = 0; ; copyNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var suffix = copyNumber == 0 ? "" : $" ({copyNumber})";
            var targetPath = Path.Combine(directory, $"{stem}{suffix}{extension}");
            FileStream stream;
            try
            {
                stream = new FileStream(
                    targetPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException) when (File.Exists(targetPath) || Directory.Exists(targetPath))
            {
                continue;
            }

            try
            {
                await using (stream)
                {
                    await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                return targetPath;
            }
            catch
            {
                File.Delete(targetPath);
                throw;
            }
        }
    }

    private static string SanitizeFileNamePart(string value, string fallback, int maxBytes)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(character => char.IsControl(character) || invalidChars.Contains(character) ? '_' : character)
            .ToArray())
            .Trim()
            .TrimEnd('.');
        var result = new StringBuilder();
        var bytes = 0;
        foreach (var rune in sanitized.EnumerateRunes())
        {
            var text = rune.ToString();
            var runeBytes = Encoding.UTF8.GetByteCount(text.Normalize(NormalizationForm.FormD));
            if (bytes + runeBytes > maxBytes)
                break;
            result.Append(text);
            bytes += runeBytes;
        }

        var trimmed = result.ToString().Trim().TrimEnd('.');
        return string.IsNullOrEmpty(trimmed) ? fallback : trimmed;
    }
}
