using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Substitution;

/// <summary>
/// Implements the merge + substitution model (spec §5): builds the variable
/// resolver from its three layers, then merges and substitutes the
/// per-platform <c>ReplaceTokens</c> and <c>MSBuildProperties</c>.
/// </summary>
public static class ConfigResolver
{
    /// <summary>
    /// Builds the variable resolver (spec §5.1). Precedence, low → high:
    /// <c>Build.Variables</c>, environment <c>Variables</c>, CLI variables.
    /// </summary>
    public static VariableResolver BuildVariableResolver(
        SherpaBundle bundle,
        EnvironmentBlock environment,
        IReadOnlyDictionary<string, string>? cliVariables)
        => VariableResolver.Build(
            bundle.Build?.Variables,
            environment.Variables,
            cliVariables);

    /// <summary>
    /// Computes the effective config for a platform (spec §5.2/§5.3). Layers are
    /// merged low → high, then variable substitution is applied to all values.
    /// </summary>
    public static EffectiveConfig ResolveForPlatform(
        SherpaBundle bundle,
        EnvironmentBlock environment,
        SherpaPlatform platform,
        VariableResolver vars,
        IReadOnlyDictionary<string, string>? cliReplaceTokens,
        IReadOnlyDictionary<string, string>? cliMSBuildProperties)
    {
        var tokenLayers = new List<IReadOnlyDictionary<string, string>?>
        {
            bundle.Build?.ReplaceTokens,
            environment.ReplaceTokens,
        };
        var msbuildLayers = new List<IReadOnlyDictionary<string, string>?>
        {
            bundle.Build?.MSBuildProperties,
            environment.MSBuildProperties,
        };

        switch (platform)
        {
            case SherpaPlatform.Android:
                tokenLayers.Add(environment.Android?.Build?.ReplaceTokens);
                msbuildLayers.Add(environment.Android?.Build?.MSBuildProperties);
                break;
            case SherpaPlatform.IOS:
                tokenLayers.Add(environment.IOS?.Build?.ReplaceTokens);
                msbuildLayers.Add(environment.IOS?.Build?.MSBuildProperties);
                break;
            case SherpaPlatform.MacOS:
                // Flat layout (spec §3.3): platform-level Variables act as
                // platform-scoped replace tokens; MSBuild props come from env level.
                tokenLayers.Add(environment.MacOS?.Variables);
                break;
            case SherpaPlatform.MacCatalyst:
                tokenLayers.Add(environment.MacCatalyst?.Variables);
                break;
            case SherpaPlatform.Windows:
                tokenLayers.Add(environment.Windows?.Variables);
                break;
        }

        // CLI overrides win (spec §5.2/§5.3, highest precedence).
        tokenLayers.Add(cliReplaceTokens);
        msbuildLayers.Add(cliMSBuildProperties);

        var tokens = Merge(tokenLayers, StringComparer.Ordinal);
        var msbuild = Merge(msbuildLayers, StringComparer.OrdinalIgnoreCase);

        return new EffectiveConfig
        {
            ReplaceTokens = vars.ResolveValues(tokens, StringComparer.Ordinal),
            MSBuildProperties = vars.ResolveValues(msbuild, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static Dictionary<string, string> Merge(
        IEnumerable<IReadOnlyDictionary<string, string>?> layersLowestFirst,
        IEqualityComparer<string> keyComparer)
    {
        var result = new Dictionary<string, string>(keyComparer);
        foreach (var layer in layersLowestFirst)
        {
            if (layer is null)
                continue;
            foreach (var (key, value) in layer)
                result[key] = value;
        }
        return result;
    }
}
