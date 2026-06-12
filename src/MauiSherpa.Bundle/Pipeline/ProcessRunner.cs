using System.Diagnostics;
using System.Text;

namespace MauiSherpa.Bundle.Pipeline;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

/// <summary>Thin wrapper over <see cref="Process"/> for invoking external toolchains.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        ISherpaLog? log = null,
        CancellationToken ct = default);

    Task<string?> WhichAsync(string command, CancellationToken ct = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        ISherpaLog? log = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);
        if (workingDirectory is not null)
            psi.WorkingDirectory = workingDirectory;
        if (environment is not null)
            foreach (var (k, v) in environment)
                psi.Environment[k] = v;

        log?.Info($"$ {fileName} {string.Join(' ', psi.ArgumentList.Select(Quote))}");

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, "", $"Failed to start '{fileName}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);

        return new ProcessResult(process.ExitCode, stdout.ToString().TrimEnd(), stderr.ToString().TrimEnd());
    }

    public async Task<string?> WhichAsync(string command, CancellationToken ct = default)
    {
        try
        {
            var result = await RunAsync(
                OperatingSystem.IsWindows() ? "where" : "which",
                new[] { command },
                ct: ct);
            return result.Success ? result.StdOut.Split('\n')[0].Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Quote(string arg)
        => arg.Contains(' ') ? $"\"{arg}\"" : arg;
}
