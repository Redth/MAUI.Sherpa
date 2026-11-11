using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class ProfileDeviceService : IProfileDeviceService
{
    static readonly string[] IosProfileTypes = ["IOS_APP_DEVELOPMENT", "IOS_APP_ADHOC"];
    static readonly string[] TvosProfileTypes = ["TVOS_APP_DEVELOPMENT", "TVOS_APP_ADHOC"];
    static readonly string[] MacProfileTypes = ["MAC_APP_DEVELOPMENT", "MAC_CATALYST_APP_DEVELOPMENT"];

    readonly IAppleConnectService _appleService;
    readonly ILoggingService _logger;

    public ProfileDeviceService(IAppleConnectService appleService, ILoggingService logger)
    {
        _appleService = appleService;
        _logger = logger;
    }

    public IReadOnlyList<AppleProfile> FindProfilesForDevice(
        IEnumerable<AppleProfile> profiles,
        AppleDevice device)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(device);

        var types = ProfileTypesFor(device);
        return profiles
            .Where(profile => types.Contains(profile.ProfileType, StringComparer.OrdinalIgnoreCase))
            .Where(profile => string.Equals(profile.State, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static IReadOnlyList<string> ProfileTypesFor(AppleDevice device)
    {
        var platform = device.Platform?.Replace("_", "").ToUpperInvariant();
        if (platform == "MACOS" || string.Equals(device.DeviceClass, "MAC", StringComparison.OrdinalIgnoreCase))
            return MacProfileTypes;
        if (string.Equals(device.DeviceClass, "APPLE_TV", StringComparison.OrdinalIgnoreCase))
            return TvosProfileTypes;
        return IosProfileTypes;
    }

    public async Task<ProfileDeviceResult> AddDeviceToProfileAsync(
        AppleProfile profile,
        AppleDevice device,
        bool removeOldInstalledCopies,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(device);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report($"Reading devices and certificates of '{profile.Name}'...");
        var details = await _appleService.GetProfileDetailsAsync(profile.Id);

        var plan = BuildAddDeviceRequest(details, device);
        if (plan is null)
        {
            progress?.Report($"'{profile.Name}' already includes '{device.Name}'");
            return new ProfileDeviceResult(profile, null, null, null, [], []);
        }

        foreach (var dropped in plan.DroppedDevices)
            progress?.Report($"Leaving out disabled device '{dropped.Name}'");

        // Nothing below can be cancelled halfway: the regeneration deletes the original.
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report($"Recreating '{profile.Name}' with '{device.Name}'...");
        var regenerated = await _appleService.RegenerateProfileAsync(profile.Id, plan.Request);
        regenerated = regenerated with
        {
            BundleId = string.IsNullOrWhiteSpace(regenerated.BundleId)
                ? details.Profile.BundleId ?? profile.BundleId
                : regenerated.BundleId,
            CertificateIds = regenerated.CertificateIds ?? plan.Request.CertificateIds
        };
        _logger.LogInformation(
            $"Added device '{device.Name}' ({device.Id}) to profile '{profile.Name}' ({profile.Id} -> {regenerated.Id})");

        var install = await ProfileRegeneration.InstallAsync(
            _appleService, _logger, profile, regenerated, removeOldInstalledCopies, progress, cancellationToken);

        return new ProfileDeviceResult(
            profile,
            regenerated,
            install.InstalledPath,
            install.InstallError,
            install.RemovedInstalledPaths,
            plan.DroppedDevices);
    }

    internal sealed record AddDevicePlan(
        AppleProfileCreateRequest Request,
        IReadOnlyList<AppleDevice> DroppedDevices);

    /// <summary>
    /// Builds the request that recreates a profile exactly as it is, plus <paramref name="device"/>.
    /// Disabled devices are left out because App Store Connect rejects them. Returns null when the
    /// profile already includes the device.
    /// </summary>
    internal static AddDevicePlan? BuildAddDeviceRequest(AppleProfileDetails details, AppleDevice device)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(device);

        if (string.Equals(device.Status, "DISABLED", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{device.Name}' is disabled, so it can't be added to '{details.Profile.Name}'. Enable it first.");
        }

        if (details.Devices.Any(existing => string.Equals(existing.Id, device.Id, StringComparison.Ordinal)))
            return null;

        var deviceIds = new List<string>();
        var dropped = new List<AppleDevice>();
        foreach (var existing in details.Devices)
        {
            if (string.Equals(existing.Status, "DISABLED", StringComparison.OrdinalIgnoreCase))
                dropped.Add(existing);
            else
                deviceIds.Add(existing.Id);
        }
        deviceIds.Add(device.Id);

        var request = new AppleProfileCreateRequest(
            details.Profile.Name,
            details.Profile.ProfileType,
            details.BundleIdResourceId,
            details.CertificateIds,
            deviceIds);

        return new AddDevicePlan(request, dropped);
    }
}
