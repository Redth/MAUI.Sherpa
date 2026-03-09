using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class ProfilingCatalogServiceTests
{
    [Fact]
    public async Task GetCatalogAsync_ReturnsBuiltInPlatformsAndScenarios()
    {
        var service = new ProfilingCatalogService([]);

        var result = await service.GetCatalogAsync();

        result.Platforms.Should().HaveCount(5);
        result.Scenarios.Should().Contain(x => x.Kind == ProfilingScenarioKind.Launch);
        result.Platforms.Should().Contain(x =>
            x.Platform == ProfilingTargetPlatform.Android &&
            x.SupportedTargetKinds.Contains(ProfilingTargetKind.Emulator));
    }

    [Fact]
    public async Task GetCapabilitiesAsync_UsesRegisteredProviderOverride()
    {
        var customCapabilities = new ProfilingPlatformCapabilities(
            ProfilingTargetPlatform.Android,
            "Android (custom)",
            [ProfilingTargetKind.PhysicalDevice],
            [ProfilingCaptureKind.Cpu],
            [ProfilingArtifactKind.Trace],
            [ProfilingScenarioKind.Launch],
            SupportsLaunchProfiling: true,
            SupportsAttachToProcess: false,
            SupportsLiveMetrics: false,
            SupportsSymbolication: false,
            Notes: "Custom override");

        var provider = new Mock<IProfilingCapabilityProvider>();
        provider.SetupGet(x => x.Platform).Returns(ProfilingTargetPlatform.Android);
        provider.Setup(x => x.GetCapabilitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(customCapabilities);

        var service = new ProfilingCatalogService([provider.Object]);

        var result = await service.GetCapabilitiesAsync(ProfilingTargetPlatform.Android);

        result.Should().Be(customCapabilities);
    }

    [Fact]
    public void CreateSessionDefinition_UsesScenarioDefaultsWhenCaptureKindsNotProvided()
    {
        var service = new ProfilingCatalogService([]);
        var target = new ProfilingTarget(
            ProfilingTargetPlatform.Android,
            ProfilingTargetKind.Emulator,
            "emulator-5554",
            "Pixel 8");

        var result = service.CreateSessionDefinition(target, ProfilingScenarioKind.Launch);

        result.Name.Should().Be("Pixel 8 - Launch & startup");
        result.CaptureKinds.Should().BeEquivalentTo(
            [ProfilingCaptureKind.Startup, ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Memory]);
        result.Duration.Should().Be(TimeSpan.FromMinutes(2));
        result.Tags.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task ValidateSessionDefinition_ReturnsErrorsForUnsupportedValues()
    {
        var service = new ProfilingCatalogService([]);
        var capabilities = await service.GetCapabilitiesAsync(ProfilingTargetPlatform.Windows);
        var definition = new ProfilingSessionDefinition(
            "session-1",
            "",
            new ProfilingTarget(
                ProfilingTargetPlatform.Android,
                ProfilingTargetKind.Emulator,
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
