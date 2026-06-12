namespace MauiSherpa.Bundle.Models;

/// <summary>The JSON result emitted after a run (spec §7).</summary>
public sealed class SherpaResult
{
    public string Environment { get; set; } = "";

    public Dictionary<string, PlatformResult> Platforms { get; set; } = new();
}

public sealed class PlatformResult
{
    public string? Version { get; set; }

    /// <summary>Produced artifact paths keyed by kind (e.g. <c>Aab</c>, <c>Apk</c>, <c>Ipa</c>).</summary>
    public Dictionary<string, string> Artifacts { get; set; } = new();

    public List<DeployOutcome> Deploys { get; set; } = new();
}

public sealed class DeployOutcome
{
    public string Provider { get; set; } = "";

    /// <summary><c>Succeeded</c>, <c>Failed</c>, or <c>Skipped</c>.</summary>
    public string Status { get; set; } = "";

    public string? Url { get; set; }

    public string? Detail { get; set; }
}
