using System.Text.Json.Serialization;

namespace MauiSherpa.Workloads.Models;

/// <summary>
/// Represents a parsed WorkloadManifest.json file.
/// </summary>
public record WorkloadManifest
{
    /// <summary>
    /// The manifest version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Description of the manifest content/purpose.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Dependencies on other manifests (manifest ID → minimum version).
    /// </summary>
    public IReadOnlyDictionary<string, string> DependsOn { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Workload definitions keyed by workload ID.
    /// </summary>
    public IReadOnlyDictionary<string, WorkloadDefinition> Workloads { get; init; } = new Dictionary<string, WorkloadDefinition>();

    /// <summary>
    /// Pack definitions keyed by pack ID.
    /// </summary>
    public IReadOnlyDictionary<string, PackDefinition> Packs { get; init; } = new Dictionary<string, PackDefinition>();
}

/// <summary>
/// JSON structure for WorkloadManifest.json deserialization.
/// </summary>
internal class WorkloadManifestJson
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("depends-on")]
    public Dictionary<string, string>? DependsOn { get; set; }

    [JsonPropertyName("workloads")]
    public Dictionary<string, WorkloadDefinitionJson>? Workloads { get; set; }

    [JsonPropertyName("packs")]
    public Dictionary<string, PackDefinitionJson>? Packs { get; set; }
}
