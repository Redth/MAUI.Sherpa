using FluentAssertions;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class MauiProfileCommandBuilderTests
{
    [Fact]
    public void BuildArguments_StartupUsesHelperEventByDefault()
    {
        var arguments = MauiProfileCommandBuilder.BuildArguments(CreateRequest());

        arguments.Should().ContainInOrder(
            "profile",
            "startup",
            "--stopping-event-provider-name",
            MauiProfileCommandBuilder.StartupProviderName,
            "--stopping-event-event-name",
            MauiProfileCommandBuilder.StartupEventName,
            "--json",
            "--ci");
    }

    [Fact]
    public void BuildArguments_DurationReplacesHelperEvent()
    {
        var request = CreateRequest() with { Duration = TimeSpan.FromSeconds(15) };

        var arguments = MauiProfileCommandBuilder.BuildArguments(request);

        arguments.Should().ContainInOrder("--duration", "00:00:15");
        arguments.Should().NotContain("--stopping-event-provider-name");
    }

    [Fact]
    public void BuildArguments_InteractionIncludesAdvancedOptions()
    {
        var request = CreateRequest() with
        {
            Mode = MauiProfileMode.Interaction,
            Format = MauiProfileOutputFormat.Mibc,
            NoBuild = true,
            TraceProfile = "cpu-sampling,gc-verbose"
        };

        var arguments = MauiProfileCommandBuilder.BuildArguments(request);

        arguments.Should().ContainInOrder("profile", "manual");
        arguments.Should().ContainInOrder("--format", "mibc");
        arguments.Should().ContainInOrder("--trace-profile", "cpu-sampling,gc-verbose");
        arguments.Count(x => x == "--trace-profile").Should().Be(1);
        arguments.Should().Contain("--no-build");
        arguments.Should().NotContain("--duration");
    }

    [Fact]
    public void FormatForDisplay_QuotesPathsWithoutChangingArguments()
    {
        var request = CreateRequest() with
        {
            ProjectPath = "/repo/My App/My App.csproj",
            OutputPath = "/tmp/Profile Output/capture.nettrace"
        };

        var command = MauiProfileCommandBuilder.FormatForDisplay("/Users/me/.dotnet/tools/maui", request);

        command.Should().Contain("\"/repo/My App/My App.csproj\"");
        command.Should().Contain("\"/tmp/Profile Output/capture.nettrace\"");
    }

    private static MauiProfileRequest CreateRequest() => new()
    {
        ProjectPath = "/repo/App.csproj",
        Platform = ProfilingTargetPlatform.Android,
        DeviceId = "emulator-5554",
        Mode = MauiProfileMode.Startup,
        OutputPath = "/tmp/capture.nettrace"
    };
}
