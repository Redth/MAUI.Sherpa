using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Pipeline;

/// <summary>
/// Emits the run result (spec §7): the JSON document (stdout + a
/// <c>sherpa-output.json</c> beside the bundle) and the <c>SHERPA_*</c>
/// environment values for CI consumption.
/// </summary>
public static class SherpaOutputWriter
{
    // PascalCase keys per spec §7 example.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(SherpaResult result) => JsonSerializer.Serialize(result, JsonOptions);

    /// <summary>Writes <c>sherpa-output.json</c> next to the bundle; returns its path.</summary>
    public static string WriteSidecar(SherpaResult result, string bundlePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(bundlePath)) ?? Directory.GetCurrentDirectory();
        var path = Path.Combine(dir, "sherpa-output.json");
        File.WriteAllText(path, Serialize(result));
        return path;
    }

    /// <summary>
    /// Surfaces results as CI variables: <c>SHERPA_VERSION</c> and
    /// <c>SHERPA_&lt;PLATFORM&gt;_&lt;KIND&gt;</c> (e.g. <c>SHERPA_ANDROID_AAB</c>).
    /// Honors GitHub Actions (<c>$GITHUB_ENV</c>/<c>$GITHUB_OUTPUT</c>) and Azure
    /// DevOps (<c>##vso[task.setvariable]</c>) when those environments are detected.
    /// </summary>
    public static IReadOnlyDictionary<string, string> EmitEnvironment(SherpaResult result, TextWriter stdout)
    {
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);

        var version = result.Platforms.Values.Select(p => p.Version).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        if (version is not null)
            vars["SHERPA_VERSION"] = version;

        foreach (var (platformName, platform) in result.Platforms)
        {
            var platformToken = platformName.ToUpperInvariant();
            foreach (var (kind, path) in platform.Artifacts)
                vars[$"SHERPA_{platformToken}_{kind.ToUpperInvariant()}"] = path;
        }

        WriteToFile(Environment.GetEnvironmentVariable("GITHUB_ENV"), vars, FileFormat.KeyValue);
        WriteToFile(Environment.GetEnvironmentVariable("GITHUB_OUTPUT"), vars, FileFormat.KeyValue);

        if (string.Equals(Environment.GetEnvironmentVariable("TF_BUILD"), "True", StringComparison.OrdinalIgnoreCase))
            foreach (var (k, v) in vars)
                stdout.WriteLine($"##vso[task.setvariable variable={k}]{v}");

        return vars;
    }

    private enum FileFormat { KeyValue }

    private static void WriteToFile(string? file, IReadOnlyDictionary<string, string> vars, FileFormat _)
    {
        if (string.IsNullOrEmpty(file))
            return;
        try
        {
            var sb = new StringBuilder();
            foreach (var (k, v) in vars)
                sb.Append(k).Append('=').Append(v).Append('\n');
            File.AppendAllText(file, sb.ToString());
        }
        catch { /* non-fatal */ }
    }
}
