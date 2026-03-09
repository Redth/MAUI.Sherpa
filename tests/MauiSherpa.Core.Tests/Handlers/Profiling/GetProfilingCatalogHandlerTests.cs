using FluentAssertions;
using MauiSherpa.Core.Handlers.Profiling;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Requests.Profiling;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Profiling;

public class GetProfilingCatalogHandlerTests
{
    private readonly Mock<IProfilingCatalogService> _profilingCatalogService;
    private readonly Mock<IMediatorContext> _context;
    private readonly GetProfilingCatalogHandler _handler;

    public GetProfilingCatalogHandlerTests()
    {
        _profilingCatalogService = new Mock<IProfilingCatalogService>();
        _context = new Mock<IMediatorContext>();
        _handler = new GetProfilingCatalogHandler(_profilingCatalogService.Object);
    }

    [Fact]
    public async Task Handle_ReturnsCatalogFromService()
    {
        var expectedCatalog = new ProfilingCatalog(
            [new ProfilingPlatformCapabilities(
                ProfilingTargetPlatform.Android,
                "Android",
                [ProfilingTargetKind.PhysicalDevice, ProfilingTargetKind.Emulator],
                [ProfilingCaptureKind.Cpu],
                [ProfilingArtifactKind.Trace],
                [ProfilingScenarioKind.Launch],
                SupportsLaunchProfiling: true,
                SupportsAttachToProcess: true,
                SupportsLiveMetrics: true,
                SupportsSymbolication: false)],
            [new ProfilingScenarioDefinition(
                ProfilingScenarioKind.Launch,
                "Launch & startup",
                "desc",
                [ProfilingCaptureKind.Cpu],
                TimeSpan.FromMinutes(1))]);

        _profilingCatalogService.Setup(x => x.GetCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedCatalog);

        var result = await _handler.Handle(new GetProfilingCatalogRequest(), _context.Object, CancellationToken.None);

        result.Should().Be(expectedCatalog);
        _profilingCatalogService.Verify(x => x.GetCatalogAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void GetKey_ReturnsStableCatalogKey()
    {
        var request = new GetProfilingCatalogRequest();

        request.GetKey().Should().Be("profiling:catalog");
    }
}
