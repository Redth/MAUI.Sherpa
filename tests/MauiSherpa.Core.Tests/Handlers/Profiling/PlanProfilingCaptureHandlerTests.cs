using FluentAssertions;
using MauiSherpa.Core.Handlers.Profiling;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Requests.Profiling;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Profiling;

public class PlanProfilingCaptureHandlerTests
{
    private readonly Mock<IProfilingCaptureOrchestrationService> _profilingCaptureOrchestrationService = new();
    private readonly Mock<IMediatorContext> _context = new();

    [Fact]
    public async Task Handle_ReturnsPlanFromOrchestrationService()
    {
        var session = new ProfilingSessionDefinition(
            "session-1",
            "Android launch",
            new ProfilingTarget(
                ProfilingTargetPlatform.Android,
                ProfilingTargetKind.Emulator,
                "emulator-5554",
                "Pixel 8"),
            ProfilingScenarioKind.Launch,
            [ProfilingCaptureKind.Cpu],
            CreatedAt: DateTimeOffset.UtcNow);
        var request = new PlanProfilingCaptureRequest(session, new ProfilingCapturePlanOptions(ProjectPath: "/Users/test/App.csproj"));
        var expectedPlan = new ProfilingCapturePlan(
            session,
            new ProfilingPlatformCapabilities(
                ProfilingTargetPlatform.Android,
                "Android",
                [ProfilingTargetKind.PhysicalDevice, ProfilingTargetKind.Emulator],
                [ProfilingCaptureKind.Cpu],
                [ProfilingArtifactKind.Trace],
                [ProfilingScenarioKind.Launch],
                SupportsLaunchProfiling: true,
                SupportsAttachToProcess: true,
                SupportsLiveMetrics: true,
                SupportsSymbolication: false),
            request.Options!,
            "macOS",
            "net10.0-android",
            "artifacts/profiling/session-1",
            "/Users/test",
            true,
            null,
            null,
            new ProfilingPlanValidation([], []),
            [],
            [],
            [],
            new Dictionary<string, string>());

        _profilingCaptureOrchestrationService.Setup(service =>
                service.PlanCaptureAsync(session, request.Options, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPlan);

        var handler = new PlanProfilingCaptureHandler(_profilingCaptureOrchestrationService.Object);
        var result = await handler.Handle(request, _context.Object, CancellationToken.None);

        result.Should().Be(expectedPlan);
        _profilingCaptureOrchestrationService.Verify(service =>
            service.PlanCaptureAsync(session, request.Options, It.IsAny<CancellationToken>()), Times.Once);
    }
}
