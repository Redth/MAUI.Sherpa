using System.Diagnostics;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class ScreenCaptureService : IScreenCaptureService
{
    private readonly IAndroidSdkService _sdkService;
    private readonly ILoggingService _logger;
    private Process? _recordProcess;
    private string? _recordingSerial;
    private string? _remoteRecordPath;

    public bool IsRecording => _recordProcess is { HasExited: false };

    public ScreenCaptureService(IAndroidSdkService sdkService, ILoggingService logger)
    {
        _sdkService = sdkService;
        _logger = logger;
    }

    public async Task<byte[]> CaptureScreenshotAsync(string serial, CancellationToken ct = default)
    {
        var adbPath = GetAdbPath() ?? throw new InvalidOperationException("Android SDK not found");

        var psi = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = $"-s {serial} exec-out screencap -p",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start screencap");

        using var ms = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(ms, ct);
        await process.WaitForExitAsync(ct);

        var data = ms.ToArray();
        _logger.LogInformation($"Screenshot captured from {serial}: {data.Length} bytes");
        return data;
    }

    public Task StartRecordingAsync(string serial, int maxSeconds = 180, CancellationToken ct = default)
    {
        if (IsRecording)
            throw new InvalidOperationException("Already recording");

        var adbPath = GetAdbPath() ?? throw new InvalidOperationException("Android SDK not found");

        _recordingSerial = serial;
        _remoteRecordPath = $"/sdcard/screenrecord-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.mp4";

        var psi = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = $"-s {serial} shell screenrecord --time-limit {maxSeconds} {_remoteRecordPath}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _recordProcess = Process.Start(psi);
        if (_recordProcess == null)
            throw new InvalidOperationException("Failed to start screenrecord");

        _logger.LogInformation($"Recording started on {serial} (PID: {_recordProcess.Id}, max {maxSeconds}s)");
        return Task.CompletedTask;
    }

    public async Task<byte[]?> StopRecordingAsync(CancellationToken ct = default)
    {
        if (_recordProcess == null || _recordingSerial == null || _remoteRecordPath == null)
            return null;

        // Send interrupt to stop recording gracefully
        if (!_recordProcess.HasExited)
        {
            try { _recordProcess.Kill(); } catch { }
            try { await _recordProcess.WaitForExitAsync(ct); } catch { }
        }

        _recordProcess.Dispose();
        _recordProcess = null;

        // Wait briefly for file to finalize on device
        await Task.Delay(500, ct);

        // Pull the file
        var adbPath = GetAdbPath();
        if (adbPath == null) return null;

        var localPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(_remoteRecordPath));

        var pullPsi = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = $"-s {_recordingSerial} pull {_remoteRecordPath} \"{localPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var pullProcess = Process.Start(pullPsi))
        {
            if (pullProcess != null)
                await pullProcess.WaitForExitAsync(ct);
        }

        // Clean up remote file
        var rmPsi = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = $"-s {_recordingSerial} shell rm {_remoteRecordPath}",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using (var rmProcess = Process.Start(rmPsi))
        {
            if (rmProcess != null)
                await rmProcess.WaitForExitAsync(ct);
        }

        var serial = _recordingSerial;
        _recordingSerial = null;
        _remoteRecordPath = null;

        if (File.Exists(localPath))
        {
            var data = await File.ReadAllBytesAsync(localPath, ct);
            File.Delete(localPath);
            _logger.LogInformation($"Recording stopped on {serial}: {data.Length} bytes");
            return data;
        }

        _logger.LogWarning($"Recording file not found after pull from {serial}");
        return null;
    }

    private string? GetAdbPath()
    {
        var sdkPath = _sdkService.SdkPath;
        if (string.IsNullOrEmpty(sdkPath)) return null;
        return Path.Combine(sdkPath, "platform-tools", "adb");
    }
}
