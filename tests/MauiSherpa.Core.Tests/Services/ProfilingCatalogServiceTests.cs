using FluentAssertions;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class ProfilingCatalogServiceTests
{
    [Fact]
    public async Task GetCatalogAsync_ReturnsBuiltInPlatformsAndScenarios()
    {
        var service = new ProfilingCatalogService();

        var result = await service.GetCatalogAsync();

        result.Platforms.Should().HaveCount(2);
        result.Scenarios.Should().HaveCount(2);
        result.Scenarios.Should().Contain(x =>
            x.DisplayName == "Startup" &&
            x.DefaultCaptureKinds.SequenceEqual(new[] { ProfilingCaptureKind.Startup }));
        result.Scenarios.Should().Contain(x =>
            x.DisplayName == "Interaction" &&
            x.DefaultCaptureKinds.SequenceEqual(new[] { ProfilingCaptureKind.Interaction }));
        result.Platforms.Should().Contain(x =>
            x.Platform == ProfilingTargetPlatform.Android &&
            x.SupportedTargetKinds.Contains(ProfilingTargetKind.Emulator));
        result.Platforms.Should().Contain(x =>
            x.Platform == ProfilingTargetPlatform.iOS &&
            x.SupportedTargetKinds.SequenceEqual(new[] { ProfilingTargetKind.Simulator }));
    }

    [Fact]
    public async Task GetCapabilitiesAsync_RejectsUnsupportedDesktopPlatforms()
    {
        var service = new ProfilingCatalogService();
        var act = () => service.GetCapabilitiesAsync(ProfilingTargetPlatform.MacCatalyst);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*Android devices/emulators and iOS simulators*");
    }

    [Fact]
    public void CreateSessionDefinition_UsesScenarioDefaultsWhenCaptureKindsNotProvided()
    {
        var service = new ProfilingCatalogService();
        var target = new ProfilingTarget(
            ProfilingTargetPlatform.Android,
            ProfilingTargetKind.Emulator,
            "emulator-5554",
            "Pixel 8");

        var result = service.CreateSessionDefinition(target, ProfilingScenarioKind.Launch);

        result.Name.Should().Be("Pixel 8 - Startup");
        result.CaptureKinds.Should().BeEquivalentTo([ProfilingCaptureKind.Startup]);
        result.Duration.Should().Be(TimeSpan.FromMinutes(2));
        result.Tags.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task ValidateSessionDefinition_ReturnsErrorsForUnsupportedValues()
    {
        var service = new ProfilingCatalogService();
        var capabilities = await service.GetCapabilitiesAsync(ProfilingTargetPlatform.Android);
        var definition = new ProfilingSessionDefinition(
            "session-1",
            "",
            new ProfilingTarget(
                ProfilingTargetPlatform.iOS,
                ProfilingTargetKind.Simulator,
                "",
                "Android emulator"),
            ProfilingScenarioKind.Launch,
            [(ProfilingCaptureKind)999],
            Duration: TimeSpan.Zero,
            CreatedAt: DateTimeOffset.UtcNow);

        var result = service.ValidateSessionDefinition(definition, capabilities);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(x => x.Contains("session name", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(x => x.Contains("identifier", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(x => x.Contains("does not match", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(x => x.Contains("not supported", StringComparison.OrdinalIgnoreCase));
        result.UnsupportedCaptureKinds.Should().Contain((ProfilingCaptureKind)999);
    }
}
