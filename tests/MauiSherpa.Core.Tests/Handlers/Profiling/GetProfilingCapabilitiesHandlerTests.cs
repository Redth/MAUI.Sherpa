using FluentAssertions;
using MauiSherpa.Core.Handlers.Profiling;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Requests.Profiling;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Profiling;

public class GetProfilingCapabilitiesHandlerTests
{
    private readonly Mock<IProfilingCatalogService> _profilingCatalogService;
    private readonly Mock<IMediatorContext> _context;
    private readonly GetProfilingCapabilitiesHandler _handler;

    public GetProfilingCapabilitiesHandlerTests()
    {
        _profilingCatalogService = new Mock<IProfilingCatalogService>();
        _context = new Mock<IMediatorContext>();
        _handler = new GetProfilingCapabilitiesHandler(_profilingCatalogService.Object);
    }

    [Fact]
    public async Task Handle_ReturnsCapabilitiesForRequestedPlatform()
    {
        var expectedCapabilities = new ProfilingPlatformCapabilities(
            ProfilingTargetPlatform.iOS,
            "iOS Simulator",
            [ProfilingTargetKind.Simulator],
            [ProfilingCaptureKind.Startup, ProfilingCaptureKind.Interaction],
            [ProfilingArtifactKind.Trace, ProfilingArtifactKind.Mibc],
            [ProfilingScenarioKind.Launch, ProfilingScenarioKind.Interaction],
            SupportsLaunchProfiling: true,
            SupportsAttachToProcess: false,
            SupportsLiveMetrics: false,
            SupportsSymbolication: true);

        _profilingCatalogService.Setup(x => x.GetCapabilitiesAsync(ProfilingTargetPlatform.iOS, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCapabilities);

        var result = await _handler.Handle(
            new GetProfilingCapabilitiesRequest(ProfilingTargetPlatform.iOS),
            _context.Object,
            CancellationToken.None);

        result.Should().Be(expectedCapabilities);
        _profilingCatalogService.Verify(x => x.GetCapabilitiesAsync(ProfilingTargetPlatform.iOS, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void GetKey_ReturnsPlatformSpecificKey()
    {
        var request = new GetProfilingCapabilitiesRequest(ProfilingTargetPlatform.Windows);

        request.GetKey().Should().Be("profiling:capabilities:Windows");
    }
}
