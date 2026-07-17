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
    public System.Text.Json.JsonElement AliasTo { get; set; }

    public PackDefinition ToModel(string id)
    {
        string? directAlias = null;
        Dictionary<string, string>? platformAliases = null;

        if (AliasTo.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            directAlias = AliasTo.GetString();
        }
        else if (AliasTo.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            platformAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in AliasTo.EnumerateObject())
            {
                var value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    platformAliases[property.Name] = value;
            }
        }

        return new PackDefinition
        {
            Id = id,
            Version = Version ?? "",
            Kind = Kind ?? "Sdk",
            AliasTo = directAlias,
            AliasToByPlatform = platformAliases
        };
    }
}
