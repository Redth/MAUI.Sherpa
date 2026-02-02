using System.Text.Json.Serialization;

namespace MauiSherpa.Workloads.Models;

/// <summary>
/// Represents a workload definition from a manifest.
/// </summary>
public record WorkloadDefinition
{
    /// <summary>
    /// The workload ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// User-visible description for the workload.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// If true, this workload can only be extended, not installed directly.
    /// </summary>
    public bool IsAbstract { get; init; }

    /// <summary>
    /// The workload kind: "dev" (default) or "build".
    /// </summary>
    public string Kind { get; init; } = "dev";

    /// <summary>
    /// IDs of packs included in this workload.
    /// </summary>
    public IReadOnlyList<string> Packs { get; init; } = [];

    /// <summary>
    /// IDs of base workloads whose packs should be included.
    /// </summary>
    public IReadOnlyList<string> Extends { get; init; } = [];

    /// <summary>
    /// Platform restrictions (RIDs) for this workload.
    /// </summary>
    public IReadOnlyList<string> Platforms { get; init; } = [];

    /// <summary>
    /// If set, this workload redirects to another workload ID.
    /// </summary>
    public string? RedirectTo { get; init; }
}

/// <summary>
/// JSON structure for workload definition deserialization.
/// </summary>
internal class WorkloadDefinitionJson
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("abstract")]
    public bool? Abstract { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("packs")]
    public List<string>? Packs { get; set; }

    [JsonPropertyName("extends")]
    public List<string>? Extends { get; set; }

    [JsonPropertyName("platforms")]
    public List<string>? Platforms { get; set; }

    [JsonPropertyName("redirect-to")]
    public string? RedirectTo { get; set; }

    public WorkloadDefinition ToModel(string id) => new()
    {
        Id = id,
        Description = Description,
        IsAbstract = Abstract ?? false,
        Kind = Kind ?? "dev",
        Packs = Packs ?? [],
        Extends = Extends ?? [],
        Platforms = Platforms ?? [],
        RedirectTo = RedirectTo
    };
}
