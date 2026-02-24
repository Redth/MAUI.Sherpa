using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Centralized device monitor: uses ADB daemon events for Android and
/// xcdevice observe for Apple devices/simulators. Publishes mediator events on changes.
/// </summary>
public class DeviceMonitorService : IDeviceMonitorService, IDisposable
{
    private readonly IAdbDeviceWatcherService _adbWatcher;
    private readonly ILoggingService _logger;
    private readonly IMediator _mediator;
    private readonly object _lock = new();

    private ConnectedDevicesSnapshot _current = ConnectedDevicesSnapshot.Empty;
    private CancellationTokenSource? _cts;
    private Process? _observeProcess;
    private Task? _appleWatchTask;

    public ConnectedDevicesSnapshot Current
    {
        get { lock (_lock) return _current; }
    }

    public event Action<ConnectedDevicesSnapshot>? Changed;

    public DeviceMonitorService(
        IAdbDeviceWatcherService adbWatcher,
        ILoggingService logger,
        IMediator mediator)
    {
        _adbWatcher = adbWatcher;
        _logger = logger;
        _mediator = mediator;
    }

    public async Task StartAsync()
    {
        _cts = new CancellationTokenSource();

        // Android: delegate to existing ADB watcher
        _adbWatcher.DevicesChanged += OnAndroidDevicesChanged;
        if (!_adbWatcher.IsWatching)
        {
            try { await _adbWatcher.StartAsync(); }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to start ADB watcher: {ex.Message}");
            }
        }
        // Seed initial Android state
        UpdateAndroidDevices(_adbWatcher.Devices);

        // Apple: start xcdevice observe
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            _appleWatchTask = AppleWatchLoopAsync(_cts.Token);
        }

        _logger.LogInformation("Device monitor started.");
    }

    public void Stop()
    {
        _adbWatcher.DevicesChanged -= OnAndroidDevicesChanged;
        _cts?.Cancel();
        KillObserveProcess();
        _cts?.Dispose();
        _cts = null;
        _logger.LogInformation("Device monitor stopped.");
    }

    // ---- Android ----

    private void OnAndroidDevicesChanged(IReadOnlyList<DeviceInfo> devices)
    {
        UpdateAndroidDevices(devices);
        PublishChange();
    }

    private void UpdateAndroidDevices(IReadOnlyList<DeviceInfo> devices)
    {
        lock (_lock)
        {
            var physical = devices.Where(d => !d.IsEmulator && d.State == "device").ToList();
            var emulators = devices.Where(d => d.IsEmulator && d.State == "device").ToList();
            _current = _current with
            {
                AndroidDevices = physical.AsReadOnly(),
                AndroidEmulators = emulators.AsReadOnly()
            };
        }
    }

    // ---- Apple (xcdevice observe) ----

    private async Task AppleWatchLoopAsync(CancellationToken ct)
    {
        // Initial fetch
        await RefreshAppleDevicesAsync(ct);
        PublishChange();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunXcdeviceObserveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"xcdevice observe error: {ex.Message}. Reconnecting in 5s...");
                KillObserveProcess();
                try { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task RunXcdeviceObserveAsync(CancellationToken ct)
    {
        var xcdevicePath = await FindXcdevicePathAsync(ct);
        if (xcdevicePath == null)
        {
            _logger.LogWarning("xcdevice not found. Apple device monitoring disabled.");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = xcdevicePath,
            Arguments = "observe --both",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _observeProcess = Process.Start(psi);
        if (_observeProcess == null)
        {
            _logger.LogWarning("Failed to start xcdevice observe.");
            return;
        }

        _logger.LogInformation("xcdevice observe started.");

        // Register for cancellation
        var reg = ct.Register(() => KillObserveProcess());

        try
        {
            var reader = _observeProcess.StandardOutput;
            int debounce = 0;

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break; // Process exited

                if (line.StartsWith("Attach:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Detach:", StringComparison.OrdinalIgnoreCase))
                {
                    // Debounce: multiple events can fire in quick succession
                    if (Interlocked.Exchange(ref debounce, 1) == 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(500, ct);
                                await RefreshAppleDevicesAsync(ct);
                                PublishChange();
                            }
                            catch (OperationCanceledException) { }
                            finally
                            {
                                Interlocked.Exchange(ref debounce, 0);
                            }
                        }, ct);
                    }
                }
            }
        }
        finally
        {
            reg.Dispose();
            KillObserveProcess();
        }

        if (!ct.IsCancellationRequested)
            throw new InvalidOperationException("xcdevice observe exited unexpectedly.");
    }

    private async Task RefreshAppleDevicesAsync(CancellationToken ct)
    {
        try
        {
            // Get all devices from xcdevice list (physical + simulators)
            var xcdevicePath = await FindXcdevicePathAsync(ct);
            if (xcdevicePath == null) return;

            var psi = new ProcessStartInfo
            {
                FileName = xcdevicePath,
                Arguments = "list --timeout=1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return;

            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            var xcDevices = JsonSerializer.Deserialize<List<XcdeviceEntry>>(output) ?? new();

            // Get simulator states from simctl
            var simStates = await GetSimulatorStatesAsync(ct);

            var physicalDevices = new List<AppleDeviceInfo>();
            var bootedSims = new List<AppleDeviceInfo>();

            foreach (var d in xcDevices)
            {
                if (d.Ignored || !d.Available) continue;

                if (d.Simulator)
                {
                    // Check if this simulator is booted
                    if (simStates.TryGetValue(d.Identifier, out var state) &&
                        state.Equals("Booted", StringComparison.OrdinalIgnoreCase))
                    {
                        bootedSims.Add(new AppleDeviceInfo(
                            d.Identifier, d.Name, d.ModelName, d.Platform,
                            d.Architecture, d.OperatingSystemVersion,
                            IsSimulator: true, IsAvailable: true,
                            Interface: null, SimState: "Booted"));
                    }
                }
                else
                {
                    physicalDevices.Add(new AppleDeviceInfo(
                        d.Identifier, d.Name, d.ModelName, d.Platform,
                        d.Architecture, d.OperatingSystemVersion,
                        IsSimulator: false, IsAvailable: true,
                        Interface: d.InterfaceType, SimState: null));
                }
            }

            lock (_lock)
            {
                _current = _current with
                {
                    ApplePhysicalDevices = physicalDevices.AsReadOnly(),
                    BootedSimulators = bootedSims.AsReadOnly()
                };
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning($"Failed to refresh Apple devices: {ex.Message}");
        }
    }

    private static async Task<Dictionary<string, string>> GetSimulatorStatesAsync(CancellationToken ct)
    {
        var states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xcrun",
                Arguments = "simctl list devices -j",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return states;

            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.TryGetProperty("devices", out var devicesObj))
            {
                foreach (var runtime in devicesObj.EnumerateObject())
                {
                    foreach (var device in runtime.Value.EnumerateArray())
                    {
                        var udid = device.GetProperty("udid").GetString();
                        var state = device.GetProperty("state").GetString();
                        if (udid != null && state != null)
                            states[udid] = state;
                    }
                }
            }
        }
        catch { }
        return states;
    }

    private string? _cachedXcdevicePath;

    private async Task<string?> FindXcdevicePathAsync(CancellationToken ct)
    {
        if (_cachedXcdevicePath != null) return _cachedXcdevicePath;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xcode-select",
                Arguments = "-p",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var xcodePath = (await proc.StandardOutput.ReadToEndAsync(ct)).Trim();
            await proc.WaitForExitAsync(ct);

            var path = Path.Combine(xcodePath, "usr/bin/xcdevice");
            if (File.Exists(path))
            {
                _cachedXcdevicePath = path;
                return path;
            }
        }
        catch { }

        return null;
    }

    private void KillObserveProcess()
    {
        try
        {
            if (_observeProcess is { HasExited: false })
            {
                _observeProcess.Kill();
                _observeProcess.Dispose();
            }
        }
        catch { }
        _observeProcess = null;
    }

    // ---- Publishing ----

    private void PublishChange()
    {
        ConnectedDevicesSnapshot snapshot;
        lock (_lock) { snapshot = _current; }

        Changed?.Invoke(snapshot);

        _ = Task.Run(async () =>
        {
            try
            {
                await _mediator.Publish(new ConnectedDevicesChangedEvent(snapshot));
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to publish device change event: {ex.Message}");
            }
        });
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    // JSON model for xcdevice list output
    private class XcdeviceEntry
    {
        [JsonPropertyName("identifier")]
        public string Identifier { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("modelName")]
        public string ModelName { get; set; } = "";

        [JsonPropertyName("platform")]
        public string Platform { get; set; } = "";

        [JsonPropertyName("architecture")]
        public string Architecture { get; set; } = "";

        [JsonPropertyName("operatingSystemVersion")]
        public string OperatingSystemVersion { get; set; } = "";

        [JsonPropertyName("simulator")]
        public bool Simulator { get; set; }

        [JsonPropertyName("available")]
        public bool Available { get; set; }

        [JsonPropertyName("ignored")]
        public bool Ignored { get; set; }

        [JsonPropertyName("interface")]
        public string? InterfaceType { get; set; }
    }
}
