using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class MacElevatedProcessScriptBuilderTests
{
    [Fact]
    public void Build_QuotesCommandAndPreservesExecutionContext()
    {
        var request = new ProcessRequest(
            Command: "/Users/test/Library/Application Support/dotnet/dotnet",
            Arguments: ["workload", "update", "--version", "10.0.300.1"],
            WorkingDirectory: "/tmp/context with spaces",
            Environment: new Dictionary<string, string>
            {
                ["DOTNET_ROOT"] = "/Users/test/Library/Application Support/dotnet",
                ["DOTNET_MULTILEVEL_LOOKUP"] = "0"
            },
            UsePseudoTerminal: true);

        var script = MacElevatedProcessScriptBuilder.Build(
            request,
            "/tmp/output with spaces.log");

        script.Should().Contain("cd '/tmp/context with spaces' || exit 1");
        script.Should().Contain(
            "export DOTNET_ROOT='/Users/test/Library/Application Support/dotnet'");
        script.Should().Contain("export DOTNET_MULTILEVEL_LOOKUP='0'");
        script.Should().Contain(
            "/usr/bin/script -q /dev/null '/Users/test/Library/Application Support/dotnet/dotnet' 'workload' 'update' '--version' '10.0.300.1'");
        script.Should().Contain("tee -a '/tmp/output with spaces.log'");
    }

    [Fact]
    public void Build_ShellQuotesArguments()
    {
        var request = new ProcessRequest(
            "/usr/bin/printf",
            ["%s", "it's safe; echo not-run"]);

        var script = MacElevatedProcessScriptBuilder.Build(request, "/tmp/output");

        script.Should().Contain("'/usr/bin/printf' '%s' 'it'\\''s safe; echo not-run'");
    }

    [Fact]
    public void Build_RejectsInvalidEnvironmentVariableNames()
    {
        var request = new ProcessRequest(
            "/usr/bin/true",
            [],
            Environment: new Dictionary<string, string> { ["BAD-NAME"] = "value" });

        var action = () => MacElevatedProcessScriptBuilder.Build(request, "/tmp/output");

        action.Should().Throw<ArgumentException>()
            .WithMessage("*BAD-NAME*");
    }
}
