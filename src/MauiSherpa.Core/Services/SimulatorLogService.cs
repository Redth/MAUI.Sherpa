using System.Diagnostics;
using System.Threading.Channels;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class SimulatorLogService : ISimulatorLogService
{
    private const int MaxEntries = 50_000;

    private readonly ILoggingService _logger;
    private readonly IPlatformService _platform;
    private readonly List<SimulatorLogEntry> _entries = new();
    private readonly object _lock = new();
    private Process? _process;
    private CancellationTokenSource? _cts;
    private Channel<SimulatorLogEntry>? _channel;

    public bool IsSupported => _platform.IsMacCatalyst;
    public bool IsRunning => _process is { HasExited: false };
    public IReadOnlyList<SimulatorLogEntry> Entries
    {
        get { lock (_lock) return _entries.ToList().AsReadOnly(); }
    }

    public event Action? OnCleared;

    public SimulatorLogService(ILoggingService logger, IPlatformService platform)
    {
        _logger = logger;
        _platform = platform;
    }

    public Task StartAsync(string udid, CancellationToken ct = default)
    {
        if (!IsSupported) return Task.CompletedTask;

        if (IsRunning)
            Stop();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _channel = Channel.CreateUnbounded<SimulatorLogEntry>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = false,
        });

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "xcrun",
                Arguments = $"simctl spawn {udid} log stream --style ndjson --level debug",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        _process.Start();
        _logger.LogInformation($"Simulator log stream started for {udid} (PID: {_process.Id})");

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
                _logger.LogInformation("Simulator log stream stopped.");
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

                var entry = SimulatorLogParser.Parse(line);
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
            _logger.LogError($"Error reading simulator log output: {ex.Message}", ex);
        }
        finally
        {
            _channel?.Writer.TryComplete();
        }
    }
}
