using System.Text;
using System.Text.RegularExpressions;
using MauiSherpa.Bundle.Loading;

namespace MauiSherpa.Bundle.Substitution;

/// <summary>
/// Resolves <c>${name}</c> references inside bundle strings (spec §5.1).
/// Sources, highest precedence first: CLI <c>-variable</c>, environment-level
/// <c>Variables</c>, top-level <c>Build.Variables</c>. Referencing an undefined
/// variable is a hard error.
/// </summary>
public sealed partial class VariableResolver
{
    private readonly IReadOnlyDictionary<string, string> _values;

    private VariableResolver(IReadOnlyDictionary<string, string> values) => _values = values;

    /// <summary>
    /// Builds a resolver by layering variable sources. Pass layers
    /// lowest-precedence first; later layers win.
    /// </summary>
    public static VariableResolver Build(params IReadOnlyDictionary<string, string>?[] layersLowestFirst)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var layer in layersLowestFirst)
        {
            if (layer is null)
                continue;
            foreach (var (key, value) in layer)
                merged[key] = value;
        }
        return new VariableResolver(merged);
    }

    [GeneratedRegex(@"\$\{(?<name>[^}]+)\}", RegexOptions.Compiled)]
    private static partial Regex VariablePattern();

    /// <summary>Whether <paramref name="name"/> is defined.</summary>
    public bool Contains(string name) => _values.ContainsKey(name);

    /// <summary>
    /// Substitutes every <c>${name}</c> in <paramref name="input"/>. Throws a
    /// <see cref="SherpaBundleException"/> listing all undefined variables.
    /// </summary>
    public string Resolve(string? input)
    {
        if (string.IsNullOrEmpty(input) || !input.Contains("${", StringComparison.Ordinal))
            return input ?? "";

        List<string>? missing = null;
        var result = VariablePattern().Replace(input, match =>
        {
            var name = match.Groups["name"].Value.Trim();
            if (_values.TryGetValue(name, out var value))
                return value;

            (missing ??= new List<string>()).Add(name);
            return match.Value;
        });

        if (missing is { Count: > 0 })
        {
            var names = string.Join(", ", missing.Distinct());
            throw new SherpaBundleException(
                $"Undefined variable(s) referenced: {names}. " +
                "Define them in a Variables block or pass -variable:\"name=value\".");
        }

        return result;
    }

    /// <summary>Resolves every value in a dictionary (keys untouched).</summary>
    public Dictionary<string, string> ResolveValues(
        IReadOnlyDictionary<string, string> source,
        IEqualityComparer<string>? keyComparer = null)
    {
        var result = new Dictionary<string, string>(keyComparer ?? StringComparer.Ordinal);
        foreach (var (key, value) in source)
            result[key] = Resolve(value);
        return result;
    }
}
