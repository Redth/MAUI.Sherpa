using System.Text.Json;
using MauiSherpa.Workloads.Models;
using NuGet.Versioning;

namespace MauiSherpa.Workloads.Services;

public static class DotnetWorkloadParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static DotnetWorkloadListResult ParseMachineReadableList(string output)
    {
        var json = ExtractJsonObject(output);
        var payload = JsonSerializer.Deserialize<MachineReadableList>(json, JsonOptions)
            ?? throw new FormatException("The workload list response was empty.");

        return new DotnetWorkloadListResult
        {
            Installed = (payload.Installed ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => new DotnetInstalledWorkload { Id = id })
                .OrderBy(w => w.Id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Updates = (payload.UpdateAvailable ?? [])
                .Where(update => !string.IsNullOrWhiteSpace(update.WorkloadId))
                .Select(update => new DotnetWorkloadUpdate
                {
                    WorkloadId = update.WorkloadId!,
                    Description = update.Description,
                    ExistingManifestVersion = update.ExistingManifestVersion,
                    AvailableManifestVersion = update.AvailableUpdateManifestVersion
                })
                .OrderBy(update => update.WorkloadId, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            UsedMachineReadableOutput = true
        };
    }

    public static DotnetWorkloadListResult ParsePlainList(string output)
    {
        var installed = new List<DotnetInstalledWorkload>();
        var inTable = false;

        foreach (var rawLine in output.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("---", StringComparison.Ordinal))
            {
                inTable = true;
                continue;
            }

            if (!inTable || string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("Use `dotnet", StringComparison.Ordinal))
                continue;

            var columns = System.Text.RegularExpressions.Regex.Split(line.Trim(), @"\s{2,}");
            if (columns.Length < 1 || string.IsNullOrWhiteSpace(columns[0]))
                continue;

            installed.Add(new DotnetInstalledWorkload
            {
                Id = columns[0],
                ManifestVersion = columns.Length > 1 ? columns[1] : null,
                InstallationSource = columns.Length > 2 ? columns[2] : null
            });
        }

        return new DotnetWorkloadListResult
        {
            Installed = installed.OrderBy(w => w.Id, StringComparer.OrdinalIgnoreCase).ToList(),
            UsedMachineReadableOutput = false
        };
    }

    public static string ParseWorkloadVersion(string output)
    {
        var value = output.Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return string.IsNullOrWhiteSpace(value)
            ? throw new FormatException("The workload version response was empty.")
            : value;
    }

    public static DotnetWorkloadUpdateMode ParseConfiguredUpdateMode(string output)
    {
        if (output.Contains("workload-set", StringComparison.OrdinalIgnoreCase))
            return DotnetWorkloadUpdateMode.WorkloadSet;
        if (output.Contains("manifests", StringComparison.OrdinalIgnoreCase))
            return DotnetWorkloadUpdateMode.Manifests;
        throw new FormatException("The workload update mode response was not understood.");
    }

    public static DotnetWorkloadUpdateMode InferUpdateMode(string workloadVersion) =>
        !workloadVersion.Contains("-manifests.", StringComparison.OrdinalIgnoreCase) &&
        NuGetVersion.TryParse(workloadVersion, out _)
            ? DotnetWorkloadUpdateMode.WorkloadSet
            : DotnetWorkloadUpdateMode.Unknown;

    public static IReadOnlyList<DotnetWorkloadSetVersion> ParseAvailableSetVersions(string output)
    {
        var json = ExtractJsonArray(output);
        var payload = JsonSerializer.Deserialize<List<WorkloadVersionPayload>>(json, JsonOptions) ?? [];
        return payload
            .Where(item => !string.IsNullOrWhiteSpace(item.WorkloadVersion))
            .Select(item => new DotnetWorkloadSetVersion
            {
                Version = item.WorkloadVersion!,
                IsPrerelease = NuGetVersion.TryParse(item.WorkloadVersion, out var version) && version.IsPrerelease
            })
            .OrderByDescending(item => NuGetVersion.TryParse(item.Version, out var version) ? version : new NuGetVersion(0, 0, 0))
            .ToList();
    }

    public static IReadOnlyList<DotnetManifestVersion> ParseManifestVersions(string output)
    {
        var json = ExtractJsonObject(output);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("manifestVersions", out var versions) ||
            versions.ValueKind != JsonValueKind.Object)
            throw new FormatException("The workload set response has no manifestVersions object.");

        var result = new List<DotnetManifestVersion>();
        foreach (var property in versions.EnumerateObject())
        {
            var value = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(value))
                continue;
            var slash = value.LastIndexOf('/');
            result.Add(new DotnetManifestVersion
            {
                ManifestId = property.Name,
                Version = slash > 0 ? value[..slash] : value,
                FeatureBand = slash > 0 ? value[(slash + 1)..] : string.Empty
            });
        }

        return result.OrderBy(item => item.ManifestId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ExtractJsonObject(string output)
    {
        var start = output.IndexOf('{');
        var end = output.LastIndexOf('}');
        if (start < 0 || end < start)
            throw new FormatException("No JSON object was found in the workload CLI output.");
        return output[start..(end + 1)];
    }

    private static string ExtractJsonArray(string output)
    {
        var start = output.IndexOf('[');
        var end = output.LastIndexOf(']');
        if (start < 0 || end < start)
            throw new FormatException("No JSON array was found in the workload CLI output.");
        return output[start..(end + 1)];
    }

    private sealed class MachineReadableList
    {
        public List<string>? Installed { get; set; }
        public List<UpdatePayload>? UpdateAvailable { get; set; }
    }

    private sealed class UpdatePayload
    {
        public string? ExistingManifestVersion { get; set; }
        public string? AvailableUpdateManifestVersion { get; set; }
        public string? Description { get; set; }
        public string? WorkloadId { get; set; }
    }

    private sealed class WorkloadVersionPayload
    {
        public string? WorkloadVersion { get; set; }
    }
}
