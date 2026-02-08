using System.Diagnostics;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Service for managing iOS/Apple simulators via xcrun simctl
/// </summary>
public class SimulatorService : ISimulatorService
{
    private readonly ILoggingService _logger;

    public SimulatorService(ILoggingService logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<SimulatorDevice>> GetSimulatorsAsync()
    {
        try
        {
            var json = await RunSimctlAsync("list devices -j");
            if (json == null) return Array.Empty<SimulatorDevice>();

            using var doc = JsonDocument.Parse(json);
            var devices = new List<SimulatorDevice>();

            // Build a runtime name lookup from runtimes list
            var runtimeNames = new Dictionary<string, string>();
            var runtimesJson = await RunSimctlAsync("list runtimes -j");
            if (runtimesJson != null)
            {
                using var rtDoc = JsonDocument.Parse(runtimesJson);
                if (rtDoc.RootElement.TryGetProperty("runtimes", out var runtimes))
                {
                    foreach (var rt in runtimes.EnumerateArray())
                    {
                        var id = rt.GetProperty("identifier").GetString();
                        var name = rt.GetProperty("name").GetString();
                        if (id != null && name != null)
                            runtimeNames[id] = name;
                    }
                }
            }

            // Build a device type to product family lookup
            var deviceTypeFamilies = new Dictionary<string, string>();
            var dtJson = await RunSimctlAsync("list devicetypes -j");
            if (dtJson != null)
            {
                using var dtDoc = JsonDocument.Parse(dtJson);
                if (dtDoc.RootElement.TryGetProperty("devicetypes", out var deviceTypes))
                {
                    foreach (var dt in deviceTypes.EnumerateArray())
                    {
                        var id = dt.GetProperty("identifier").GetString();
                        var family = dt.GetProperty("productFamily").GetString();
                        if (id != null && family != null)
                            deviceTypeFamilies[id] = family;
                    }
                }
            }

            if (doc.RootElement.TryGetProperty("devices", out var devicesElement))
            {
                foreach (var runtimeProp in devicesElement.EnumerateObject())
                {
                    var runtimeId = runtimeProp.Name;
                    runtimeNames.TryGetValue(runtimeId, out var runtimeName);

                    foreach (var device in runtimeProp.Value.EnumerateArray())
                    {
                        var deviceTypeId = device.GetProperty("deviceTypeIdentifier").GetString() ?? "";
                        deviceTypeFamilies.TryGetValue(deviceTypeId, out var productFamily);

                        string? lastBooted = null;
                        if (device.TryGetProperty("lastBootedAt", out var bootProp))
                            lastBooted = bootProp.GetString();

                        devices.Add(new SimulatorDevice(
                            Udid: device.GetProperty("udid").GetString()!,
                            Name: device.GetProperty("name").GetString()!,
                            State: device.GetProperty("state").GetString()!,
                            IsAvailable: device.TryGetProperty("isAvailable", out var avail) && avail.GetBoolean(),
                            DeviceTypeIdentifier: deviceTypeId,
                            RuntimeIdentifier: runtimeId,
                            Runtime: runtimeName,
                            ProductFamily: productFamily,
                            LastBootedAt: lastBooted
                        ));
                    }
                }
            }

            return devices;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to list simulators: {ex.Message}", ex);
            return Array.Empty<SimulatorDevice>();
        }
    }

    public async Task<IReadOnlyList<SimulatorDeviceType>> GetDeviceTypesAsync()
    {
        try
        {
            var json = await RunSimctlAsync("list devicetypes -j");
            if (json == null) return Array.Empty<SimulatorDeviceType>();

            using var doc = JsonDocument.Parse(json);
            var types = new List<SimulatorDeviceType>();

            if (doc.RootElement.TryGetProperty("devicetypes", out var deviceTypes))
            {
                foreach (var dt in deviceTypes.EnumerateArray())
                {
                    types.Add(new SimulatorDeviceType(
                        Identifier: dt.GetProperty("identifier").GetString()!,
                        Name: dt.GetProperty("name").GetString()!,
                        ProductFamily: dt.GetProperty("productFamily").GetString()!
                    ));
                }
            }

            return types;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to list device types: {ex.Message}", ex);
            return Array.Empty<SimulatorDeviceType>();
        }
    }

    public async Task<IReadOnlyList<SimulatorRuntime>> GetRuntimesAsync()
    {
        try
        {
            var json = await RunSimctlAsync("list runtimes -j");
            if (json == null) return Array.Empty<SimulatorRuntime>();

            using var doc = JsonDocument.Parse(json);
            var runtimes = new List<SimulatorRuntime>();

            if (doc.RootElement.TryGetProperty("runtimes", out var runtimesElement))
            {
                foreach (var rt in runtimesElement.EnumerateArray())
                {
                    List<SimulatorDeviceType>? supportedTypes = null;
                    if (rt.TryGetProperty("supportedDeviceTypes", out var sdt))
                    {
                        supportedTypes = new List<SimulatorDeviceType>();
                        foreach (var dt in sdt.EnumerateArray())
                        {
                            supportedTypes.Add(new SimulatorDeviceType(
                                Identifier: dt.GetProperty("identifier").GetString()!,
                                Name: dt.GetProperty("name").GetString()!,
                                ProductFamily: dt.GetProperty("productFamily").GetString()!
                            ));
                        }
                    }

                    runtimes.Add(new SimulatorRuntime(
                        Identifier: rt.GetProperty("identifier").GetString()!,
                        Name: rt.GetProperty("name").GetString()!,
                        Version: rt.GetProperty("version").GetString()!,
                        Platform: rt.TryGetProperty("platform", out var plat) ? plat.GetString() : null,
                        IsAvailable: rt.TryGetProperty("isAvailable", out var avail) && avail.GetBoolean(),
                        SupportedDeviceTypes: supportedTypes
                    ));
                }
            }

            return runtimes;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to list runtimes: {ex.Message}", ex);
            return Array.Empty<SimulatorRuntime>();
        }
    }

    public async Task<bool> CreateSimulatorAsync(string name, string deviceTypeId, string runtimeId, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Creating simulator '{name}'...");
            var result = await RunSimctlAsync($"create \"{name}\" {deviceTypeId} {runtimeId}");
            if (result != null)
            {
                var udid = result.Trim();
                progress?.Report($"Created simulator '{name}' ({udid})");
                _logger.LogInformation($"Created simulator: {name} ({udid})");
                return true;
            }

            progress?.Report($"Failed to create simulator '{name}'");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to create simulator '{name}': {ex.Message}", ex);
            progress?.Report($"Failed to create simulator: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DeleteSimulatorAsync(string udid, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Deleting simulator {udid}...");
            var result = await RunSimctlWithExitCodeAsync($"delete {udid}");
            if (result)
            {
                progress?.Report("Simulator deleted");
                _logger.LogInformation($"Deleted simulator: {udid}");
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to delete simulator {udid}: {ex.Message}", ex);
            progress?.Report($"Failed to delete simulator: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> BootSimulatorAsync(string udid, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Booting simulator {udid}...");
            var result = await RunSimctlWithExitCodeAsync($"boot {udid}");
            if (result)
            {
                progress?.Report("Simulator booted");
                _logger.LogInformation($"Booted simulator: {udid}");
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to boot simulator {udid}: {ex.Message}", ex);
            progress?.Report($"Failed to boot simulator: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ShutdownSimulatorAsync(string udid, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Shutting down simulator {udid}...");
            var result = await RunSimctlWithExitCodeAsync($"shutdown {udid}");
            if (result)
            {
                progress?.Report("Simulator shut down");
                _logger.LogInformation($"Shut down simulator: {udid}");
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to shut down simulator {udid}: {ex.Message}", ex);
            progress?.Report($"Failed to shut down simulator: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> EraseSimulatorAsync(string udid, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Erasing simulator {udid}...");
            var result = await RunSimctlWithExitCodeAsync($"erase {udid}");
            if (result)
            {
                progress?.Report("Simulator erased");
                _logger.LogInformation($"Erased simulator: {udid}");
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to erase simulator {udid}: {ex.Message}", ex);
            progress?.Report($"Failed to erase simulator: {ex.Message}");
            return false;
        }
    }

    public async Task<IReadOnlyList<SimulatorApp>> GetInstalledAppsAsync(string udid)
    {
        try
        {
            // listapps returns plist format; convert to JSON via plutil
            var plistOutput = await RunSimctlAsync($"listapps {udid}");
            if (plistOutput == null) return Array.Empty<SimulatorApp>();

            var json = await ConvertPlistToJsonAsync(plistOutput);
            if (json == null) return Array.Empty<SimulatorApp>();

            using var doc = JsonDocument.Parse(json);
            var apps = new List<SimulatorApp>();

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var bundleId = prop.Name;
                var app = prop.Value;

                var name = GetOptionalString(app, "CFBundleDisplayName")
                    ?? GetOptionalString(app, "CFBundleName")
                    ?? bundleId;
                var version = GetOptionalString(app, "CFBundleVersion");
                var appType = GetOptionalString(app, "ApplicationType") ?? "Unknown";
                var dataContainer = GetOptionalString(app, "DataContainer");
                var bundlePath = GetOptionalString(app, "Path");

                // Clean up file:// prefix from DataContainer
                if (dataContainer?.StartsWith("file://") == true)
                    dataContainer = Uri.UnescapeDataString(new Uri(dataContainer).LocalPath);

                apps.Add(new SimulatorApp(bundleId, name, version, appType, dataContainer, bundlePath));
            }

            return apps.OrderBy(a => a.ApplicationType == "System" ? 1 : 0).ThenBy(a => a.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to list apps for simulator {udid}: {ex.Message}", ex);
            return Array.Empty<SimulatorApp>();
        }
    }

    public async Task<bool> InstallAppAsync(string udid, string appPath, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Installing app from {Path.GetFileName(appPath)}...");
            var result = await RunSimctlWithExitCodeAsync($"install {udid} \"{appPath}\"");
            if (result)
            {
                progress?.Report("App installed successfully");
                _logger.LogInformation($"Installed app on simulator {udid}: {appPath}");
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to install app on simulator {udid}: {ex.Message}", ex);
            progress?.Report($"Failed to install app: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UninstallAppAsync(string udid, string bundleId, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Uninstalling {bundleId}...");
            var result = await RunSimctlWithExitCodeAsync($"uninstall {udid} {bundleId}");
            if (result)
            {
                progress?.Report($"Uninstalled {bundleId}");
                _logger.LogInformation($"Uninstalled {bundleId} from simulator {udid}");
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to uninstall {bundleId} from simulator {udid}: {ex.Message}", ex);
            progress?.Report($"Failed to uninstall: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> LaunchAppAsync(string udid, string bundleId, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Launching {bundleId}...");
            var result = await RunSimctlWithExitCodeAsync($"launch {udid} {bundleId}");
            if (result)
            {
                progress?.Report($"Launched {bundleId}");
                _logger.LogInformation($"Launched {bundleId} on simulator {udid}");
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to launch {bundleId} on simulator {udid}: {ex.Message}", ex);
            progress?.Report($"Failed to launch: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TerminateAppAsync(string udid, string bundleId, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Terminating {bundleId}...");
            var result = await RunSimctlWithExitCodeAsync($"terminate {udid} {bundleId}");
            if (result)
            {
                progress?.Report($"Terminated {bundleId}");
                _logger.LogInformation($"Terminated {bundleId} on simulator {udid}");
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to terminate {bundleId} on simulator {udid}: {ex.Message}", ex);
            progress?.Report($"Failed to terminate: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TakeScreenshotAsync(string udid, string outputPath, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report("Capturing screenshot...");
            var result = await RunSimctlWithExitCodeAsync($"io {udid} screenshot \"{outputPath}\"");
            if (result)
            {
                progress?.Report($"Screenshot saved to {outputPath}");
                _logger.LogInformation($"Screenshot taken for simulator {udid}: {outputPath}");
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to take screenshot for simulator {udid}: {ex.Message}", ex);
            progress?.Report($"Failed to take screenshot: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> GetAppContainerPathAsync(string udid, string bundleId, string containerType = "data")
    {
        try
        {
            var result = await RunSimctlAsync($"get_app_container {udid} {bundleId} {containerType}");
            return result?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to get app container for {bundleId}: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<bool> OpenUrlAsync(string udid, string url, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Opening {url}...");
            var result = await RunSimctlWithExitCodeAsync($"openurl {udid} \"{url}\"");
            if (result)
            {
                progress?.Report("URL opened");
                _logger.LogInformation($"Opened URL on simulator {udid}: {url}");
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to open URL on simulator {udid}: {ex.Message}", ex);
            progress?.Report($"Failed to open URL: {ex.Message}");
            return false;
        }
    }

    public string GetSimulatorDataPath(string udid)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "Developer", "CoreSimulator", "Devices", udid, "data");
    }

    public async Task<bool> CloneSimulatorAsync(string udid, string newName, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Cloning simulator as '{newName}'...");
            var result = await RunSimctlAsync($"clone {udid} \"{newName}\"");
            if (result != null)
            {
                var newUdid = result.Trim();
                progress?.Report($"Cloned simulator '{newName}' ({newUdid})");
                _logger.LogInformation($"Cloned simulator {udid} as '{newName}' ({newUdid})");
                return true;
            }
            progress?.Report("Failed to clone simulator");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to clone simulator {udid}: {ex.Message}", ex);
            progress?.Report($"Failed to clone: {ex.Message}");
            return false;
        }
    }

    // Push notifications
    public async Task<bool> SendPushNotificationAsync(string udid, string bundleId, string payloadJson, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report("Sending push notification...");
            // Write payload to temp file since simctl push reads from file or stdin
            var tempFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempFile, payloadJson);
                var result = await RunSimctlAsync($"push {udid} {bundleId} \"{tempFile}\"");
                progress?.Report("Push notification sent");
                _logger.LogInformation($"Sent push notification to {bundleId} on {udid}");
                return true;
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to send push notification: {ex.Message}", ex);
            progress?.Report($"Failed: {ex.Message}");
            return false;
        }
    }

    // Location simulation
    public async Task<bool> SetLocationAsync(string udid, double latitude, double longitude, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Setting location to {latitude}, {longitude}...");
            await RunSimctlAsync($"location {udid} set {latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            progress?.Report("Location set");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to set location: {ex.Message}", ex);
            progress?.Report($"Failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ClearLocationAsync(string udid, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report("Clearing simulated location...");
            await RunSimctlAsync($"location {udid} clear");
            progress?.Report("Location cleared");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to clear location: {ex.Message}", ex);
            progress?.Report($"Failed: {ex.Message}");
            return false;
        }
    }

    // Status bar overrides
    public async Task<bool> OverrideStatusBarAsync(string udid, StatusBarOverride overrides, IProgress<string>? progress = null)
    {
        try
        {
            var args = new List<string>();
            if (overrides.Time != null) args.AddRange(["--time", $"\"{overrides.Time}\""]);
            if (overrides.DataNetwork != null) args.AddRange(["--dataNetwork", overrides.DataNetwork]);
            if (overrides.WifiMode != null) args.AddRange(["--wifiMode", overrides.WifiMode]);
            if (overrides.WifiBars != null) args.AddRange(["--wifiBars", overrides.WifiBars.Value.ToString()]);
            if (overrides.CellularBars != null) args.AddRange(["--cellularBars", overrides.CellularBars.Value.ToString()]);
            if (overrides.BatteryLevel != null) args.AddRange(["--batteryLevel", overrides.BatteryLevel.Value.ToString()]);
            if (overrides.BatteryState != null) args.AddRange(["--batteryState", overrides.BatteryState]);

            if (args.Count == 0)
            {
                progress?.Report("No overrides specified");
                return false;
            }

            progress?.Report("Setting status bar overrides...");
            await RunSimctlAsync($"status_bar {udid} override {string.Join(" ", args)}");
            progress?.Report("Status bar updated");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to override status bar: {ex.Message}", ex);
            progress?.Report($"Failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ClearStatusBarAsync(string udid, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report("Clearing status bar overrides...");
            await RunSimctlAsync($"status_bar {udid} clear");
            progress?.Report("Status bar cleared");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to clear status bar: {ex.Message}", ex);
            progress?.Report($"Failed: {ex.Message}");
            return false;
        }
    }

    // Route playback
    private CancellationTokenSource? _routeCts;
    public bool IsPlayingRoute { get; private set; }
    public event Action? RoutePlaybackStateChanged;

    public async Task<bool> StartRoutePlaybackAsync(string udid, IReadOnlyList<RouteWaypoint> waypoints,
        double speedMps = 20, CancellationToken ct = default)
    {
        if (waypoints.Count < 2) return false;
        StopRoutePlayback();

        _routeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsPlayingRoute = true;
        RoutePlaybackStateChanged?.Invoke();

        try
        {
            // xcrun simctl location start supports native waypoint sequences
            var args = $"location {udid} start --speed={speedMps.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            foreach (var wp in waypoints)
            {
                args += $" {wp.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{wp.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            }
            await RunSimctlAsync(args);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to start route playback: {ex.Message}", ex);
            return false;
        }
        finally
        {
            IsPlayingRoute = false;
            RoutePlaybackStateChanged?.Invoke();
        }
    }

    public void StopRoutePlayback()
    {
        _routeCts?.Cancel();
        _routeCts?.Dispose();
        _routeCts = null;
        if (IsPlayingRoute)
        {
            IsPlayingRoute = false;
            RoutePlaybackStateChanged?.Invoke();
        }
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private async Task<string?> ConvertPlistToJsonAsync(string plistContent)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "plutil",
                Arguments = "-convert json -o - -- -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            await process.StandardInput.WriteAsync(plistContent);
            process.StandardInput.Close();

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to convert plist to JSON: {ex.Message}", ex);
            return null;
        }
    }

    private async Task<string?> RunSimctlAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "xcrun",
            Arguments = $"simctl {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 ? output : null;
    }

    private async Task<bool> RunSimctlWithExitCodeAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "xcrun",
            Arguments = $"simctl {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return false;

        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }
}
