using MauiSherpa.Bundle.Models;
using MauiSherpa.Bundle.Substitution;

namespace MauiSherpa.Bundle.Pipeline;

/// <summary>Run-wide state shared across steps and platforms.</summary>
public sealed class SherpaContext
{
    public required SherpaBundle Bundle { get; init; }
    public required string EnvironmentName { get; init; }
    public required EnvironmentBlock Environment { get; init; }
    public required SherpaRunOptions Options { get; init; }
    public required ProjectInfo Project { get; init; }
    public required VariableResolver Variables { get; init; }
    public required ISherpaLog Log { get; init; }
    public required IProcessRunner Process { get; init; }

    /// <summary>Scratch directory for materialized signing assets (keystores, certs, profiles).</summary>
    public required string ScratchDirectory { get; init; }

    public SherpaResult Result { get; } = new();

    public CancellationToken CancellationToken { get; init; }
}

/// <summary>Per-platform state threaded setup → build → deploy.</summary>
public sealed class PlatformContext
{
    public required SherpaContext Run { get; init; }
    public required SherpaPlatform Platform { get; init; }
    public required EffectiveConfig Config { get; init; }
    public required PlatformResult Result { get; init; }

    /// <summary>
    /// Signing/MSBuild properties contributed by the setup step (e.g.
    /// <c>AndroidSigningKeyStore</c>, <c>CodesignKey</c>) that the build step
    /// must pass to MSBuild in addition to <see cref="EffectiveConfig.MSBuildProperties"/>.
    /// </summary>
    public Dictionary<string, string> SigningProperties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ISherpaLog Log => Run.Log;
    public IProcessRunner Process => Run.Process;
    public CancellationToken CancellationToken => Run.CancellationToken;

    /// <summary>All MSBuild properties to pass to the build: effective config + signing.</summary>
    public IReadOnlyDictionary<string, string> AllMSBuildProperties()
    {
        var merged = new Dictionary<string, string>(Config.MSBuildProperties, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in SigningProperties)
            merged[k] = v;
        return merged;
    }
}
