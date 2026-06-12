using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Deploy;

/// <summary>
/// Resolves a <c>Provider</c> name to an <see cref="IDeployProvider"/>
/// (spec §4: "matches by name to a registered provider plugin").
/// </summary>
public sealed class DeployProviderRegistry
{
    private readonly Dictionary<string, IDeployProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public DeployProviderRegistry(IEnumerable<IDeployProvider> providers)
    {
        foreach (var p in providers)
            _providers[p.Name] = p;
    }

    /// <summary>The built-in provider set (spec §4 table).</summary>
    public static DeployProviderRegistry CreateDefault() => new(new IDeployProvider[]
    {
        new TestFlightProvider(),
        new FirebaseProvider(),
        new PlayStoreProvider(),
        new AmazonAppStoreProvider(),
        new MicrosoftStoreProvider(),
    });

    public bool TryResolve(string? provider, out IDeployProvider resolved)
    {
        if (!string.IsNullOrWhiteSpace(provider) && _providers.TryGetValue(provider, out var p))
        {
            resolved = p;
            return true;
        }
        resolved = null!;
        return false;
    }

    public IReadOnlyCollection<string> ProviderNames => _providers.Keys;
}
