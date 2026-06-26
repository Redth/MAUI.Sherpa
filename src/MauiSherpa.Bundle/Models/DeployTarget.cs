using System.Text.Json;
using System.Text.Json.Serialization;

namespace MauiSherpa.Bundle.Models;

/// <summary>
/// A single deploy destination (spec §4). Every entry must carry a
/// <c>Provider</c>; remaining fields are provider-specific and captured as
/// extension data so unknown providers/fields round-trip cleanly.
/// </summary>
public sealed class DeployTarget
{
    public string? Provider { get; init; }

    // A regular `set` (not `init`): System.Text.Json's source generator rejects
    // an init-only [JsonExtensionData] property, and bundles are serialized through
    // a source-generated context for AOT/trim safety.
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets a provider-specific string field (case-insensitive), or null.</summary>
    public string? GetString(string name)
    {
        foreach (var (key, value) in Fields)
        {
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                continue;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True or JsonValueKind.False => value.GetBoolean().ToString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.GetRawText(),
            };
        }
        return null;
    }

    /// <summary>Gets a provider-specific string array field (case-insensitive), or null.</summary>
    public IReadOnlyList<string>? GetStringArray(string name)
    {
        foreach (var (key, value) in Fields)
        {
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (value.ValueKind != JsonValueKind.Array)
                return null;

            var items = new List<string>();
            foreach (var element in value.EnumerateArray())
                if (element.ValueKind == JsonValueKind.String && element.GetString() is { } s)
                    items.Add(s);
            return items;
        }
        return null;
    }
}
