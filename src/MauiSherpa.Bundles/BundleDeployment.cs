namespace MauiSherpa.Bundles;

public sealed record BundleDeploymentContext
{
    public required BundleDeploymentTarget Target { get; init; }
    public required BundlePlatform Platform { get; init; }
    public required BundleArtifact Artifact { get; init; }
    public required string WorkingDirectory { get; init; }
    public required IReadOnlyDictionary<string, string> Variables { get; init; }
    public required IReadOnlySet<string> SecretValues { get; init; }
    public bool DryRun { get; init; }
}

public interface IBundleDeploymentProvider
{
    BundleDeploymentProvider Provider { get; }

    IReadOnlyList<string> Validate(BundleDeploymentContext context);

    Task<BundleDeploymentResult> DeployAsync(
        BundleDeploymentContext context,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class BundleDeploymentRegistry(IEnumerable<IBundleDeploymentProvider> providers)
{
    private readonly IReadOnlyDictionary<BundleDeploymentProvider, IBundleDeploymentProvider> _providers =
        providers.ToDictionary(provider => provider.Provider);

    public IBundleDeploymentProvider Get(BundleDeploymentProvider provider) =>
        _providers.TryGetValue(provider, out var implementation)
            ? implementation
            : throw new BundleValidationException([$"Deployment provider '{provider}' is not registered."]);
}

public static class BundleDeploymentProviderFactory
{
    public static IReadOnlyList<IBundleDeploymentProvider> CreateAll(IBundleProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        return typeof(BundleDeploymentProviderFactory).Assembly
            .GetTypes()
            .Where(type =>
                !type.IsAbstract &&
                typeof(IBundleDeploymentProvider).IsAssignableFrom(type))
            .Select(type => type.GetConstructor([typeof(IBundleProcessRunner)]))
            .Where(constructor => constructor is not null)
            .Select(constructor =>
                (IBundleDeploymentProvider)constructor!.Invoke([processRunner]))
            .ToArray();
    }
}
