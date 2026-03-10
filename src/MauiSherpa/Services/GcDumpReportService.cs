using System.Globalization;
using System.Text.RegularExpressions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Services;

/// <summary>
/// Parses .gcdump files by running <c>dotnet-gcdump report</c> and parsing the heapstat output.
/// </summary>
public class GcDumpReportService : IGcDumpReportService
{
    private readonly ILoggingService _logger;

    public GcDumpReportService(ILoggingService logger)
    {
        _logger = logger;
    }

    public async Task<GcDumpReport?> GetReportAsync(string gcdumpPath, CancellationToken ct = default)
    {
        if (!File.Exists(gcdumpPath))
        {
            _logger.LogWarning($"GC dump file not found: {gcdumpPath}");
            return null;
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet-gcdump",
                ArgumentList = { "report", gcdumpPath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                _logger.LogError("Failed to start dotnet-gcdump process");
                return null;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                _logger.LogError($"dotnet-gcdump report failed (exit {process.ExitCode}): {stderr}");
                return null;
            }

            return GcDumpReportParser.ParseHeapStatOutput(stdout);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to run dotnet-gcdump report: {ex.Message}", ex);
            return null;
        }
    }
}
