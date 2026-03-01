using System.Diagnostics;
using System.Threading.Channels;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Streams syslog from a physical iOS device.
/// Uses pymobiledevice3 (supports iOS 17+ via bonjour/mobdev2 discovery),
/// falls back to idevicesyslog (libimobiledevice, works for iOS 16 and older).
/// </summary>
public class PhysicalDeviceLogService : IPhysicalDeviceLogService
{
    private const int MaxEntries = 50_000;

    private readonly ILoggingService _logger;
    private readonly IPlatformService _platform;
    private readonly IPhysicalDeviceService _deviceService;
    private readonly List<SimulatorLogEntry> _entries = new();
    private readonly object _lock = new();
    private Process? _process;
    private CancellationTokenSource? _cts;
    private Channel<SimulatorLogEntry>? _channel;

    // Common venv install locations for pymobiledevice3
    private static readonly string[] PymobiledevicePaths = new[]
    {
        "pymobiledevice3", // on PATH
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/pymobiledevice3-venv/bin/pymobiledevice3"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/bin/pymobiledevice3"),
    };

    public bool IsSupported => _platform.IsMacCatalyst || _platform.IsMacOS;
    public bool IsRunning => _process is { HasExited: false };
    public IReadOnlyList<SimulatorLogEntry> Entries
    {
        get { lock (_lock) return _entries.ToList().AsReadOnly(); }
    }

    public event Action? OnCleared;

    public PhysicalDeviceLogService(ILoggingService logger, IPlatformService platform, IPhysicalDeviceService deviceService)
    {
        _logger = logger;
        _platform = platform;
        _deviceService = deviceService;
    }

    public async Task StartAsync(string udid, CancellationToken ct = default)
    {
        if (!IsSupported) return;

        if (IsRunning)
            Stop();

        // The udid parameter may be a CoreDevice identifier (UUID format).
        // idevicesyslog needs the hardware UDID, so look it up.
        var hardwareUdid = await ResolveHardwareUdidAsync(udid);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _channel = Channel.CreateUnbounded<SimulatorLogEntry>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = false,
        });

        var (fileName, arguments) = FindLogCommand(hardwareUdid, udid);

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        _process.Start();
        _logger.LogInformation($"Physical device log stream started for {udid} (PID: {_process.Id}, cmd: {fileName} {arguments})");

        _ = Task.Run(() => ReadOutputAsync(_process, _cts.Token), _cts.Token);
        _ = Task.Run(() => ReadStderrAsync(_process, _cts.Token), _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _channel?.Writer.TryComplete();

        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                _logger.LogInformation("Physical device log stream stopped.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to kill log stream process: {ex.Message}");
            }
        }

        _process?.Dispose();
        _process = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
        OnCleared?.Invoke();
    }

    public async IAsyncEnumerable<SimulatorLogEntry> StreamAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_channel == null)
            yield break;

        await foreach (var entry in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return entry;
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private async Task<string> ResolveHardwareUdidAsync(string identifierOrUdid)
    {
        try
        {
            var devices = await _deviceService.GetDevicesAsync();
            // Match by CoreDevice identifier
            var device = devices.FirstOrDefault(d =>
                d.Identifier.Equals(identifierOrUdid, StringComparison.OrdinalIgnoreCase));
            if (device != null)
                return device.Udid;

            // Maybe it's already a hardware UDID
            device = devices.FirstOrDefault(d =>
                d.Udid.Equals(identifierOrUdid, StringComparison.OrdinalIgnoreCase));
            if (device != null)
                return device.Udid;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to resolve hardware UDID for {identifierOrUdid}: {ex.Message}");
        }

        // Fall back to using as-is
        return identifierOrUdid;
    }

    private (string fileName, string arguments) FindLogCommand(string hardwareUdid, string coreDeviceId)
    {
        // pymobiledevice3 supports iOS 17+ via bonjour/mobdev2 discovery
        var pymobile = ResolvePymobiledevice3();
        if (pymobile != null)
        {
            _logger.LogInformation($"Using pymobiledevice3 for device log streaming: {pymobile}");
            return (pymobile, $"syslog live --mobdev2 --udid {hardwareUdid}");
        }

        // idevicesyslog works with devices visible to usbmuxd (iOS 16 and older,
        // or iOS 17+ if a tunnel is set up)
        if (TryWhich("idevicesyslog"))
        {
            _logger.LogInformation("Using idevicesyslog for device log streaming");
            return ("idevicesyslog", $"-u {hardwareUdid}");
        }

        // No tool available — output an error message
        _logger.LogWarning("No device log streaming tool found (pymobiledevice3 or idevicesyslog)");
        return ("echo", "Device log streaming requires pymobiledevice3 or idevicesyslog (libimobiledevice). Install with: python3 -m venv ~/.local/pymobiledevice3-venv && ~/.local/pymobiledevice3-venv/bin/pip install pymobiledevice3");
    }

    /// <summary>
    /// Finds pymobiledevice3 on PATH or in common venv locations.
    /// </summary>
    private static string? ResolvePymobiledevice3()
    {
        foreach (var path in PymobiledevicePaths)
        {
            if (path == "pymobiledevice3")
            {
                if (TryWhich(path))
                    return path;
            }
            else if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    private static bool TryWhich(string tool)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = tool,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(2000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private async Task ReadOutputAsync(Process process, CancellationToken ct)
    {
        try
        {
            var reader = process.StandardOutput;

            while (!ct.IsCancellationRequested && !reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var entry = ParseSyslogLine(line);
                if (entry == null)
                    continue;

                lock (_lock)
                {
                    if (_entries.Count >= MaxEntries)
                        _entries.RemoveAt(0);

                    _entries.Add(entry);
                }

                _channel?.Writer.TryWrite(entry);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error reading physical device log output: {ex.Message}", ex);
        }
        finally
        {
            _channel?.Writer.TryComplete();
        }
    }

    private async Task ReadStderrAsync(Process process, CancellationToken ct)
    {
        try
        {
            var reader = process.StandardError;
            while (!ct.IsCancellationRequested && !reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(line))
                    _logger.LogWarning($"Device log stderr: {line}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError($"Error reading device log stderr: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parse syslog lines from idevicesyslog or pymobiledevice3 syslog.
    /// Typical format: "Mar  1 12:34:56 DeviceName processName(pid)[category] &lt;Level&gt;: message"
    /// </summary>
    private static SimulatorLogEntry? ParseSyslogLine(string line)
    {
        try
        {
            // Try to parse the timestamp and level from typical syslog format
            var level = SimulatorLogLevel.Default;
            var process = "";
            var message = line;

            // idevicesyslog format: "Mon DD HH:MM:SS DeviceName process[pid] <Level>: message"
            // Try to extract level marker like <Notice>, <Error>, <Warning>
            var levelStart = line.IndexOf('<');
            var levelEnd = levelStart >= 0 ? line.IndexOf('>', levelStart) : -1;
            if (levelStart >= 0 && levelEnd > levelStart)
            {
                var levelStr = line.Substring(levelStart + 1, levelEnd - levelStart - 1);
                level = levelStr.ToLowerInvariant() switch
                {
                    "error" or "fault" => SimulatorLogLevel.Error,
                    "warning" or "warn" => SimulatorLogLevel.Fault,
                    "info" => SimulatorLogLevel.Info,
                    "debug" => SimulatorLogLevel.Debug,
                    _ => SimulatorLogLevel.Default,
                };

                // Extract process name from before the level marker
                var beforeLevel = line[..levelStart].TrimEnd();
                var bracketPos = beforeLevel.LastIndexOf('[');
                var spaceBeforeProc = bracketPos >= 0
                    ? beforeLevel.LastIndexOf(' ', bracketPos)
                    : beforeLevel.LastIndexOf(' ');
                if (spaceBeforeProc >= 0)
                    process = beforeLevel[(spaceBeforeProc + 1)..].Trim();

                // Message is everything after ">: "
                var msgStart = levelEnd + 1;
                if (msgStart < line.Length && line[msgStart] == ':')
                    msgStart++;
                if (msgStart < line.Length && line[msgStart] == ' ')
                    msgStart++;
                message = msgStart < line.Length ? line[msgStart..] : "";
            }

            return new SimulatorLogEntry(
                DateTimeOffset.Now.ToString("o"),
                0,  // ProcessId
                0,  // ThreadId
                level,
                process,
                null, // subsystem
                null, // category
                message,
                line
            );
        }
        catch
        {
            return new SimulatorLogEntry(
                DateTimeOffset.Now.ToString("o"),
                0,
                0,
                SimulatorLogLevel.Default,
                "",
                null,
                null,
                line,
                line
            );
        }
    }
}
