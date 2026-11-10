using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class CertificateRenewalService : ICertificateRenewalService
{
    readonly IAppleConnectService _appleService;
    readonly ILoggingService _logger;

    public CertificateRenewalService(IAppleConnectService appleService, ILoggingService logger)
    {
        _appleService = appleService;
        _logger = logger;
    }

    public IReadOnlyList<AppleProfile> FindProfilesUsingCertificate(
        IEnumerable<AppleProfile> profiles,
        string certificateId)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (string.IsNullOrWhiteSpace(certificateId))
            return [];

        return profiles
            .Where(profile => profile.CertificateIds?.Contains(certificateId, StringComparer.Ordinal) == true)
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<AppleCertificate> FindReplacementCandidates(
        IEnumerable<AppleCertificate> certificates,
        AppleCertificate certificate,
        DateTime? now = null)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(certificate);

        var currentTime = now ?? DateTime.UtcNow;
        return certificates
            .Where(candidate => !string.Equals(candidate.Id, certificate.Id, StringComparison.Ordinal))
            .Where(candidate => !candidate.IsLocalOnly)
            .Where(candidate => string.Equals(
                candidate.CertificateType,
                certificate.CertificateType,
                StringComparison.OrdinalIgnoreCase))
            .Where(candidate => candidate.ExpirationDate > currentTime)
            .Where(candidate => candidate.ExpirationDate > certificate.ExpirationDate)
            .OrderByDescending(candidate => candidate.ExpirationDate)
            .ToList();
    }

    public async Task<ProfileRenewalResult> RepointProfileAsync(
        AppleProfile profile,
        string oldCertificateId,
        string newCertificateId,
        bool removeOldInstalledCopies,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldCertificateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newCertificateId);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report($"Reading devices and certificates of '{profile.Name}'...");
        var details = await _appleService.GetProfileDetailsAsync(profile.Id);

        var plan = BuildReplacementRequest(details, oldCertificateId, newCertificateId);
        foreach (var device in plan.DroppedDevices)
            progress?.Report($"Leaving out disabled device '{device.Name}'");

        // Nothing below can be cancelled halfway: the regeneration deletes the original.
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report($"Recreating '{profile.Name}' with the new certificate...");
        var regenerated = await _appleService.RegenerateProfileAsync(profile.Id, plan.Request);
        regenerated = regenerated with
        {
            BundleId = string.IsNullOrWhiteSpace(regenerated.BundleId)
                ? details.Profile.BundleId ?? profile.BundleId
                : regenerated.BundleId,
            CertificateIds = regenerated.CertificateIds ?? plan.Request.CertificateIds
        };
        _logger.LogInformation(
            $"Re-pointed profile '{profile.Name}' ({profile.Id} -> {regenerated.Id}) from certificate {oldCertificateId} to {newCertificateId}");

        string? installedPath = null;
        string? installError = null;
        try
        {
            progress?.Report($"Installing regenerated '{regenerated.Name}'...");
            installedPath = await _appleService.InstallProfileAsync(regenerated.Id);
        }
        catch (Exception ex)
        {
            installError = ex.Message;
            _logger.LogWarning($"Regenerated profile '{regenerated.Name}' could not be installed: {ex.Message}");
        }

        IReadOnlyList<string> removed = [];
        if (removeOldInstalledCopies && installError is null)
            removed = await RemoveOldInstalledCopiesAsync(profile, regenerated, progress, cancellationToken);

        return new ProfileRenewalResult(
            profile,
            regenerated,
            installedPath,
            installError,
            removed,
            plan.DroppedDevices);
    }

    async Task<IReadOnlyList<string>> RemoveOldInstalledCopiesAsync(
        AppleProfile original,
        AppleProfile regenerated,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(original.Uuid) ||
            string.Equals(original.Uuid, regenerated.Uuid, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        try
        {
            var paths = await _appleService.FindInstalledProfilePathsAsync(original.Uuid);
            if (paths.Count == 0)
                return [];

            await ProvisioningProfileCleanupAnalyzer.DeleteAsync(paths, cancellationToken);
            progress?.Report($"Removed {paths.Count} old installed copy(ies) of '{original.Name}'");
            return paths;
        }
        catch (Exception ex)
        {
            // The new profile is installed; a stale old copy is harmless to leave behind.
            _logger.LogWarning($"Could not remove old installed copies of '{original.Name}': {ex.Message}");
            progress?.Report($"Could not remove the old installed copy: {ex.Message}");
            return [];
        }
    }

    internal sealed record ReplacementPlan(
        AppleProfileCreateRequest Request,
        IReadOnlyList<AppleDevice> DroppedDevices);

    /// <summary>
    /// Builds the request that recreates a profile exactly as it is, except that
    /// <paramref name="oldCertificateId"/> becomes <paramref name="newCertificateId"/>.
    /// Other certificates stay. Disabled devices are left out because App Store Connect
    /// rejects them; a profile left with no devices is refused.
    /// </summary>
    internal static ReplacementPlan BuildReplacementRequest(
        AppleProfileDetails details,
        string oldCertificateId,
        string newCertificateId)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (!details.CertificateIds.Contains(oldCertificateId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{details.Profile.Name}' no longer uses the certificate being replaced, so it was left unchanged.");
        }

        var certificateIds = details.CertificateIds
            .Select(id => string.Equals(id, oldCertificateId, StringComparison.Ordinal) ? newCertificateId : id)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<string>? deviceIds = null;
        var dropped = new List<AppleDevice>();
        if (details.Devices.Count > 0)
        {
            deviceIds = [];
            foreach (var device in details.Devices)
            {
                if (string.Equals(device.Status, "DISABLED", StringComparison.OrdinalIgnoreCase))
                    dropped.Add(device);
                else
                    deviceIds.Add(device.Id);
            }

            if (deviceIds.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Every device in '{details.Profile.Name}' is disabled, so it can't be recreated. Enable a device first.");
            }
        }

        var request = new AppleProfileCreateRequest(
            details.Profile.Name,
            details.Profile.ProfileType,
            details.BundleIdResourceId,
            certificateIds,
            deviceIds);

        return new ReplacementPlan(request, dropped);
    }
}
