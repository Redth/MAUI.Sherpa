using System.Diagnostics;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class PhysicalDeviceService : IPhysicalDeviceService
{
    private readonly ILoggingService _logger;
    private readonly IPlatformService _platform;

    public PhysicalDeviceService(ILoggingService logger, IPlatformService platform)
    {
        _logger = logger;
        _platform = platform;
    }

    public bool IsSupported => _platform.IsMacCatalyst || _platform.IsMacOS;

    public async Task<IReadOnlyList<PhysicalDevice>> GetDevicesAsync()
    {
        if (!IsSupported) return [];
        try
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "xcrun",
                    Arguments = $"devicectl list devices --json-output \"{tempFile}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return [];

                await process.WaitForExitAsync();

                if (!File.Exists(tempFile))
                    return [];

                var json = await File.ReadAllTextAsync(tempFile);
                return ParseDevices(json);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to list physical devices: {ex.Message}", ex);
            return [];
        }
    }

    public async Task<bool> InstallAppAsync(string identifier, string appPath, IProgress<string>? progress = null)
    {
        if (!IsSupported) return false;
        try
        {
            progress?.Report($"Installing app on device...");
            var psi = new ProcessStartInfo
            {
                FileName = "xcrun",
                Arguments = $"devicectl device install app --device {identifier} \"{appPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                progress?.Report("Failed to start devicectl");
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                progress?.Report("App installed successfully");
                return true;
            }

            progress?.Report($"Failed: {error}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to install app: {ex.Message}", ex);
            progress?.Report($"Failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> LaunchAppAsync(string identifier, string bundleId, IProgress<string>? progress = null)
    {
        if (!IsSupported) return false;
        try
        {
            progress?.Report($"Launching {bundleId}...");
            var psi = new ProcessStartInfo
            {
                FileName = "xcrun",
                Arguments = $"devicectl device process launch --device {identifier} {bundleId}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                progress?.Report("Failed to start devicectl");
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                progress?.Report("App launched successfully");
                return true;
            }

            progress?.Report($"Failed: {error}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to launch app: {ex.Message}", ex);
            progress?.Report($"Failed: {ex.Message}");
            return false;
        }
    }

    private IReadOnlyList<PhysicalDevice> ParseDevices(string json)
    {
        var devices = new List<PhysicalDevice>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("result", out var result) ||
                !result.TryGetProperty("devices", out var devicesArray))
                return devices;

            foreach (var device in devicesArray.EnumerateArray())
            {
                var dp = device.TryGetProperty("deviceProperties", out var dProps) ? dProps : default;
                var hp = device.TryGetProperty("hardwareProperties", out var hProps) ? hProps : default;
                var cp = device.TryGetProperty("connectionProperties", out var cProps) ? cProps : default;

                var platform = GetStr(hp, "platform") ?? "unknown";
                // Only include iOS devices, skip watchOS etc.
                if (platform != "iOS") continue;

                var identifier = GetStr(device, "identifier") ?? "";
                var udid = GetStr(hp, "udid") ?? identifier;
                var name = GetStr(dp, "name") ?? "Unknown";
                var model = GetStr(hp, "marketingName") ?? GetStr(hp, "productType") ?? "Unknown";
                var osVersion = GetStr(dp, "osVersionNumber") ?? "?";
                var transport = GetStr(cp, "transportType") ?? "unknown";
                var pairingState = GetStr(cp, "pairingState") ?? "unknown";
                var tunnelState = GetStr(cp, "tunnelState") ?? "unknown";

                devices.Add(new PhysicalDevice(
                    identifier, udid, name, model, platform,
                    GetStr(hp, "deviceType") ?? "iPhone",
                    osVersion, transport, pairingState, tunnelState
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to parse devicectl output: {ex.Message}", ex);
        }

        return devices;
    }

    private static string? GetStr(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Undefined) return null;
        return element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }
}
