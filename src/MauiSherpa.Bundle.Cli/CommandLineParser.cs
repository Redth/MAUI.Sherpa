using MauiSherpa.Bundle.Models;
using MauiSherpa.Bundle.Pipeline;

namespace MauiSherpa.Bundle.Cli;

/// <summary>
/// Parses the spec §1 command line. The flag style is MSBuild-like
/// (<c>-flag:value</c>, repeatable <c>-variable:"name=value"</c>) rather than
/// idiomatic POSIX, so it is parsed directly rather than via System.CommandLine.
/// </summary>
public static class CommandLineParser
{
    public sealed class ParseResult
    {
        public SherpaRunOptions? Options { get; init; }
        public bool ShowHelp { get; init; }
        public bool ShowVersion { get; init; }
        public string? Error { get; init; }
    }

    private static readonly HashSet<string> ValueFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "environment", "platform", "step", "project", "variable", "replacetoken", "msbuild", "password",
    };

    public static ParseResult Parse(string[] args)
    {
        string? bundlePath = null;
        string? environment = null;
        string? platformList = null;
        string? stepList = null;
        string? projectPath = null;
        string? password = null;
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var replaceTokens = new Dictionary<string, string>(StringComparer.Ordinal);
        var msbuild = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var jsonOnly = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrEmpty(arg))
                continue;

            if (arg[0] != '-')
            {
                if (bundlePath is not null)
                    return Fail($"Unexpected argument '{arg}'. The bundle path is already set to '{bundlePath}'.");
                bundlePath = arg;
                continue;
            }

            var body = arg.TrimStart('-');

            // Booleans first.
            if (Matches(body, "help", "h", "?")) return new ParseResult { ShowHelp = true };
            if (Matches(body, "version")) return new ParseResult { ShowVersion = true };
            if (Matches(body, "json", "j")) { jsonOnly = true; continue; }

            // Split "<name><sep><value>" at the first ':' or '='.
            var (name, inlineValue) = SplitFlag(body);

            if (!ValueFlags.Contains(name))
                return Fail($"Unknown option '{arg}'.");

            var value = inlineValue;
            if (value is null)
            {
                // Allow a space-separated value: "-environment production".
                if (i + 1 < args.Length && args[i + 1].Length > 0 && args[i + 1][0] != '-')
                    value = args[++i];
                else
                    return Fail($"Option '-{name}' requires a value.");
            }

            switch (name.ToLowerInvariant())
            {
                case "environment": environment = value; break;
                case "platform": platformList = value; break;
                case "step": stepList = value; break;
                case "project": projectPath = value; break;
                case "password": password = value; break;
                case "variable": if (!AddPair(variables, value, out var ve)) return Fail(ve!); break;
                case "replacetoken": if (!AddPair(replaceTokens, value, out var re)) return Fail(re!); break;
                case "msbuild": if (!AddPair(msbuild, value, out var me)) return Fail(me!); break;
            }
        }

        if (bundlePath is null)
            return Fail("Missing required <bundle> path.");
        if (string.IsNullOrWhiteSpace(environment))
            return Fail("Missing required option '-environment:<name>'.");

        IReadOnlyList<SherpaPlatform>? platforms = null;
        if (platformList is not null)
        {
            if (!TryParsePlatforms(platformList, out platforms, out var perr))
                return Fail(perr!);
        }

        if (!TryParseSteps(stepList, out var steps, out var serr))
            return Fail(serr!);

        return new ParseResult
        {
            Options = new SherpaRunOptions
            {
                BundlePath = bundlePath,
                Environment = environment!,
                Platforms = platforms,
                Steps = steps,
                ProjectPath = projectPath,
                Password = password,
                Variables = variables,
                ReplaceTokens = replaceTokens,
                MSBuildProperties = msbuild,
                JsonOnly = jsonOnly,
            },
        };
    }

    private static (string Name, string? Value) SplitFlag(string body)
    {
        var colon = body.IndexOf(':');
        var equals = body.IndexOf('=');
        int sep = (colon, equals) switch
        {
            (< 0, < 0) => -1,
            (< 0, _) => equals,
            (_, < 0) => colon,
            _ => Math.Min(colon, equals),
        };
        return sep < 0 ? (body, null) : (body[..sep], body[(sep + 1)..]);
    }

    private static bool AddPair(Dictionary<string, string> target, string pair, out string? error)
    {
        var eq = pair.IndexOf('=');
        if (eq <= 0)
        {
            error = $"Expected 'name=value' but got '{pair}'.";
            return false;
        }
        target[pair[..eq].Trim()] = pair[(eq + 1)..];
        error = null;
        return true;
    }

    private static bool TryParsePlatforms(string list, out IReadOnlyList<SherpaPlatform> platforms, out string? error)
    {
        var result = new List<SherpaPlatform>();
        foreach (var token in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!SherpaPlatformExtensions.TryParse(token, out var p))
            {
                platforms = Array.Empty<SherpaPlatform>();
                error = $"Unknown platform '{token}'. Valid: android, ios, macos, maccatalyst, windows.";
                return false;
            }
            if (!result.Contains(p))
                result.Add(p);
        }
        platforms = result;
        error = null;
        return true;
    }

    private static bool TryParseSteps(string? list, out IReadOnlyList<SherpaStep> steps, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(list))
        {
            steps = new[] { SherpaStep.Setup, SherpaStep.Build, SherpaStep.Deploy };
            return true;
        }

        var set = new HashSet<SherpaStep>();
        foreach (var token in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(token, "all", StringComparison.OrdinalIgnoreCase))
            {
                set.Add(SherpaStep.Setup);
                set.Add(SherpaStep.Build);
                set.Add(SherpaStep.Deploy);
                continue;
            }
            if (!SherpaStepExtensions.TryParse(token, out var s))
            {
                steps = Array.Empty<SherpaStep>();
                error = $"Unknown step '{token}'. Valid: setup, build, deploy, all.";
                return false;
            }
            set.Add(s);
        }

        // Always run in canonical order regardless of input order (spec §1).
        steps = set.OrderBy(s => (int)s).ToArray();
        return true;
    }

    private static bool Matches(string body, params string[] names)
        => names.Any(n => string.Equals(body, n, StringComparison.OrdinalIgnoreCase));

    private static ParseResult Fail(string error) => new() { Error = error };
}
