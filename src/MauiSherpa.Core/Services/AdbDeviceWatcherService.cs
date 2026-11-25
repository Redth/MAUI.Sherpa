using AndroidSdk;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Watches for ADB device connect/disconnect events using the ADB daemon protocol
/// (host:track-devices-l). Fires DevicesChanged when the set of connected devices changes.
/// </summary>
public class AdbDeviceWatcherService : IAdbDeviceWatcherService
{
    private readonly IAndroidSdkService _sdkService;
    private readonly ILoggingService _logger;
    private readonly object _lock = new();
    private List<DeviceInfo> _devices = new();
    private CancellationTokenSource? _cts;
    private Task? _watchTask;
    private AdbdClient? _client;
    private bool _started;
    private readonly SemaphoreSlim _restartGate = new(1, 1);

    public IReadOnlyList<DeviceInfo> Devices
    {
        get { lock (_lock) return _devices.ToList().AsReadOnly(); }
    }

    public bool IsWatching => _watchTask is { IsCompleted: false };

    public event Action<IReadOnlyList<DeviceInfo>>? DevicesChanged;

    public AdbDeviceWatcherService(IAndroidSdkService sdkService, ILoggingService logger)
    {
        _sdkService = sdkService;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        if (_started) return;
        _started = true;
        _sdkService.SdkPathChanged += OnSdkPathAvailable;

        _cts = new CancellationTokenSource();

        var sdkPath = _sdkService.SdkPath;
        await Task.CompletedTask;
        if (!string.IsNullOrEmpty(sdkPath))
        {
            _watchTask = WatchLoopAsync(sdkPath, _cts.Token);
            _logger.LogInformation("ADB device watcher started.");
        }
        else
        {
            // SDK still not found — listen for it to become available later
            _logger.LogInformation("ADB device watcher waiting for SDK path...");
        }
    }

    private void OnSdkPathAvailable()
    {
        _ = RestartForSdkAsync();
    }

    private async Task RestartForSdkAsync()
    {
        await _restartGate.WaitAsync();
        try
        {
            if (!_started) return;
            _cts?.Cancel();
            _client?.Disconnect();
            if (_watchTask != null) await _watchTask;
            _cts?.Dispose();
            _client = null;
            _cts = new CancellationTokenSource();
            lock (_lock) _devices = [];
            DevicesChanged?.Invoke([]);
            if (_started && _sdkService.SdkPath is { } path)
                _watchTask = WatchLoopAsync(path, _cts.Token);
        }
        catch (Exception ex) { _logger.LogError("Could not reconnect ADB watcher after SDK change.", ex); }
        finally { _restartGate.Release(); }
    }

    public void Stop()
    {
        _started = false;
        _sdkService.SdkPathChanged -= OnSdkPathAvailable;
        _cts?.Cancel();
        _client?.Disconnect();
        _cts?.Dispose();
        _cts = null;
        _client = null;
        _logger.LogInformation("ADB device watcher stopped.");
    }

    private async Task WatchLoopAsync(string sdkPath, CancellationToken ct)
    {
        // Retry loop in case the ADB connection drops
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Fetch the full list on every (re)connection; a restarted ADB server doesn't replay old changes.
                await RefreshDeviceListAsync(sdkPath, ct);
                _client = new AdbdClient(sdkPath);

                // WatchDevicesAsync fires per-device per-update, but the upstream
                // parsing of multi-device messages is unreliable (splits all whitespace).
                // We use it only as a change notification trigger, then fetch the full
                // list via a separate ListDevicesAsync call.
                await _client.WatchDevicesAsync(ct, async _ =>
                {
                    await RefreshDeviceListAsync(sdkPath, ct);
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"ADB device watcher error: {ex.Message}. Reconnecting in 5s...");
                _client?.Disconnect();
                _client = null;

                try { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    // Debounce: the watcher fires multiple callbacks per update (one per device line).
    // We only want to fetch the full list once per batch.
    private int _refreshPending = 0;

    private async Task RefreshDeviceListAsync(string sdkPath, CancellationToken ct)
    {
        // If a refresh is already pending, skip
        if (Interlocked.Exchange(ref _refreshPending, 1) == 1)
            return;

        // Small delay to let all per-device callbacks arrive before we query
        try { await Task.Delay(250, ct); }
        catch (OperationCanceledException) { Interlocked.Exchange(ref _refreshPending, 0); return; }

        try
        {
            var listClient = new AdbdClient(sdkPath);
            var adbDevices = await listClient.ListDevicesAsync(ct);
            listClient.Disconnect();

            var newList = adbDevices
                .Select(d => new DeviceInfo(
                    d.Serial,
                    "device",
                    d.Model?.Replace('_', ' '),
                    d.Serial.StartsWith("emulator-")))
                .ToList();
            ct.ThrowIfCancellationRequested();

            lock (_lock)
            {
                var changed = HasChanged(_devices, newList);
                _devices = newList;

                if (changed)
                {
                    _logger.LogInformation($"ADB devices changed: {newList.Count} device(s) connected.");
                    DevicesChanged?.Invoke(newList.AsReadOnly());
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning($"Failed to refresh device list: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshPending, 0);
        }
    }

    private static bool HasChanged(List<DeviceInfo> old, List<DeviceInfo> current)
    {
        if (old.Count != current.Count) return true;
        var oldSerials = old.Select(d => d.Serial).OrderBy(s => s).ToList();
        var newSerials = current.Select(d => d.Serial).OrderBy(s => s).ToList();
        return !oldSerials.SequenceEqual(newSerials);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
