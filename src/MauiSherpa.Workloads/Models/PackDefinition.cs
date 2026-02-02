using System.Text.Json.Serialization;

namespace MauiSherpa.Workloads.Models;

/// <summary>
/// Represents a pack definition from a workload manifest.
/// </summary>
public record PackDefinition
{
    /// <summary>
    /// The pack ID (NuGet package ID).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The pack version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The kind of pack: Sdk, Framework, Library, Template, or Tool.
    /// </summary>
    public string Kind { get; init; } = "Sdk";

    /// <summary>
    /// For aliased packs, the ID of the pack this aliases.
    /// </summary>
    public string? AliasTo { get; init; }

    /// <summary>
    /// Platform-specific pack mappings (RID → pack ID).
    /// </summary>
    public IReadOnlyDictionary<string, string>? AliasToByPlatform { get; init; }
}

/// <summary>
/// JSON structure for pack definition deserialization.
/// </summary>
internal class PackDefinitionJson
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("alias-to")]
    public PackAliasJson? AliasTo { get; set; }

    public PackDefinition ToModel(string id) => new()
    {
        Id = id,
        Version = Version ?? "",
        Kind = Kind ?? "Sdk",
        AliasTo = AliasTo?.Id,
        AliasToByPlatform = AliasTo?.Platforms
    };
}

/// <summary>
/// JSON structure for pack alias.
/// </summary>
internal class PackAliasJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? ExtensionData { get; set; }

    public Dictionary<string, string>? Platforms =>
        ExtensionData?.Where(kvp => kvp.Key != "id")
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? "");
}
