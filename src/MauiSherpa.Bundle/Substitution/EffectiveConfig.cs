namespace MauiSherpa.Bundle.Substitution;

/// <summary>
/// The fully-merged, variable-substituted configuration for one platform
/// (spec §5.2/§5.3): the tokens to write into files and the MSBuild properties
/// to pass to the build.
/// </summary>
public sealed class EffectiveConfig
{
    /// <summary>Token names are literal, so compared case-sensitively.</summary>
    public Dictionary<string, string> ReplaceTokens { get; init; } = new(StringComparer.Ordinal);

    /// <summary>MSBuild property names are case-insensitive (matches MSBuild + spec §5.4).</summary>
    public Dictionary<string, string> MSBuildProperties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
