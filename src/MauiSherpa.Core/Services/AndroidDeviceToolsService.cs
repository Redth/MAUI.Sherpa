using System.Diagnostics;
using System.Globalization;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class AndroidDeviceToolsService : IAndroidDeviceToolsService
{
    private readonly IAndroidSdkService _sdkService;
    private readonly ILoggingService _logger;
    private CancellationTokenSource? _routeCts;

    public bool IsPlayingRoute { get; private set; }
    public event Action? RoutePlaybackStateChanged;

    public AndroidDeviceToolsService(IAndroidSdkService sdkService, ILoggingService logger)
    {
        _sdkService = sdkService;
        _logger = logger;
    }

    // ── Location ──

    public async Task<bool> SetLocationAsync(string serial, double latitude, double longitude)
    {
        try
        {
            // adb emu geo fix uses longitude,latitude order
            var lon = longitude.ToString(CultureInfo.InvariantCulture);
            var lat = latitude.ToString(CultureInfo.InvariantCulture);
            await RunAdbAsync(serial, $"emu geo fix {lon} {lat}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to set location: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> ClearLocationAsync(string serial)
    {
        // No explicit clear — set to 0,0 as a reset
        return await SetLocationAsync(serial, 0, 0);
    }

    public async Task<bool> StartRoutePlaybackAsync(string serial, IReadOnlyList<RouteWaypoint> waypoints,
        double speedMps = 20, CancellationToken ct = default)
    {
        if (waypoints.Count < 2) return false;
        StopRoutePlayback();

        _routeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _routeCts.Token;
        IsPlayingRoute = true;
        RoutePlaybackStateChanged?.Invoke();

        _ = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < waypoints.Count - 1; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var wp = waypoints[i];
                    var next = waypoints[i + 1];

                    await SetLocationAsync(serial, wp.Latitude, wp.Longitude);

                    // Calculate distance and delay
                    var dist = HaversineDistance(wp.Latitude, wp.Longitude, next.Latitude, next.Longitude);
                    var delayMs = (int)(dist / speedMps * 1000);
                    delayMs = Math.Clamp(delayMs, 100, 30000);
                    await Task.Delay(delayMs, token);
                }
                // Set final waypoint
                var last = waypoints[^1];
                await SetLocationAsync(serial, last.Latitude, last.Longitude);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError($"Route playback error: {ex.Message}", ex);
            }
            finally
            {
                IsPlayingRoute = false;
                RoutePlaybackStateChanged?.Invoke();
            }
        }, token);

        return true;
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

    // ── Battery ──

    public async Task<bool> SetBatteryLevelAsync(string serial, int level)
    {
        try
        {
            await RunAdbAsync(serial, $"shell dumpsys battery set level {Math.Clamp(level, 0, 100)}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to set battery level: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> SetBatteryStatusAsync(string serial, string status)
    {
        try
        {
            // Status codes: 1=unknown, 2=charging, 3=discharging, 4=not-charging, 5=full
            var code = status.ToLowerInvariant() switch
            {
                "charging" => 2,
                "discharging" => 3,
                "not-charging" => 4,
                "full" => 5,
                _ => 1
            };
            await RunAdbAsync(serial, $"shell dumpsys battery set status {code}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to set battery status: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> ResetBatteryAsync(string serial)
    {
        try
        {
            await RunAdbAsync(serial, "shell dumpsys battery reset");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to reset battery: {ex.Message}", ex);
            return false;
        }
    }

    // ── Demo Mode ──

    public async Task<bool> EnableDemoModeAsync(string serial)
    {
        try
        {
            await RunAdbAsync(serial, "shell settings put global sysui_demo_allowed 1");
            await RunAdbAsync(serial, "shell am broadcast -a com.android.systemui.demo -e command enter");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to enable demo mode: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> SetDemoStatusAsync(string serial, AndroidDemoStatus status)
    {
        try
        {
            if (status.Time != null)
                await RunAdbAsync(serial, $"shell am broadcast -a com.android.systemui.demo -e command clock -e hhmm {status.Time}");

            if (status.WifiLevel != null)
                await RunAdbAsync(serial, $"shell am broadcast -a com.android.systemui.demo -e command network -e wifi show -e level {status.WifiLevel}");

            if (status.MobileLevel != null)
            {
                var dataType = status.MobileDataType ?? "lte";
                await RunAdbAsync(serial, $"shell am broadcast -a com.android.systemui.demo -e command network -e mobile show -e level {status.MobileLevel} -e datatype {dataType}");
            }

            if (status.BatteryLevel != null)
            {
                var plugged = status.BatteryPlugged == true ? "true" : "false";
                await RunAdbAsync(serial, $"shell am broadcast -a com.android.systemui.demo -e command battery -e level {status.BatteryLevel} -e plugged {plugged}");
            }

            if (status.HideNotifications == true)
                await RunAdbAsync(serial, "shell am broadcast -a com.android.systemui.demo -e command notifications -e visible false");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to set demo status: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> DisableDemoModeAsync(string serial)
    {
        try
        {
            await RunAdbAsync(serial, "shell am broadcast -a com.android.systemui.demo -e command exit");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to disable demo mode: {ex.Message}", ex);
            return false;
        }
    }

    // ── Deep Links ──

    public async Task<bool> OpenDeepLinkAsync(string serial, string url)
    {
        try
        {
            await RunAdbAsync(serial, $"shell am start -a android.intent.action.VIEW -d \"{url}\"");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to open deep link: {ex.Message}", ex);
            return false;
        }
    }

    // ── Helpers ──

    private async Task<string> RunAdbAsync(string serial, string arguments)
    {
        var adbPath = GetAdbPath();
        if (adbPath == null)
            throw new InvalidOperationException("Android SDK not found");

        var psi = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = $"-s {serial} {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Failed to start adb process");

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }

    private string? GetAdbPath()
    {
        var sdkPath = _sdkService.SdkPath;
        if (string.IsNullOrEmpty(sdkPath)) return null;
        return Path.Combine(sdkPath, "platform-tools", "adb");
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180;
}
