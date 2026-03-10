using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

/// <summary>
/// Converts profiling artifacts between formats using dotnet CLI tools.
/// </summary>
public class ProfilingArtifactConverterService : IProfilingArtifactConverterService
{
    private readonly ILoggingService _logger;

    public ProfilingArtifactConverterService(ILoggingService logger)
    {
        _logger = logger;
    }

    public async Task<string?> ConvertToSpeedscopeAsync(string nettracePath, CancellationToken ct = default)
    {
        if (!File.Exists(nettracePath))
        {
            _logger.LogWarning($"Trace file not found: {nettracePath}");
            return null;
        }

        // dotnet-trace convert appends ".speedscope.json" to the -o path,
        // so we strip .nettrace and set the base output name
        var dir = Path.GetDirectoryName(nettracePath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(nettracePath);
        var outputPath = Path.Combine(dir, baseName);

        // The actual output file will be "{baseName}.speedscope.json"
        var expectedOutputPath = Path.Combine(dir, $"{baseName}.speedscope.json");

        // If already converted, return existing
        if (File.Exists(expectedOutputPath))
        {
            _logger.LogInformation($"Speedscope file already exists: {expectedOutputPath}");
            return expectedOutputPath;
        }

        try
        {
            _logger.LogInformation($"Converting {nettracePath} to speedscope format...");

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet-trace",
                ArgumentList = { "convert", nettracePath, "--format", "speedscope", "-o", outputPath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                _logger.LogError("Failed to start dotnet-trace convert process");
                return null;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            // dotnet-trace convert returns exit code 1 even on success sometimes,
            // so check for the output file instead
            if (File.Exists(expectedOutputPath))
            {
                _logger.LogInformation($"Conversion complete: {expectedOutputPath}");
                return expectedOutputPath;
            }

            // dotnet-trace sometimes appends double extension — check for that too
            var doubleExtPath = Path.Combine(dir, $"{baseName}.speedscope.speedscope.json");
            if (File.Exists(doubleExtPath))
            {
                // Rename to the expected single-extension name
                File.Move(doubleExtPath, expectedOutputPath);
                _logger.LogInformation($"Conversion complete (renamed): {expectedOutputPath}");
                return expectedOutputPath;
            }

            // Search for any .speedscope.json file in the directory
            var speedscopeFiles = Directory.GetFiles(dir, "*.speedscope.json");
            var newestSpeedscope = speedscopeFiles
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (newestSpeedscope is not null)
            {
                _logger.LogInformation($"Found speedscope output at: {newestSpeedscope}");
                return newestSpeedscope;
            }

            _logger.LogError($"dotnet-trace convert produced no output. stdout: {stdout}, stderr: {stderr}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to convert trace: {ex.Message}", ex);
            return null;
        }
    }
}
