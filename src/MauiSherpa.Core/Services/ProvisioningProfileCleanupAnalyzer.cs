using System.Security.Cryptography.X509Certificates;
using AppleDev;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public static class ProvisioningProfileCleanupAnalyzer
{
    public static async Task<IReadOnlyList<InstalledProvisioningProfileAssessment>> AnalyzeAsync(
        IEnumerable<string> directories,
        IEnumerable<string?> installedCertificateSerialNumbers,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directories);
        ArgumentNullException.ThrowIfNull(installedCertificateSerialNumbers);

        var currentTime = now ?? DateTimeOffset.UtcNow;
        var installedSerials = installedCertificateSerialNumbers
            .Where(serial => !string.IsNullOrWhiteSpace(serial))
            .Select(serial => NormalizeSerialNumber(serial!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assessments = new List<InstalledProvisioningProfileAssessment>();

        foreach (var directory in directories.Distinct(GetPathComparer()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(directory))
                continue;

            var paths = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(IsProvisioningProfileFile)
                .OrderBy(path => path, GetPathComparer());

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                assessments.Add(await AnalyzeFileAsync(
                    path,
                    installedSerials,
                    currentTime,
                    cancellationToken).ConfigureAwait(false));
            }
        }

        MarkOlderVersions(assessments);

        return assessments
            .OrderByDescending(item => item.RecommendedForDeletion)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.CreationDate)
            .ToList();
    }

    public static Task<int> DeleteAsync(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var deleted = 0;
        foreach (var path in paths.Distinct(GetPathComparer()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsProvisioningProfileFile(path))
                throw new InvalidOperationException($"Refusing to delete non-profile file '{path}'.");

            if (!File.Exists(path))
                continue;

            File.Delete(path);
            deleted++;
        }

        return Task.FromResult(deleted);
    }

    private static async Task<InstalledProvisioningProfileAssessment> AnalyzeFileAsync(
        string path,
        IReadOnlySet<string> installedSerials,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(path);
        var location = GetLocationLabel(path);

        try
        {
            var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var profile = await ProvisioningProfiles.ParseAsync(content).ConfigureAwait(false);
            var provisionsAllDevices = ProvisioningProfileMetadata.ProvisionsAllDevices(content);
            var certificateSerials = GetCertificateSerialNumbers(profile);
            var hasMatchingCertificate = certificateSerials.Any(installedSerials.Contains);
            var isExpired = profile.ExpirationDate <= now;
            var reasons = new List<string>();

            if (isExpired)
                reasons.Add($"Expired {profile.ExpirationDate:MMM d, yyyy}");
            if (!hasMatchingCertificate)
                reasons.Add("No matching signing certificate with a private key is installed");

            return new InstalledProvisioningProfileAssessment(
                path,
                fileName,
                profile.Name ?? Path.GetFileNameWithoutExtension(path),
                profile.Uuid,
                GetBundleIdentifier(profile),
                profile.TeamIdentifier.FirstOrDefault(),
                GetProfileKind(profile, provisionsAllDevices),
                location,
                profile.CreationDate,
                profile.ExpirationDate,
                certificateSerials.Count,
                hasMatchingCertificate,
                isExpired,
                IsOlderVersion: false,
                IsReadable: true,
                RecommendedForDeletion: reasons.Count > 0,
                Reasons: reasons);
        }
        catch (Exception ex)
        {
            return new InstalledProvisioningProfileAssessment(
                path,
                fileName,
                Path.GetFileNameWithoutExtension(path),
                Uuid: null,
                BundleId: null,
                TeamIdentifier: null,
                ProfileKind: "Unknown",
                location,
                CreationDate: null,
                ExpirationDate: null,
                CertificateCount: 0,
                HasMatchingCertificate: false,
                IsExpired: false,
                IsOlderVersion: false,
                IsReadable: false,
                RecommendedForDeletion: true,
                Reasons: [$"Profile cannot be read: {ex.Message}"]);
        }
    }

    private static void MarkOlderVersions(List<InstalledProvisioningProfileAssessment> assessments)
    {
        var groups = assessments
            .Where(item => item.IsReadable && !string.IsNullOrWhiteSpace(item.BundleId))
            .GroupBy(
                item => $"{item.TeamIdentifier}\0{item.BundleId}\0{item.ProfileKind}\0{item.Name}",
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var distinctVersions = group
                .GroupBy(item => item.Uuid ?? item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(items => items.First())
                .ToList();
            if (distinctVersions.Count < 2)
                continue;

            var keeper = distinctVersions
                .OrderByDescending(item => !item.IsExpired && item.HasMatchingCertificate)
                .ThenByDescending(item => item.CreationDate)
                .ThenByDescending(item => item.ExpirationDate)
                .First();

            foreach (var older in distinctVersions.Where(item =>
                         !string.Equals(item.Uuid, keeper.Uuid, StringComparison.OrdinalIgnoreCase)))
            {
                for (var index = 0; index < assessments.Count; index++)
                {
                    var item = assessments[index];
                    if (!string.Equals(item.Uuid, older.Uuid, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var reasons = item.Reasons.ToList();
                    reasons.Add($"Older {item.ProfileKind.ToLowerInvariant()} profile for this app");
                    assessments[index] = item with
                    {
                        IsOlderVersion = true,
                        RecommendedForDeletion = true,
                        Reasons = reasons
                    };
                }
            }
        }
    }

    private static IReadOnlyList<string> GetCertificateSerialNumbers(ProvisioningProfileInfo profile)
    {
        var serials = new List<string>();
        foreach (var certificateData in profile.DeveloperCertificates)
        {
            try
            {
                using var certificate = X509CertificateLoader.LoadCertificate(certificateData);
                serials.Add(NormalizeSerialNumber(certificate.SerialNumber));
            }
            catch
            {
                // A malformed embedded certificate contributes no usable local identity match.
            }
        }

        return serials;
    }

    private static string? GetBundleIdentifier(ProvisioningProfileInfo profile)
    {
        if (!TryGetEntitlementString(profile, "application-identifier", out var applicationIdentifier) &&
            !TryGetEntitlementString(profile, "com.apple.application-identifier", out applicationIdentifier))
        {
            return null;
        }

        var teamPrefix = profile.ApplicationIdentifierPrefix.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(teamPrefix) &&
            applicationIdentifier.StartsWith($"{teamPrefix}.", StringComparison.OrdinalIgnoreCase))
        {
            return applicationIdentifier[(teamPrefix.Length + 1)..];
        }

        var separator = applicationIdentifier.IndexOf('.');
        return separator >= 0 ? applicationIdentifier[(separator + 1)..] : applicationIdentifier;
    }

    private static string GetProfileKind(
        ProvisioningProfileInfo profile,
        bool provisionsAllDevices)
    {
        var platform = profile.Platform.FirstOrDefault()?.ToUpperInvariant() switch
        {
            "OSX" or "MACOS" or "MAC_OS" => "macOS",
            "TVOS" => "tvOS",
            _ => "iOS"
        };

        if (profile.Entitlements.TryGetValue("get-task-allow", out var getTaskAllow) &&
            getTaskAllow is true)
        {
            return $"{platform} Development";
        }

        return profile.ProvisionedDevices.Length > 0
            ? $"{platform} Ad Hoc"
            : provisionsAllDevices
                ? $"{platform} Direct"
                : $"{platform} App Store";
    }

    private static bool TryGetEntitlementString(
        ProvisioningProfileInfo profile,
        string key,
        out string value)
    {
        if (profile.Entitlements.TryGetValue(key, out var entitlement) &&
            entitlement is string text &&
            !string.IsNullOrWhiteSpace(text))
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string NormalizeSerialNumber(string serialNumber)
    {
        var normalized = serialNumber.Trim().TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

    private static bool IsProvisioningProfileFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".mobileprovision", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".provisionprofile", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetLocationLabel(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        if (directory.Contains(
                Path.Combine("Developer", "Xcode", "UserData", "Provisioning Profiles"),
                StringComparison.OrdinalIgnoreCase))
        {
            return "Xcode 16+";
        }

        if (directory.Contains(
                Path.Combine("MobileDevice", "Provisioning Profiles"),
                StringComparison.OrdinalIgnoreCase))
        {
            return "Xcode 15 and earlier";
        }

        return directory;
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
