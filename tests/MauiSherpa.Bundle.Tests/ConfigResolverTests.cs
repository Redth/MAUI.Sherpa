using FluentAssertions;
using MauiSherpa.Bundle.Loading;
using MauiSherpa.Bundle.Models;
using MauiSherpa.Bundle.Substitution;

namespace MauiSherpa.Bundle.Tests;

public class ConfigResolverTests
{
    private static Dictionary<string, string> D(params (string, string)[] pairs)
        => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    /// <summary>The worked example from spec §5.4.</summary>
    [Fact]
    public void WorkedExample_5_4_resolves_android_replace_tokens()
    {
        const string json = """
        {
          "Build": { "ReplaceTokens": { "SentryDsn": "https://default" } },
          "Production": {
            "ReplaceTokens": { "Hello": "World", "SentryDsn": "https://prod" },
            "Android": { "Build": { "ReplaceTokens": { "Hello": "Android World ${buildNumber}" } } }
          }
        }
        """;
        var bundle = SherpaBundleSerializer.Deserialize(json);
        bundle.TryGetEnvironment("production", out _, out var env);

        var cliVars = D(("buildNumber", "1234"));
        var cliTokens = D(("Hello", "Android World 2"));   // CLI -replacetoken

        var vars = ConfigResolver.BuildVariableResolver(bundle, env, cliVars);
        var cfg = ConfigResolver.ResolveForPlatform(
            bundle, env, SherpaPlatform.Android, vars, cliTokens, cliMSBuildProperties: null);

        cfg.ReplaceTokens["SentryDsn"].Should().Be("https://prod");   // from Production
        cfg.ReplaceTokens["Hello"].Should().Be("Android World 2");    // CLI wins over "Android World 1234"
    }

    [Fact]
    public void MSBuild_properties_merge_low_to_high_and_substitute_variables()
    {
        const string json = """
        {
          "Build": { "MSBuildProperties": { "Shared": "base" } },
          "Production": {
            "MSBuildProperties": { "Shared": "env" },
            "Android": { "Build": { "MSBuildProperties": { "ApplicationVersion": "1.0.${buildNumber}" } } }
          }
        }
        """;
        var bundle = SherpaBundleSerializer.Deserialize(json);
        bundle.TryGetEnvironment("Production", out _, out var env);

        var vars = ConfigResolver.BuildVariableResolver(bundle, env, D(("buildNumber", "99")));
        var cfg = ConfigResolver.ResolveForPlatform(
            bundle, env, SherpaPlatform.Android, vars,
            cliReplaceTokens: null,
            cliMSBuildProperties: D(("applicationversion", "2.0.0")));   // CLI override, different case

        cfg.MSBuildProperties["Shared"].Should().Be("env");
        // CLI override wins and matches case-insensitively (spec §5.4).
        cfg.MSBuildProperties["ApplicationVersion"].Should().Be("2.0.0");
    }

    [Fact]
    public void Flat_platform_variables_act_as_replace_tokens()
    {
        const string json = """
        {
          "Production": { "Windows": { "Variables": { "Hello": "Windows World" } } }
        }
        """;
        var bundle = SherpaBundleSerializer.Deserialize(json);
        bundle.TryGetEnvironment("Production", out _, out var env);

        var vars = ConfigResolver.BuildVariableResolver(bundle, env, null);
        var cfg = ConfigResolver.ResolveForPlatform(
            bundle, env, SherpaPlatform.Windows, vars, null, null);

        cfg.ReplaceTokens["Hello"].Should().Be("Windows World");
    }
}
