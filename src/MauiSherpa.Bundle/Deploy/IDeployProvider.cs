using MauiSherpa.Bundle.Loading;
using MauiSherpa.Bundle.Models;
using MauiSherpa.Bundle.Pipeline;
using MauiSherpa.Bundle.Substitution;

namespace MauiSherpa.Bundle.Deploy;

/// <summary>Inputs for a single deploy (one <see cref="DeployTarget"/> on one platform).</summary>
public sealed class DeployContext
{
    public required PlatformContext Platform { get; init; }
    public required DeployTarget Target { get; init; }
    public required VariableResolver Variables { get; init; }

    public ISherpaLog Log => Platform.Log;
    public IProcessRunner Process => Platform.Process;
    public CancellationToken CancellationToken => Platform.CancellationToken;
    public string ScratchDirectory => Platform.Run.ScratchDirectory;

    /// <summary>A provider-specific string field with <c>${}</c> resolved, or null.</summary>
    public string? Field(string name)
    {
        var raw = Target.GetString(name);
        return raw is null ? null : Variables.Resolve(raw);
    }

    /// <summary>A required field; throws a clear error when missing.</summary>
    public string RequireField(string name)
        => Field(name) ?? throw new SherpaBundleException(
            $"Deploy provider '{Target.Provider}' requires field '{name}'.");

    /// <summary>The primary artifact produced for this platform, by preferred kind order.</summary>
    public string? PrimaryArtifact(params string[] preferredKinds)
    {
        foreach (var kind in preferredKinds)
            if (Platform.Result.Artifacts.TryGetValue(kind, out var path))
                return path;
        return Platform.Result.Artifacts.Values.FirstOrDefault();
    }
}

/// <summary>A deployment backend matched by <see cref="Name"/> against <c>Provider</c> (spec §4).</summary>
public interface IDeployProvider
{
    /// <summary>Provider name, matched case-insensitively against the bundle's <c>Provider</c>.</summary>
    string Name { get; }

    /// <summary>Platforms this provider supports.</summary>
    bool Supports(SherpaPlatform platform);

    Task<DeployOutcome> DeployAsync(DeployContext ctx);
}

public abstract class DeployProviderBase : IDeployProvider
{
    public abstract string Name { get; }
    public abstract bool Supports(SherpaPlatform platform);
    public abstract Task<DeployOutcome> DeployAsync(DeployContext ctx);

    protected DeployOutcome Succeeded(string? url = null, string? detail = null)
        => new() { Provider = Name, Status = "Succeeded", Url = url, Detail = detail };

    protected DeployOutcome Failed(string detail)
        => new() { Provider = Name, Status = "Failed", Detail = detail };

    protected DeployOutcome Skipped(string detail)
        => new() { Provider = Name, Status = "Skipped", Detail = detail };
}
