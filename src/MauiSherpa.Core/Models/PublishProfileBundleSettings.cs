namespace MauiSherpa.Core.Interfaces;

/// <summary>
/// Extra configuration captured on a <see cref="PublishProfile"/> so it can be
/// assembled into a <c>.sherpabundle</c> file (see sherpa-spec.md). The signing
/// material (certificates, profiles, keystores) is still resolved from the
/// profile's existing Apple/Android configs; these settings add the bundle-only
/// concepts the spec needs — an environment name, build substitution maps, and
/// deploy destinations.
/// </summary>
public record PublishProfileBundleSettings
{
    /// <summary>
    /// The environment key the bundle is written under (spec §2 — bundles are
    /// keyed by environment, e.g. <c>Production</c>). Defaults to "Production".
    /// </summary>
    public string EnvironmentName { get; init; } = "Production";

    /// <summary>Platforms to emit blocks for. Empty means "infer from the profile's configs".</summary>
    public List<BundlePlatform> Platforms { get; init; } = new();

    /// <summary>Environment-level <c>${name}</c> variables (spec §5.1).</summary>
    public Dictionary<string, string> Variables { get; init; } = new();

    /// <summary>Environment-level replace tokens (spec §5.2).</summary>
    public Dictionary<string, string> ReplaceTokens { get; init; } = new();

    /// <summary>Environment-level MSBuild properties (spec §5.3).</summary>
    public Dictionary<string, string> MSBuildProperties { get; init; } = new();

    /// <summary>Deploy destinations, distributed into each platform's <c>Deploy[]</c> (spec §4).</summary>
    public List<PublishProfileDeployTarget> DeployTargets { get; init; } = new();
}

/// <summary>
/// A single deploy destination configured on a profile for bundle assembly.
/// Provider-specific fields are split so that secret-bearing values can be
/// resolved at build time rather than stored verbatim.
/// </summary>
public record PublishProfileDeployTarget(
    string Provider,
    BundlePlatform Platform
)
{
    /// <summary>
    /// App Store Connect / TestFlight: reference an existing Apple Identity to
    /// source the <c>.p8</c> API key, <c>KeyId</c>, and <c>IssuerId</c>.
    /// </summary>
    public string? AppleIdentityId { get; init; }

    /// <summary>Plain provider fields entered directly (e.g. <c>AppId</c>, <c>Track</c>).</summary>
    public Dictionary<string, string> Fields { get; init; } = new();

    /// <summary>
    /// Provider fields sourced from the managed secrets store, resolved to inline
    /// values at build time. Maps a bundle field name → managed secret source key.
    /// </summary>
    public Dictionary<string, string> SecretFields { get; init; } = new();
}

/// <summary>Target platforms a bundle block can describe (spec §3).</summary>
public enum BundlePlatform
{
    iOS,
    Android,
    MacOS,
    MacCatalyst,
    Windows
}
