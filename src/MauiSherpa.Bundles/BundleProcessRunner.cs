using System.Diagnostics;

namespace MauiSherpa.Bundles;

public sealed record BundleProcessRequest
{
    public required string FileName { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> SecretValues { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}

public sealed record BundleProcessResult(int ExitCode, string StandardOutput, string StandardError);

public interface IBundleProcessRunner
{
    Task<BundleProcessResult> RunAsync(
        BundleProcessRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class BundleProcessRunner : IBundleProcessRunner
{
    public async Task<BundleProcessResult> RunAsync(
        BundleProcessRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var redactor = new SecretRedactor(request.SecretValues);
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            startInfo.WorkingDirectory = request.WorkingDirectory;
        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (var (key, value) in request.Environment)
            startInfo.Environment[key] = value;

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdoutTask = ReadAsync(process.StandardOutput, redactor, progress, cancellationToken);
        var stderrTask = ReadAsync(process.StandardError, redactor, progress, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new BundleProcessResult(process.ExitCode, stdout, stderr);
    }

    private static async Task<string> ReadAsync(
        StreamReader reader,
        SecretRedactor redactor,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var redacted = redactor.Redact(line);
            lines.Add(redacted);
            progress?.Report(redacted);
        }
        return string.Join(Environment.NewLine, lines);
    }
}
