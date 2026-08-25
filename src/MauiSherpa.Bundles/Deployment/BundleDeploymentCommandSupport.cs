using System.Text.Json;
using System.Text.RegularExpressions;

namespace MauiSherpa.Bundles;

internal static partial class BundleDeploymentCommandSupport
{
    public static Dictionary<string, string> MergeVariables(BundleDeploymentContext context)
    {
        var merged = new Dictionary<string, string>(context.Variables, StringComparer.OrdinalIgnoreCase);
        if (context.Target.Variables.Count == 0)
            return merged;

        var source = new Dictionary<string, string>(merged, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in context.Target.Variables)
            source[key] = value;

        var resolvedTargetVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in context.Target.Variables.Keys)
            merged[key] = ResolveTargetVariable(key, source, context.Target.Variables, resolvedTargetVariables, new Stack<string>());

        return merged;
    }

    public static string? GetScalar(
        BundleDeploymentTarget target,
        IReadOnlyDictionary<string, string> variables,
        string name)
    {
        if (target.Settings.TryGetValue(name, out var setting))
        {
            var value = GetScalar(setting, variables);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return variables.TryGetValue(name, out var variable) && !string.IsNullOrWhiteSpace(variable)
            ? ExpandIfPossible(variable, variables)
            : null;
    }

    public static IReadOnlyList<string> GetList(
        BundleDeploymentTarget target,
        IReadOnlyDictionary<string, string> variables,
        string name)
    {
        if (target.Settings.TryGetValue(name, out var setting))
            return GetList(setting, variables);

        return variables.TryGetValue(name, out var variable)
            ? SplitList(ExpandIfPossible(variable, variables))
            : [];
    }

    public static void ValidatePlatform(
        List<string> errors,
        BundleDeploymentProvider provider,
        BundlePlatform actual,
        params BundlePlatform[] expected)
    {
        if (!expected.Contains(actual))
            errors.Add($"Provider '{provider}' does not support platform '{actual}'.");
    }

    public static void ValidateArtifact(
        List<string> errors,
        BundleDeploymentProvider provider,
        BundleArtifact artifact,
        params string[] extensions)
    {
        var normalizedExtensions = extensions
            .Select(extension => extension.TrimStart('.'))
            .ToArray();
        var extension = Path.GetExtension(artifact.Path).TrimStart('.');
        var kind = artifact.Kind.TrimStart('.');

        if (!normalizedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(
                $"Provider '{provider}' requires {DescribeArtifactExtensions(normalizedExtensions)} artifacts, " +
                $"but '{Path.GetFileName(artifact.Path)}' has extension '{extension}'.");
        }

        if (!string.IsNullOrWhiteSpace(kind) &&
            !normalizedExtensions.Contains(kind, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(
                $"Provider '{provider}' requires artifact kind {DescribeArtifactExtensions(normalizedExtensions)}, " +
                $"but found '{artifact.Kind}'.");
        }
    }

    public static void RequireValue(List<string> errors, string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"'{name}' is required.");
    }

    public static void RequireAny(List<string> errors, IReadOnlyList<string> values, params string[] names)
    {
        if (values.Count == 0)
            errors.Add($"At least one of '{string.Join("' or '", names)}' is required.");
    }

    public static void ValidatePath(
        List<string> errors,
        string? path,
        string name,
        bool requireExists,
        params string[] expectedExtensions)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            errors.Add($"'{name}' is required.");
            return;
        }

        var extension = Path.GetExtension(path).TrimStart('.');
        if (expectedExtensions.Length > 0 &&
            !expectedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"'{name}' must point to {DescribeArtifactExtensions(expectedExtensions)}.");
        }

        if (requireExists && !File.Exists(path))
            errors.Add($"'{name}' file '{path}' was not found.");
    }

    public static void ValidateFileName(
        List<string> errors,
        string? path,
        string expectedFileName,
        string name)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!string.Equals(Path.GetFileName(path), expectedFileName, StringComparison.Ordinal))
        {
            errors.Add($"'{name}' must be materialized as '{expectedFileName}'.");
        }
    }

    public static async Task<BundleDeploymentResult> RunProcessAsync(
        BundleDeploymentProvider provider,
        IBundleProcessRunner processRunner,
        BundleDeploymentContext context,
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (context.DryRun)
        {
            return new BundleDeploymentResult
            {
                Provider = provider,
                Succeeded = true,
                Message = $"{provider} deployment validated."
            };
        }

        var result = await processRunner.RunAsync(
            new BundleProcessRequest
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = context.WorkingDirectory,
                Environment = environment ?? EmptyEnvironment,
                SecretValues = context.SecretValues
            },
            progress,
            cancellationToken).ConfigureAwait(false);

        var output = string.Join(
            Environment.NewLine,
            new[] { result.StandardOutput, result.StandardError }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new BundleDeploymentResult
        {
            Provider = provider,
            Succeeded = result.ExitCode == 0,
            ReleaseId = ExtractReleaseId(output),
            Url = ExtractUrl(output),
            Message = result.ExitCode == 0
                ? Summarize(output, $"{provider} deployment completed.")
                : Summarize(output, $"{provider} exited with code {result.ExitCode}.")
        };
    }

    private static string? GetScalar(JsonElement element, IReadOnlyDictionary<string, string> variables) => element.ValueKind switch
    {
        JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString())
            ? null
            : ExpandIfPossible(element.GetString()!, variables),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
        _ => null
    };

    private static IReadOnlyList<string> GetList(JsonElement element, IReadOnlyDictionary<string, string> variables) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray()
            .Select(item => GetScalar(item, variables))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray(),
        JsonValueKind.String => SplitList(ExpandIfPossible(element.GetString(), variables)),
        _ => []
    };

    private static string ExpandIfPossible(string? value, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value ?? string.Empty;
        return BundleVariableResolver.Expand(value, variables);
    }

    private static string ResolveTargetVariable(
        string key,
        IReadOnlyDictionary<string, string> source,
        IReadOnlyDictionary<string, string> targetVariables,
        IDictionary<string, string> resolved,
        Stack<string> resolving)
    {
        if (resolved.TryGetValue(key, out var existing))
            return existing;
        if (!source.TryGetValue(key, out var value))
            throw new BundleValidationException([$"Deployment variable '{key}' is not defined."]);
        if (!targetVariables.ContainsKey(key))
            return value;
        if (resolving.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            var cycle = resolving.Reverse().Append(key);
            throw new BundleValidationException(
                [$"Deployment variable cycle detected: {string.Join(" -> ", cycle)}."]);
        }

        resolving.Push(key);
        try
        {
            var expanded = VariableReferenceRegex().Replace(value, match =>
            {
                var dependency = match.Groups["dollar"].Success
                    ? match.Groups["dollar"].Value
                    : match.Groups["mustache"].Value;
                return ResolveTargetVariable(dependency, source, targetVariables, resolved, resolving);
            });
            resolved[key] = expanded;
            return expanded;
        }
        finally
        {
            resolving.Pop();
        }
    }

    private static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string DescribeArtifactExtensions(IEnumerable<string> extensions) =>
        string.Join(" or ", extensions.Select(extension => $"'.{extension.TrimStart('.')}'"));

    private static string Summarize(string output, string fallback) =>
        output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? fallback;

    private static string? ExtractReleaseId(string output)
    {
        var jsonValue = TryExtractJsonProperty(output, "releaseId", "submissionId", "editId", "id");
        if (!string.IsNullOrWhiteSpace(jsonValue))
            return jsonValue;

        var match = IdentifierRegex().Match(output);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string? ExtractUrl(string output)
    {
        var jsonUrl = TryExtractJsonProperty(output, "url", "uri", "consoleUrl", "releaseUrl");
        if (!string.IsNullOrWhiteSpace(jsonUrl))
            return jsonUrl;

        var match = UrlRegex().Match(output);
        return match.Success
            ? match.Value.TrimEnd('.', ',', ';', ')', ']')
            : null;
    }

    private static string? TryExtractJsonProperty(string text, params string[] propertyNames)
    {
        foreach (var candidate in EnumerateJsonCandidates(text))
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var propertyName in propertyNames)
                {
                    if (document.RootElement.TryGetProperty(propertyName, out var property))
                    {
                        var value = property.ValueKind == JsonValueKind.String
                            ? property.GetString()
                            : property.ToString();
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateJsonCandidates(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        yield return text;
        foreach (var line in text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('{') && line.EndsWith('}'))
                yield return line;
        }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyEnvironment =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"https?://\S+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(?:release(?:\s+id)?|submission(?:\s+id)?|edit(?:\s+id)?|id)\s*[:=]\s*[""']?(?<value>[A-Za-z0-9._/-]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"\$\{(?<dollar>[A-Za-z_][A-Za-z0-9_.-]*)\}|\{\{\s*(?<mustache>[A-Za-z_][A-Za-z0-9_.-]*)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariableReferenceRegex();
}
