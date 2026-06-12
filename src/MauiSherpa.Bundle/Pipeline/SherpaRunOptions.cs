using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Pipeline;

/// <summary>
/// Fully-parsed inputs for a single <c>sherpacli</c> invocation (spec §1).
/// </summary>
public sealed class SherpaRunOptions
{
    /// <summary>Path to the <c>.sherpabundle</c> file (positional).</summary>
    public required string BundlePath { get; init; }

    /// <summary>Selected environment name (<c>-environment</c>, required).</summary>
    public required string Environment { get; init; }

    /// <summary>
    /// Requested platforms (<c>-platform</c>). Null means "every platform defined
    /// in the selected environment".
    /// </summary>
    public IReadOnlyList<SherpaPlatform>? Platforms { get; init; }

    /// <summary>Requested steps in execution order (<c>-step</c>); defaults to all.</summary>
    public IReadOnlyList<SherpaStep> Steps { get; init; } =
        new[] { SherpaStep.Setup, SherpaStep.Build, SherpaStep.Deploy };

    /// <summary>Explicit project path (<c>-project</c>); null triggers inference (spec §6).</summary>
    public string? ProjectPath { get; init; }

    public IReadOnlyDictionary<string, string> Variables { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> ReplaceTokens { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> MSBuildProperties { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>When true, suppress human-readable logs and emit only the JSON result.</summary>
    public bool JsonOnly { get; init; }
}
