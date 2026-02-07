using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class DeviceShellService : IDeviceShellService
{
    private readonly IAndroidSdkService _sdkService;
    private readonly ILoggingService _logger;
    private Process? _process;
    private Channel<string>? _channel;
    private CancellationTokenSource? _readCts;

    public bool IsRunning => _process is { HasExited: false };
    public string? ActiveSerial { get; private set; }

    public DeviceShellService(IAndroidSdkService sdkService, ILoggingService logger)
    {
        _sdkService = sdkService;
        _logger = logger;
    }

    public Task StartAsync(string serial, CancellationToken ct = default)
    {
        Stop();

        var adbPath = GetAdbPath();
        if (adbPath == null)
            throw new InvalidOperationException("Android SDK not found");

        ActiveSerial = serial;
        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        _readCts = new CancellationTokenSource();

        var psi = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = $"-s {serial} shell",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Prevent encoding issues with binary/control chars
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        // Set TERM so adb shell allocates a proper PTY
        psi.Environment["TERM"] = "xterm-256color";

        _process = Process.Start(psi);
        if (_process == null)
            throw new InvalidOperationException("Failed to start adb shell process");

        _logger.LogInformation($"Shell started for device {serial} (PID: {_process.Id})");

        // Read stdout char-by-char for real-time output (prompts, partial lines)
        _ = ReadStreamCharsAsync(_process.StandardOutput, _readCts.Token);
        _ = ReadStreamCharsAsync(_process.StandardError, _readCts.Token);

        return Task.CompletedTask;
    }

    public async Task SendCommandAsync(string command, CancellationToken ct = default)
    {
        if (_process is not { HasExited: false })
            throw new InvalidOperationException("Shell not running");

        await _process.StandardInput.WriteLineAsync(command.AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
    }

    public async IAsyncEnumerable<string> OutputStreamAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_channel == null) yield break;

        await foreach (var chunk in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    public void Stop()
    {
        _readCts?.Cancel();
        _channel?.Writer.TryComplete();

        if (_process is { HasExited: false })
        {
            try
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(2000))
                    _process.Kill();
            }
            catch { }
        }

        if (ActiveSerial != null)
            _logger.LogInformation($"Shell stopped for device {ActiveSerial}");

        _process?.Dispose();
        _process = null;
        _readCts?.Dispose();
        _readCts = null;
        _channel = null;
        ActiveSerial = null;
    }

    private async Task ReadStreamCharsAsync(System.IO.StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[1024];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(), ct);
                if (count == 0) break; // stream closed
                var chunk = new string(buffer, 0, count);
                _channel?.Writer.TryWrite(chunk);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning($"Shell read error: {ex.Message}");
        }
        finally
        {
            _channel?.Writer.TryComplete();
        }
    }

    private string? GetAdbPath()
    {
        var sdkPath = _sdkService.SdkPath;
        if (string.IsNullOrEmpty(sdkPath)) return null;
        return System.IO.Path.Combine(sdkPath, "platform-tools", "adb");
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
