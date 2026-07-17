using System.Text.Json;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Reads the same per-feature-band install state used by the .NET SDK workload commands.
/// </summary>
public static class WorkloadInstallStateReader
{
    public static DotnetWorkloadUpdateMode ReadUpdateMode(DotnetWorkloadTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var path = GetPath(target);

        if (!File.Exists(path))
            return DotnetWorkloadUpdateMode.WorkloadSet;

        using var document = ReadDocument(path);

        if (!document.RootElement.TryGetProperty("useWorkloadSets", out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return DotnetWorkloadUpdateMode.WorkloadSet;

        return value.ValueKind switch
        {
            JsonValueKind.True => DotnetWorkloadUpdateMode.WorkloadSet,
            JsonValueKind.False => DotnetWorkloadUpdateMode.Manifests,
            _ => throw new FormatException(
                $"The workload install-state value 'useWorkloadSets' in '{path}' is not a boolean.")
        };
    }

    public static IReadOnlyList<DotnetManifestVersion> ReadManifestVersions(
        DotnetWorkloadTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var path = GetPath(target);
        if (!File.Exists(path))
            return [];

        using var document = ReadDocument(path);
        if (!document.RootElement.TryGetProperty("manifests", out var manifests) ||
            manifests.ValueKind == JsonValueKind.Null)
            return [];
        if (manifests.ValueKind != JsonValueKind.Object)
            throw new FormatException($"The workload install-state value 'manifests' in '{path}' is not an object.");

        var versions = new List<DotnetManifestVersion>();
        foreach (var manifest in manifests.EnumerateObject())
        {
            if (manifest.Value.ValueKind != JsonValueKind.String)
                throw new FormatException(
                    $"The workload install-state manifest '{manifest.Name}' in '{path}' does not have a string version.");

            var value = manifest.Value.GetString() ?? string.Empty;
            var parts = value.Split('/');
            if (parts.Length is < 1 or > 2 || string.IsNullOrWhiteSpace(parts[0]))
                throw new FormatException(
                    $"The workload install-state manifest '{manifest.Name}' in '{path}' has invalid version '{value}'.");

            versions.Add(new DotnetManifestVersion
            {
                ManifestId = manifest.Name,
                Version = parts[0],
                FeatureBand = parts.Length == 2
                    ? parts[1]
                    : target.FeatureBand.ToString()
            });
        }

        return versions;
    }

    private static string GetPath(DotnetWorkloadTarget target) =>
        Path.Combine(
            target.InstallRoot,
            "metadata",
            "workloads",
            CanonicalArchitecture(target.Architecture),
            target.FeatureBand.ToString(),
            "InstallState",
            "default.json");

    private static JsonDocument ReadDocument(string path) =>
        JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

    private static string CanonicalArchitecture(string architecture) =>
        architecture.Trim().ToLowerInvariant() switch
        {
            "arm64" => "Arm64",
            "arm" => "Arm",
            "x64" => "X64",
            "x86" => "X86",
            _ => architecture
        };
}
