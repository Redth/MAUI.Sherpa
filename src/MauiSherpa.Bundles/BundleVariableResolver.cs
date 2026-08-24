using System.Text.RegularExpressions;

namespace MauiSherpa.Bundles;

public sealed record ResolvedBundleVariables(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlySet<string> SecretNames,
    IReadOnlySet<string> SecretValues);

public static partial class BundleVariableResolver
{
    public static ResolvedBundleVariables Resolve(
        SherpaBundle bundle,
        string environmentName,
        BundlePlatform platform,
        BundlePhase phase,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (!bundle.Environments.TryGetValue(environmentName, out var environment))
            throw new BundleValidationException([$"Environment '{environmentName}' was not found."]);
        if (!environment.Platforms.TryGetValue(platform, out var configuration))
            throw new BundleValidationException([$"Platform '{platform}' is not configured for environment '{environmentName}'."]);

        var merged = new Dictionary<string, string>(bundle.Variables, StringComparer.OrdinalIgnoreCase);
        Merge(merged, environment.Variables);
        Merge(merged, configuration.Variables);
        Merge(merged, phase switch
        {
            BundlePhase.Install => configuration.Install.Variables,
            BundlePhase.Build => configuration.Build.Variables,
            BundlePhase.Deploy => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        });
        if (overrides is not null)
            Merge(merged, overrides);

        merged["SherpaEnvironment"] = environmentName;
        merged["SherpaPlatform"] = platform.ToString();

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in merged.Keys)
            resolved[key] = ResolveValue(key, merged, resolved, new Stack<string>());

        var secretNames = bundle.SecretVariables
            .Where(resolved.ContainsKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var secretValues = secretNames
            .Select(name => resolved[name])
            .Where(value => !string.IsNullOrEmpty(value))
            .ToHashSet(StringComparer.Ordinal);
        return new ResolvedBundleVariables(resolved, secretNames, secretValues);
    }

    public static string Expand(string input, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(values);

        return VariableTokenRegex().Replace(input, match =>
        {
            var name = match.Groups["dollar"].Success
                ? match.Groups["dollar"].Value
                : match.Groups["mustache"].Value;
            if (!values.TryGetValue(name, out var value))
                throw new BundleValidationException([$"Variable '{name}' is not defined."]);
            return value;
        });
    }

    private static string ResolveValue(
        string key,
        IReadOnlyDictionary<string, string> source,
        IDictionary<string, string> resolved,
        Stack<string> resolving)
    {
        if (resolved.TryGetValue(key, out var existing))
            return existing;
        if (resolving.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            var cycle = resolving.Reverse().Append(key);
            throw new BundleValidationException([$"Variable cycle detected: {string.Join(" -> ", cycle)}."]);
        }

        resolving.Push(key);
        try
        {
            return VariableTokenRegex().Replace(source[key], match =>
            {
                var dependency = match.Groups["dollar"].Success
                    ? match.Groups["dollar"].Value
                    : match.Groups["mustache"].Value;
                if (!source.ContainsKey(dependency))
                    throw new BundleValidationException([$"Variable '{dependency}' referenced by '{key}' is not defined."]);
                return ResolveValue(dependency, source, resolved, resolving);
            });
        }
        finally
        {
            resolving.Pop();
        }
    }

    private static void Merge(
        IDictionary<string, string> destination,
        IReadOnlyDictionary<string, string> source)
    {
        foreach (var (key, value) in source)
            destination[key] = value;
    }

    [GeneratedRegex(@"\$\{(?<dollar>[A-Za-z_][A-Za-z0-9_.-]*)\}|\{\{\s*(?<mustache>[A-Za-z_][A-Za-z0-9_.-]*)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariableTokenRegex();
}
