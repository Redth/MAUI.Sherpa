using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class LogcatService : ILogcatService
{
    private const int MaxEntries = 50_000;

    private readonly IAndroidSdkService _sdkService;
    private readonly ILoggingService _logger;
    private readonly List<LogcatEntry> _entries = new();
    private readonly object _lock = new();
    private Process? _process;
    private CancellationTokenSource? _cts;
    private LogcatEntry? _lastEntry;
    private Channel<LogcatEntry>? _channel;

    public bool IsRunning => _process is { HasExited: false };
    public IReadOnlyList<LogcatEntry> Entries
    {
        get { lock (_lock) return _entries.ToList().AsReadOnly(); }
    }

    public event Action? OnCleared;

    public LogcatService(IAndroidSdkService sdkService, ILoggingService logger)
    {
        _sdkService = sdkService;
        _logger = logger;
    }

    public Task StartAsync(string serial, CancellationToken ct = default)
    {
        if (IsRunning)
            Stop();

        var sdkPath = _sdkService.SdkPath;
        if (string.IsNullOrEmpty(sdkPath))
            throw new InvalidOperationException("Android SDK path is not configured.");

        var adbPath = AppDataPath.GetAdbPath(sdkPath);
        if (!File.Exists(adbPath))
            throw new FileNotFoundException("adb not found.", adbPath);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _channel = Channel.CreateUnbounded<LogcatEntry>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = false,
        });

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = $"-s {serial} logcat -v threadtime",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        _process.Start();
        _logger.LogInformation($"Logcat started for device {serial} (PID: {_process.Id})");

        _ = Task.Run(() => ReadOutputAsync(_process, _cts.Token), _cts.Token);

        return Task.CompletedTask;
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
                _logger.LogInformation("Logcat process stopped.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to kill logcat process: {ex.Message}");
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
            _lastEntry = null;
        }
        OnCleared?.Invoke();
    }

    public async IAsyncEnumerable<LogcatEntry> StreamAsync(
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

    private async Task ReadOutputAsync(Process process, CancellationToken ct)
    {
        try
        {
            var reader = process.StandardOutput;

            while (!ct.IsCancellationRequested && !reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null)
                    break;

                var entry = LogcatParser.Parse(line);

                if (entry == null && !string.IsNullOrWhiteSpace(line))
                {
                    entry = LogcatParser.CreateContinuation(line, _lastEntry);
                }

                if (entry == null)
                    continue;

                lock (_lock)
                {
                    if (_entries.Count >= MaxEntries)
                        _entries.RemoveAt(0);

                    _entries.Add(entry);
                    _lastEntry = entry;
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
            _logger.LogError($"Error reading logcat output: {ex.Message}", ex);
        }
        finally
        {
            _channel?.Writer.TryComplete();
        }
    }
}
