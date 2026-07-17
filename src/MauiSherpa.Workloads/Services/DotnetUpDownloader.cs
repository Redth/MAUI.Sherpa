using System.Security.Cryptography;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Downloads the dotnetup binary from aka.ms and verifies its SHA-512 checksum,
/// mirroring the official get-dotnetup install scripts (download binary + companion
/// <c>.sha512</c>, compare, then mark executable).
///
/// The download itself is driven through an injected <see cref="HttpClient"/> so the
/// verify/compare logic stays unit-testable with a fake handler.
/// </summary>
public sealed class DotnetUpDownloader
{
    private readonly HttpClient _httpClient;

    public DotnetUpDownloader(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Downloads the dotnetup binary for <paramref name="rid"/> to <paramref name="destinationPath"/>,
    /// verifies its SHA-512 against the published checksum, and marks it executable on Unix.
    /// Throws <see cref="InvalidOperationException"/> if the checksum does not match.
    /// </summary>
    public async Task DownloadAndVerifyAsync(
        string rid,
        string destinationPath,
        string? quality = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!DotnetUpRuntimeIdentifier.IsSupportedRid(rid))
            throw new PlatformNotSupportedException($"dotnetup does not publish a binary for RID '{rid}'.");

        var binaryUrl = DotnetUpRuntimeIdentifier.GetDownloadUrl(rid, quality);
        var checksumUrl = DotnetUpRuntimeIdentifier.GetChecksumUrl(rid, quality);

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        progress?.Report($"Downloading checksum from {checksumUrl}…");
        var expectedHash = (await DownloadChecksumAsync(checksumUrl, cancellationToken).ConfigureAwait(false)).Checksum;

        progress?.Report($"Downloading dotnetup ({rid}) from {binaryUrl}…");
        var tempPath = destinationPath + ".download";
        try
        {
            string actualHash;
            using (var response = await _httpClient.GetAsync(
                       binaryUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = new FileStream(
                    tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await httpStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report("Verifying SHA-512 checksum…");
            await using (var verifyStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                actualHash = await ComputeSha512Async(verifyStream, cancellationToken).ConfigureAwait(false);
            }

            if (!HashesEqual(expectedHash, actualHash))
            {
                throw new InvalidOperationException(
                    "dotnetup download failed checksum verification. " +
                    $"Expected SHA-512 {expectedHash} but got {actualHash}.");
            }

            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            File.Move(tempPath, destinationPath);

            MarkExecutable(destinationPath);
            progress?.Report("dotnetup downloaded and verified.");
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }
        }
    }

    public async Task<DotnetUpPublishedArtifact> GetPublishedArtifactAsync(
        string rid,
        string? quality = null,
        CancellationToken cancellationToken = default)
    {
        if (!DotnetUpRuntimeIdentifier.IsSupportedRid(rid))
            throw new PlatformNotSupportedException($"dotnetup does not publish a binary for RID '{rid}'.");

        var checksumUrl = DotnetUpRuntimeIdentifier.GetChecksumUrl(rid, quality);
        var (checksum, effectiveUri) = await DownloadChecksumAsync(
            checksumUrl, cancellationToken).ConfigureAwait(false);
        var version = ExtractPublishedVersion(effectiveUri)
            ?? throw new InvalidOperationException(
                $"The published dotnetup version could not be determined from '{effectiveUri}'.");

        return new DotnetUpPublishedArtifact
        {
            Version = version,
            Sha512 = checksum
        };
    }

    public static string? ExtractPublishedVersion(Uri? artifactUri)
    {
        if (artifactUri is null)
            return null;

        var segments = artifactUri.AbsolutePath.Split(
            '/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 3 ||
            !segments[^1].StartsWith("dotnetup-", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Uri.UnescapeDataString(segments[^2]);
    }

    private async Task<(string Checksum, Uri? EffectiveUri)> DownloadChecksumAsync(
        string checksumUrl,
        CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(checksumUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return (NormalizeChecksum(raw), response.RequestMessage?.RequestUri);
    }

    /// <summary>
    /// Extracts the hex digest from a checksum file. The published file may contain just the
    /// hash, or "hash  filename" (sha512sum format).
    /// </summary>
    public static string NormalizeChecksum(string raw)
    {
        var firstToken = raw.Trim().Split(
            new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        return firstToken.Trim();
    }

    /// <summary>Computes the lowercase hex SHA-512 of a stream.</summary>
    public static async Task<string> ComputeSha512Async(Stream stream, CancellationToken ct = default)
    {
        using var sha = SHA512.Create();
        var hashBytes = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>Case-insensitive comparison of two hex checksum strings.</summary>
    public static bool HashesEqual(string expected, string actual) =>
        !string.IsNullOrEmpty(expected) &&
        !string.IsNullOrEmpty(actual) &&
        string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void MarkExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(
                path,
                mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch
        {
            // Best effort; the caller will surface a clear error if exec fails.
        }
    }
}
