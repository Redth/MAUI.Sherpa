using System.Text.Json;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.NuGet;
using NuGet.Versioning;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Service for querying workload sets from NuGet.
/// </summary>
public class WorkloadSetService : IWorkloadSetService
{
    private readonly INuGetClient _nugetClient;

    /// <summary>
    /// Creates a new WorkloadSetService with the default NuGet client.
    /// </summary>
    public WorkloadSetService() : this(new NuGetClient())
    {
    }

    /// <summary>
    /// Creates a new WorkloadSetService with a custom NuGet client.
    /// </summary>
    public WorkloadSetService(INuGetClient nugetClient)
    {
        _nugetClient = nugetClient;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NuGetVersion>> GetAvailableWorkloadSetVersionsAsync(
        string featureBand,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        var packageId = GetWorkloadSetPackageId(featureBand);
        return await _nugetClient.GetPackageVersionsAsync(packageId, includePrerelease, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WorkloadSet?> GetWorkloadSetAsync(
        string featureBand,
        NuGetVersion version,
        CancellationToken cancellationToken = default)
    {
        var packageId = GetWorkloadSetPackageId(featureBand);

        // Try different possible file paths for workloadsets.json
        var possiblePaths = new[]
        {
            "data/microsoft.net.workloads.workloadset.json",
            "data/WorkloadSet.json",
            "data/workloadset.json",
            "WorkloadSet.json",
            "workloadset.json"
        };

        string? content = null;
        foreach (var path in possiblePaths)
        {
            content = await _nugetClient.GetPackageFileContentAsync(packageId, version, path, cancellationToken);
            if (content != null)
                break;
        }

        if (content == null)
            return null;

        return ParseWorkloadSet(content, featureBand, version.ToString());
    }

    /// <inheritdoc />
    public async Task<WorkloadSet?> GetLatestWorkloadSetAsync(
        string featureBand,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        var versions = await GetAvailableWorkloadSetVersionsAsync(featureBand, includePrerelease, cancellationToken);
        if (versions.Count == 0)
            return null;

        return await GetWorkloadSetAsync(featureBand, versions[0], cancellationToken);
    }

    private static string GetWorkloadSetPackageId(string featureBand) => $"Microsoft.NET.Workloads.{featureBand}";

    private static WorkloadSet? ParseWorkloadSet(string json, string featureBand, string version)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        // The workloadset.json format is: { "ManifestId": "manifestVersion/featureBand", ... }
        var workloads = JsonSerializer.Deserialize<Dictionary<string, string>>(json, options);
        if (workloads == null)
            return null;

        var entries = new Dictionary<string, WorkloadSetEntry>();

        foreach (var (manifestId, value) in workloads)
        {
            // Parse "version/featureBand" format
            var parts = value.Split('/');
            if (parts.Length >= 1)
            {
                entries[manifestId] = new WorkloadSetEntry
                {
                    ManifestId = manifestId,
                    ManifestVersion = parts[0],
                    ManifestFeatureBand = parts.Length >= 2 ? parts[1] : null
                };
            }
        }

        return new WorkloadSet
        {
            Version = version,
            FeatureBand = featureBand,
            Workloads = entries
        };
    }
}
