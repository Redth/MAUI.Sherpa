using FluentAssertions;
using MauiSherpa.Bundle.Cli;
using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Tests;

public class CommandLineParserTests
{
    [Fact]
    public void Parses_minimal_valid_invocation()
    {
        var r = CommandLineParser.Parse(new[] { "app.sherpabundle", "-environment:production" });

        r.Error.Should().BeNull();
        r.Options!.BundlePath.Should().Be("app.sherpabundle");
        r.Options.Environment.Should().Be("production");
        r.Options.Steps.Should().Equal(SherpaStep.Setup, SherpaStep.Build, SherpaStep.Deploy);
        r.Options.Platforms.Should().BeNull(); // default = all defined
    }

    [Fact]
    public void Missing_bundle_is_an_error()
        => CommandLineParser.Parse(new[] { "-environment:prod" }).Error.Should().NotBeNull();

    [Fact]
    public void Missing_environment_is_an_error()
        => CommandLineParser.Parse(new[] { "app.sherpabundle" }).Error.Should().Contain("environment");

    [Fact]
    public void Parses_platform_list_and_dedupes()
    {
        var r = CommandLineParser.Parse(new[] { "b", "-environment:p", "-platform:android,ios,android" });
        r.Options!.Platforms.Should().Equal(SherpaPlatform.Android, SherpaPlatform.IOS);
    }

    [Fact]
    public void Steps_are_ordered_canonically_regardless_of_input()
    {
        var r = CommandLineParser.Parse(new[] { "b", "-environment:p", "-step:deploy,setup" });
        r.Options!.Steps.Should().Equal(SherpaStep.Setup, SherpaStep.Deploy);
    }

    [Fact]
    public void Step_all_expands_to_all_three()
    {
        var r = CommandLineParser.Parse(new[] { "b", "-environment:p", "-step:all" });
        r.Options!.Steps.Should().Equal(SherpaStep.Setup, SherpaStep.Build, SherpaStep.Deploy);
    }

    [Fact]
    public void Repeatable_variable_replacetoken_msbuild()
    {
        var r = CommandLineParser.Parse(new[]
        {
            "b", "-environment:p",
            "-variable:buildNumber=1234",
            "-replacetoken:Hello=Android World 2",
            "-msbuild:ApplicationVersion=1.0.0",
        });

        r.Options!.Variables["buildNumber"].Should().Be("1234");
        r.Options.ReplaceTokens["Hello"].Should().Be("Android World 2"); // value keeps spaces
        r.Options.MSBuildProperties["ApplicationVersion"].Should().Be("1.0.0");
    }

    [Fact]
    public void Value_may_contain_colons_and_equals()
    {
        var r = CommandLineParser.Parse(new[] { "b", "-environment:p", "-replacetoken:Url=https://x.com?a=b" });
        r.Options!.ReplaceTokens["Url"].Should().Be("https://x.com?a=b");
    }

    [Fact]
    public void Unknown_platform_and_step_error()
    {
        CommandLineParser.Parse(new[] { "b", "-environment:p", "-platform:blackberry" }).Error.Should().Contain("platform");
        CommandLineParser.Parse(new[] { "b", "-environment:p", "-step:teleport" }).Error.Should().Contain("step");
    }

    [Fact]
    public void Json_help_version_flags()
    {
        CommandLineParser.Parse(new[] { "b", "-environment:p", "--json" }).Options!.JsonOnly.Should().BeTrue();
        CommandLineParser.Parse(new[] { "--help" }).ShowHelp.Should().BeTrue();
        CommandLineParser.Parse(new[] { "--version" }).ShowVersion.Should().BeTrue();
    }

    [Fact]
    public void Unknown_option_errors()
        => CommandLineParser.Parse(new[] { "b", "-environment:p", "-bogus:1" }).Error.Should().Contain("Unknown option");
}
