using FluentAssertions;
using MauiSherpa.Bundle.Loading;
using MauiSherpa.Bundle.Substitution;

namespace MauiSherpa.Bundle.Tests;

public class VariableResolverTests
{
    private static Dictionary<string, string> D(params (string, string)[] pairs)
        => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    [Fact]
    public void Substitutes_single_variable()
    {
        var r = VariableResolver.Build(D(("name", "World")));
        r.Resolve("Hello ${name}").Should().Be("Hello World");
    }

    [Fact]
    public void Cli_layer_wins_over_environment_and_build()
    {
        // lowest → highest: Build, environment, CLI (spec §5.1)
        var r = VariableResolver.Build(
            D(("x", "build")),
            D(("x", "env")),
            D(("x", "cli")));
        r.Resolve("${x}").Should().Be("cli");
    }

    [Fact]
    public void Undefined_variable_is_a_hard_error()
    {
        var r = VariableResolver.Build(D(("a", "1")));
        var act = () => r.Resolve("${a} ${missing}");
        act.Should().Throw<SherpaBundleException>().WithMessage("*missing*");
    }

    [Fact]
    public void Reports_all_missing_variables_at_once()
    {
        var r = VariableResolver.Build();
        var act = () => r.Resolve("${one} ${two}");
        act.Should().Throw<SherpaBundleException>()
            .WithMessage("*one*").WithMessage("*two*");
    }

    [Fact]
    public void Does_not_touch_msbuild_style_references()
    {
        var r = VariableResolver.Build();
        r.Resolve("$(MSBuildProjectDirectory)").Should().Be("$(MSBuildProjectDirectory)");
    }

    [Fact]
    public void Resolves_dictionary_values_only()
    {
        var r = VariableResolver.Build(D(("n", "1234")));
        var result = r.ResolveValues(D(("Version", "1.0.${n}")));
        result["Version"].Should().Be("1.0.1234");
    }
}
