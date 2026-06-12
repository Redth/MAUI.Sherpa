using System.Text.Json;
using System.Text.Json.Nodes;
using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Loading;

/// <summary>
/// Deserializes the on-the-wire JSON form of a bundle into a <see cref="SherpaBundle"/>.
/// The root is a dynamic object — a reserved <c>Build</c> key plus an arbitrary
/// set of named environments (spec §2) — so it is parsed as a node tree first
/// and each environment is bound individually.
/// </summary>
public static class SherpaBundleSerializer
{
    private const string BuildKey = "Build";
    private const string SchemaKey = "$schema";

    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static SherpaBundle Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(
                utf8Json.ToArray(),
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
        }
        catch (JsonException ex)
        {
            throw new SherpaBundleException($"Bundle is not valid JSON: {ex.Message}", ex);
        }

        if (root is not JsonObject obj)
            throw new SherpaBundleException("Bundle root must be a JSON object.");

        CommonConfig? build = null;
        var environments = new Dictionary<string, EnvironmentBlock>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, node) in obj)
        {
            if (string.Equals(key, SchemaKey, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(key, BuildKey, StringComparison.OrdinalIgnoreCase))
            {
                build = node.Deserialize<CommonConfig>(Options);
                continue;
            }

            var env = node.Deserialize<EnvironmentBlock>(Options);
            if (env is not null)
                environments[key] = env;
        }

        return new SherpaBundle { Build = build, Environments = environments };
    }

    public static SherpaBundle Deserialize(string json)
        => Deserialize(System.Text.Encoding.UTF8.GetBytes(json));
}
