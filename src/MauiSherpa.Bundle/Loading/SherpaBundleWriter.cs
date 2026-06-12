using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Loading;

/// <summary>
/// Serializes a <see cref="SherpaBundle"/> back to the on-the-wire JSON form
/// (spec §2): a dynamic root holding an optional <c>$schema</c>, an optional
/// <c>Build</c> defaults block, and one entry per named environment. This is the
/// write-side counterpart to <see cref="SherpaBundleSerializer"/>.
/// </summary>
public static class SherpaBundleWriter
{
    public const string SchemaUrl = "https://schemas.sherpa.dev/sherpabundle/v1.json";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes the bundle to indented JSON.</summary>
    public static string Write(SherpaBundle bundle, bool includeSchema = true)
    {
        var root = new JsonObject();

        if (includeSchema)
            root[ "$schema" ] = SchemaUrl;

        if (bundle.Build is not null)
            root["Build"] = JsonSerializer.SerializeToNode(bundle.Build, WriteOptions);

        foreach (var (name, env) in bundle.Environments)
            root[name] = JsonSerializer.SerializeToNode(env, WriteOptions);

        return root.ToJsonString(WriteOptions);
    }
}
