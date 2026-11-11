using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public sealed class AppleDevicePairingService : IAppleDevicePairingService
{
    private readonly IPlatformService _platform;
    private readonly ILoggingService _logger;
    private readonly IAppleDevicePairingRunner _runner;
    private readonly SemaphoreSlim _operation = new(1, 1);

    public AppleDevicePairingService(IPlatformService platform, ILoggingService logger)
        : this(platform, logger, new AppleDevicePairingRunner()) { }

    internal AppleDevicePairingService(
        IPlatformService platform, ILoggingService logger, IAppleDevicePairingRunner runner)
    {
        _platform = platform;
        _logger = logger;
        _runner = runner;
    }

    public bool IsSupported => _platform.IsMacOS || _platform.IsMacCatalyst;
    public bool IsWirelessSupported => IsSupported && _runner.IsWirelessAvailable;

    public async Task<IReadOnlyList<PhysicalDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
            throw new AppleDevicePairingException("Apple device pairing requires macOS and Xcode.");
        using var result = await _runner.RunDeviceCtlAsync(["list", "devices"], 20, cancellationToken);
        return PhysicalDeviceService.ParseDevices(result.RootElement);
    }

    public Task<ApplePairingResult> PairUsbAsync(string identifier, CancellationToken cancellationToken = default) =>
        RunOperationAsync(async ct =>
        {
            var device = await GetDeviceAsync(identifier, ct);
            if (device.IsPaired)
                return await VerifyDeviceAsync(device, ct);
            if (device.PairingState == "pairingInProgress")
                return new(ApplePairingState.Failed, "Apple is already pairing this device. Wait for its prompts, then refresh. No second pairing was started.", device);
            if (device.PairingState != "unpaired")
                return new(ApplePairingState.Failed, "Apple did not report this device as unpaired. Refresh its state before attempting to pair.", device);
            if (!device.IsWired)
                return new(ApplePairingState.Failed, "USB pairing requires a cable-connected device. Connect and unlock it, then refresh devices.", device);

            using var pair = await _runner.RunDeviceCtlAsync(
                ["manage", "pair", "--device", device.Identifier], 120, ct);
            return await VerifyCompletedPairingAsync(identifier, ct);
        }, cancellationToken);

    public Task<ApplePairingResult> VerifyAsync(string identifier, CancellationToken cancellationToken = default) =>
        RunOperationAsync(async ct => await VerifyDeviceAsync(await GetDeviceAsync(identifier, ct), ct), cancellationToken);

    public Task<ApplePairingResult> UnpairAsync(string identifier, CancellationToken cancellationToken = default) =>
        RunOperationAsync(async ct =>
        {
            var device = await GetDeviceAsync(identifier, ct);
            if (!device.IsPaired)
                return new(ApplePairingState.Failed, $"Apple does not report {device.Name} as paired, so nothing was unpaired.", device);

            using var unpair = await _runner.RunDeviceCtlAsync(
                ["manage", "unpair", "--device", device.Identifier], 60, ct);
            var remaining = (await GetDevicesAsync(ct))
                .FirstOrDefault(d => d.Identifier.Equals(device.Identifier, StringComparison.OrdinalIgnoreCase));
            return remaining?.IsPaired == true
                ? new(ApplePairingState.Failed, $"Apple completed the request, but still reports {device.Name} as paired. Refresh before trying again.", remaining)
                : new(ApplePairingState.Unpaired, $"This Mac no longer has a pairing with {device.Name}. Pair it again to use it for development.", remaining ?? device with { PairingState = "unpaired" });
        }, cancellationToken);

    public Task<ApplePairingResult> PairWirelessAsync(
        IProgress<ApplePairingProgress> progress, CancellationToken cancellationToken = default) =>
        RunOperationAsync(async ct =>
        {
            if (!IsWirelessSupported)
                return new(ApplePairingState.Failed, "The private Apple Wi-Fi feature is unavailable on this installation. Use USB pairing instead.");
            var before = (await GetDevicesAsync(ct)).Where(d => d.IsPaired)
                .Select(d => d.Identifier).ToHashSet(StringComparer.OrdinalIgnoreCase);
            await _runner.RunWirelessAsync(progress, 180, ct);
            progress.Report(new() { Stage = ApplePairingStage.Verifying });

            try
            {
                // Native success can precede publication of the new CoreDevice record.
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var candidates = (await GetDevicesAsync(ct))
                        .Where(d => d.IsPaired && !before.Contains(d.Identifier)).ToList();
                    if (candidates.Count > 1)
                        return new(ApplePairingState.CompletedUnverified,
                            "Apple completed pairing, but more than one device changed. Refresh devices and check the connection of the device you paired.");
                    if (candidates.Count == 1)
                        return await VerifyCompletedPairingAsync(candidates[0].Identifier, ct);
                    if (attempt < 4)
                        await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }
            }
            catch (Exception ex) when (ex is AppleDevicePairingException or InvalidOperationException or JsonException)
            {
                _logger.LogWarning($"Apple Wi-Fi pairing completed, but device verification failed ({ex.GetType().Name}).");
                return new(ApplePairingState.CompletedUnverified,
                    "Apple reported pairing completed, but device verification could not finish. Refresh devices and check the intended device before retrying.");
            }
            return new(ApplePairingState.CompletedUnverified,
                "Apple reported pairing completed, but a unique new paired device could not be identified. Refresh devices and check the intended device. Existing pairings were not removed.");
        }, cancellationToken);

    private async Task<ApplePairingResult> VerifyCompletedPairingAsync(string identifier, CancellationToken ct)
    {
        try
        {
            var verified = await VerifyDeviceAsync(await GetDeviceAsync(identifier, ct), ct);
            return verified.State == ApplePairingState.Failed
                ? new(ApplePairingState.CompletedUnverified,
                    $"Apple completed the pairing exchange, but its saved device state is not confirmed. {verified.Message}", verified.Device)
                : verified;
        }
        catch (Exception ex) when (ex is AppleDevicePairingException or InvalidOperationException or JsonException)
        {
            _logger.LogWarning($"Apple pairing completed, but device verification failed ({ex.GetType().Name}).");
            return new(ApplePairingState.CompletedUnverified,
                "Apple reported pairing completed, but device verification could not finish. Refresh devices and check the intended device before retrying.");
        }
    }

    private async Task<PhysicalDevice> GetDeviceAsync(string identifier, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 128 || identifier.Any(char.IsControl))
            throw new AppleDevicePairingException("Choose a device using its stable CoreDevice identifier.");
        var matches = (await GetDevicesAsync(ct))
            .Where(d => d.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
            throw new AppleDevicePairingException("The selected physical device is no longer uniquely discoverable. Refresh devices; names are not used to select a pairing target.");
        using var result = await _runner.RunDeviceCtlAsync(
            ["device", "info", "details", "--device", matches[0].Identifier], 20, ct);
        var device = PhysicalDeviceService.ParseDevice(result.RootElement.GetProperty("result"));
        if (device == null || !device.Identifier.Equals(matches[0].Identifier, StringComparison.OrdinalIgnoreCase))
            throw new AppleDevicePairingException("Apple returned details for an unexpected device. No pairing was started.");
        return device;
    }

    private async Task<ApplePairingResult> VerifyDeviceAsync(PhysicalDevice device, CancellationToken ct)
    {
        if (!device.IsPaired)
            return new(ApplePairingState.Failed, "Apple does not report this device as paired. Complete any device trust prompts yourself, then refresh.", device);
        try
        {
            using var probe = await _runner.RunDeviceCtlAsync(
                ["device", "info", "lockState", "--device", device.Identifier], 20, ct);
            var result = probe.RootElement.GetProperty("result");
            if (result.ValueKind != JsonValueKind.Object ||
                !IsBoolean(result, "passcodeRequired") || !IsBoolean(result, "unlockedSinceBoot"))
                throw new AppleDevicePairingException("Apple's live connection check returned an unexpected response.");

            var modeHint = device.DeveloperModeStatus == "disabled"
                ? " Enable Developer Mode on the device before deploying development apps." : "";
            return new(ApplePairingState.Connected,
                $"{device.Name} is paired and responding to Apple's live connection check.{modeHint}",
                device with { ConnectionState = "connected" });
        }
        catch (AppleDevicePairingException ex)
        {
            _logger.LogWarning("Apple device has a pairing record, but its live connection could not be verified.");
            return new(ApplePairingState.Paired,
                $"Apple has a pairing record for {device.Name}, but live access was not verified. {ex.Message} Unlock the device and check its cable or network.",
                device);
        }
    }

    private static bool IsBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private async Task<ApplePairingResult> RunOperationAsync(
        Func<CancellationToken, Task<ApplePairingResult>> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!await _operation.WaitAsync(0, ct))
            return new(ApplePairingState.Failed, "Another Apple pairing or connection check is active. Stop it or wait for it to finish.");
        try
        {
            if (!IsSupported)
                return new(ApplePairingState.Failed, "Apple device pairing requires macOS and Xcode.");
            return await action(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AppleDevicePairingException ex)
        {
            _logger.LogWarning("Apple device pairing could not finish. Refresh device state before retrying.");
            return new(ApplePairingState.Failed, ex.Message);
        }
        catch (Exception ex)
        {
            // Do not attach process output or sensitive event data to logs or telemetry.
            _logger.LogError($"Apple device pairing failed ({ex.GetType().Name}).");
            return new(ApplePairingState.Failed,
                "Apple pairing could not finish. Check your Xcode installation and device, then refresh before retrying.");
        }
        finally
        {
            _operation.Release();
        }
    }
}
