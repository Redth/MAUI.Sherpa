using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace MauiSherpa.Cli.Helpers;

/// <summary>
/// Spawns an external process and streams stdout lines as they arrive.
/// Designed for long-running streaming commands like adb logcat and xcrun simctl log stream.
/// </summary>
public sealed class StreamingProcess : IDisposable
{
    private Process? _process;
    private CancellationTokenSource? _cts;

    public bool IsRunning => _process is { HasExited: false };

    public static async IAsyncEnumerable<string> RunAsync(
        string fileName,
        string arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var sp = new StreamingProcess();
        await foreach (var line in sp.StartAsync(fileName, arguments, ct))
        {
            yield return line;
        }
    }

    public async IAsyncEnumerable<string> StartAsync(
        string fileName,
        string arguments,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _process = new Process { StartInfo = psi };
        _process.Start();

        // Drain stderr in background to prevent blocking
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var line = await _process.StandardError.ReadLineAsync(_cts.Token);
                    if (line is null) break;
                }
            }
            catch { /* ignored */ }
        }, _cts.Token);

        var reader = _process.StandardOutput;
        while (!_cts.Token.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null) break;
            yield return line;
        }

        Stop();
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
        }
        catch { /* best effort */ }
    }

    public void Dispose()
    {
        Stop();
        _process?.Dispose();
        _cts?.Dispose();
    }
}
