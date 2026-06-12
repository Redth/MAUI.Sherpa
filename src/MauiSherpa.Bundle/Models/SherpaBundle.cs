using System.Text.Json.Serialization;

namespace MauiSherpa.Bundle.Models;

/// <summary>
/// The root of a <c>.sherpabundle</c> document (spec §2). Holds the optional
/// top-level <c>Build</c> defaults plus every named environment.
/// </summary>
public sealed class SherpaBundle
{
    /// <summary>
    /// Reserved top-level <c>Build</c> block — baseline defaults merged into every
    /// environment as the lowest-precedence layer (spec §2, §5).
    /// </summary>
    public CommonConfig? Build { get; init; }

    /// <summary>Named environments keyed by name (e.g. <c>Production</c>).</summary>
    public IReadOnlyDictionary<string, EnvironmentBlock> Environments { get; init; }
        = new Dictionary<string, EnvironmentBlock>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves an environment by name, case-insensitively (spec §1: the
    /// <c>-environment</c> match is case-insensitive against bundle keys).
    /// </summary>
    public bool TryGetEnvironment(string name, out string canonicalName, out EnvironmentBlock environment)
    {
        foreach (var (key, value) in Environments)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                canonicalName = key;
                environment = value;
                return true;
            }
        }

        canonicalName = name;
        environment = null!;
        return false;
    }
}

/// <summary>
/// The set of substitution/override dictionaries that appear at the
/// <c>Build</c>, environment, and platform-build layers (spec §5).
/// </summary>
public class CommonConfig
{
    /// <summary>Default values for <c>${name}</c> substitution (spec §5.1).</summary>
    public Dictionary<string, string>? Variables { get; init; }

    /// <summary>Tokens substituted into source/asset files during build (spec §5.2).</summary>
    public Dictionary<string, string>? ReplaceTokens { get; init; }

    /// <summary>MSBuild properties passed as <c>-p:Name=Value</c> (spec §5.3).</summary>
    public Dictionary<string, string>? MSBuildProperties { get; init; }
}

/// <summary>A named environment block (spec §2.1).</summary>
public sealed class EnvironmentBlock : CommonConfig
{
    public AndroidPlatform? Android { get; init; }

    [JsonPropertyName("iOS")]
    public ApplePlatform? IOS { get; init; }

    public MacPlatform? MacOS { get; init; }

    public MacPlatform? MacCatalyst { get; init; }

    public WindowsPlatform? Windows { get; init; }
}
