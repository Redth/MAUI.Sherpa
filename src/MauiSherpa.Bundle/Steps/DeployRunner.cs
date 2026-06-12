using MauiSherpa.Bundle.Deploy;
using MauiSherpa.Bundle.Models;
using MauiSherpa.Bundle.Pipeline;

namespace MauiSherpa.Bundle.Steps;

/// <summary>
/// The <c>deploy</c> step (spec §4): runs each <see cref="DeployTarget"/> for the
/// platform through its matched provider. Deploy is an array (Android/iOS) so a
/// single build can ship to multiple destinations.
/// </summary>
public sealed class DeployRunner
{
    private readonly DeployProviderRegistry _registry;

    public DeployRunner(DeployProviderRegistry registry) => _registry = registry;

    public async Task RunAsync(PlatformContext ctx)
    {
        ctx.Log.Step($"[{ctx.Platform.ToDisplayName()}] deploy");

        var targets = GetTargets(ctx);
        if (targets is null || targets.Count == 0)
        {
            ctx.Log.Info("No deploy targets for this platform.");
            return;
        }

        foreach (var target in targets)
        {
            var outcome = await RunTargetAsync(ctx, target);
            ctx.Result.Deploys.Add(outcome);

            var line = $"{outcome.Provider}: {outcome.Status}{(outcome.Detail is null ? "" : $" — {outcome.Detail}")}";
            switch (outcome.Status)
            {
                case "Succeeded": ctx.Log.Success(line); break;
                case "Skipped": ctx.Log.Warn(line); break;
                default: ctx.Log.Error(line); break;
            }
        }
    }

    private async Task<DeployOutcome> RunTargetAsync(PlatformContext ctx, DeployTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Provider))
            return new DeployOutcome { Provider = "(unknown)", Status = "Failed", Detail = "Deploy entry is missing 'Provider'." };

        if (!_registry.TryResolve(target.Provider, out var provider))
            return new DeployOutcome
            {
                Provider = target.Provider,
                Status = "Failed",
                Detail = $"No registered provider named '{target.Provider}'. Known: {string.Join(", ", _registry.ProviderNames)}.",
            };

        if (!provider.Supports(ctx.Platform))
            return new DeployOutcome
            {
                Provider = provider.Name,
                Status = "Failed",
                Detail = $"Provider '{provider.Name}' does not support {ctx.Platform.ToDisplayName()}.",
            };

        try
        {
            return await provider.DeployAsync(new DeployContext
            {
                Platform = ctx,
                Target = target,
                Variables = ctx.Run.Variables,
            });
        }
        catch (Exception ex)
        {
            return new DeployOutcome { Provider = provider.Name, Status = "Failed", Detail = ex.Message };
        }
    }

    private static List<DeployTarget>? GetTargets(PlatformContext ctx) => ctx.Platform switch
    {
        SherpaPlatform.Android => ctx.Run.Environment.Android?.Deploy,
        SherpaPlatform.IOS => ctx.Run.Environment.IOS?.Deploy,
        _ => null, // Mac/Windows flat blocks define no deploy array (spec §3.3/§3.4)
    };
}
