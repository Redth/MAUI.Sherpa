using System.Text.Json.Serialization;

namespace MauiSherpa.Workloads.Models;

/// <summary>
/// Represents a workload set that maps workload IDs to their manifest versions.
/// This corresponds to the contents of a Microsoft.NET.Workloads.{feature-band} package.
/// </summary>
public record WorkloadSet
{
    /// <summary>
    /// The workload set version (e.g., "9.0.100").
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The SDK feature band this workload set applies to.
    /// </summary>
    public required string FeatureBand { get; init; }

    /// <summary>
    /// The workload manifest entries mapping workload IDs to versions.
    /// </summary>
    public IReadOnlyDictionary<string, WorkloadSetEntry> Workloads { get; init; } = new Dictionary<string, WorkloadSetEntry>();
}

/// <summary>
/// Represents a single workload entry within a workload set.
/// </summary>
public record WorkloadSetEntry
{
    /// <summary>
    /// The workload manifest ID (e.g., "microsoft.net.sdk.maui").
    /// </summary>
    public required string ManifestId { get; init; }

    /// <summary>
    /// The manifest version (e.g., "9.0.100").
    /// </summary>
    public required string ManifestVersion { get; init; }

    /// <summary>
    /// The feature band for the manifest package name.
    /// </summary>
    public string? ManifestFeatureBand { get; init; }
}

/// <summary>
/// JSON structure for the workloadsets.json file inside workload set packages.
/// </summary>
internal class WorkloadSetJson
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("workloads")]
    public Dictionary<string, string>? Workloads { get; set; }
}
