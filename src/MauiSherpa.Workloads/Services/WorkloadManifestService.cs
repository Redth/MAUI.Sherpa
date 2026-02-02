using System.Text.Json;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.NuGet;
using NuGet.Versioning;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Service for querying workload manifests from NuGet.
/// </summary>
public class WorkloadManifestService : IWorkloadManifestService
{
    private readonly INuGetClient _nugetClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Creates a new WorkloadManifestService with the default NuGet client.
    /// </summary>
    public WorkloadManifestService() : this(new NuGetClient())
    {
    }

    /// <summary>
    /// Creates a new WorkloadManifestService with a custom NuGet client.
    /// </summary>
    public WorkloadManifestService(INuGetClient nugetClient)
    {
        _nugetClient = nugetClient;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NuGetVersion>> GetAvailableManifestVersionsAsync(
        string manifestId,
        string sdkBand,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        var packageId = GetManifestPackageId(manifestId, sdkBand);
        return await _nugetClient.GetPackageVersionsAsync(packageId, includePrerelease, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WorkloadManifest?> GetManifestAsync(
        string manifestId,
        string sdkBand,
        NuGetVersion version,
        CancellationToken cancellationToken = default)
    {
        var content = await GetManifestContentAsync(manifestId, sdkBand, version, cancellationToken);
        if (content == null)
            return null;

        return ParseManifest(content);
    }

    /// <inheritdoc />
    public async Task<JsonDocument?> GetRawManifestAsync(
        string manifestId,
        string sdkBand,
        NuGetVersion version,
        CancellationToken cancellationToken = default)
    {
        var content = await GetManifestContentAsync(manifestId, sdkBand, version, cancellationToken);
        if (content == null)
            return null;

        return JsonDocument.Parse(content, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
    }

    /// <inheritdoc />
    public async Task<WorkloadManifest?> GetLatestManifestAsync(
        string manifestId,
        string sdkBand,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        var versions = await GetAvailableManifestVersionsAsync(manifestId, sdkBand, includePrerelease, cancellationToken);
        if (versions.Count == 0)
            return null;

        return await GetManifestAsync(manifestId, sdkBand, versions[0], cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WorkloadDependencies?> GetDependenciesAsync(
        string manifestId,
        string sdkBand,
        NuGetVersion version,
        CancellationToken cancellationToken = default)
    {
        var content = await GetDependenciesContentAsync(manifestId, sdkBand, version, cancellationToken);
        if (content == null)
            return null;

        return WorkloadDependenciesParser.Parse(content);
    }

    /// <inheritdoc />
    public async Task<JsonDocument?> GetRawDependenciesAsync(
        string manifestId,
        string sdkBand,
        NuGetVersion version,
        CancellationToken cancellationToken = default)
    {
        var content = await GetDependenciesContentAsync(manifestId, sdkBand, version, cancellationToken);
        if (content == null)
            return null;

        return JsonDocument.Parse(content, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
    }

    private async Task<string?> GetManifestContentAsync(
        string manifestId,
        string sdkBand,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        var packageId = GetManifestPackageId(manifestId, sdkBand);

        // Try different possible file paths for WorkloadManifest.json
        var possiblePaths = new[]
        {
            "data/WorkloadManifest.json",
            "data/workloadmanifest.json",
            "WorkloadManifest.json",
            "workloadmanifest.json"
        };

        foreach (var path in possiblePaths)
        {
            var content = await _nugetClient.GetPackageFileContentAsync(packageId, version, path, cancellationToken);
            if (content != null)
                return content;
        }

        return null;
    }

    private async Task<string?> GetDependenciesContentAsync(
        string manifestId,
        string sdkBand,
        NuGetVersion version,
        CancellationToken cancellationToken)
    {
        var packageId = GetManifestPackageId(manifestId, sdkBand);

        var possiblePaths = new[]
        {
            "data/WorkloadDependencies.json",
            "data/workloaddependencies.json",
            "WorkloadDependencies.json",
            "workloaddependencies.json"
        };

        foreach (var path in possiblePaths)
        {
            var content = await _nugetClient.GetPackageFileContentAsync(packageId, version, path, cancellationToken);
            if (content != null)
                return content;
        }

        return null;
    }

    private static string GetManifestPackageId(string manifestId, string sdkBand) =>
        $"{manifestId}.Manifest-{sdkBand}";

    private static WorkloadManifest? ParseManifest(string json)
    {
        var manifestJson = JsonSerializer.Deserialize<WorkloadManifestJson>(json, JsonOptions);
        if (manifestJson == null)
            return null;

        var workloads = manifestJson.Workloads?
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToModel(kvp.Key))
            ?? new Dictionary<string, WorkloadDefinition>();

        var packs = manifestJson.Packs?
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToModel(kvp.Key))
            ?? new Dictionary<string, PackDefinition>();

        return new WorkloadManifest
        {
            Version = manifestJson.Version ?? "",
            Description = manifestJson.Description,
            DependsOn = manifestJson.DependsOn ?? new Dictionary<string, string>(),
            Workloads = workloads,
            Packs = packs
        };
    }
}
