using FluentAssertions;
using MauiSherpa.Core.Handlers.Profiling;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Requests.Profiling;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Profiling;

public class AnalyzeProfilingArtifactHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsAnalysisFromService()
    {
        var service = new Mock<IProfilingArtifactAnalysisService>();
        var context = new Mock<IMediatorContext>();
        var expected = new ProfilingArtifactAnalysisResult(
            new ProfilingArtifactAnalysis(
                new ProfilingArtifactMetadata(
                    "artifact-1",
                    "session-1",
                    ProfilingArtifactKind.Trace,
                    "Trace capture",
                    "trace.json",
                    null,
                    "application/json",
                    DateTimeOffset.UtcNow),
                "/tmp/trace.json",
                true,
                ProfilingAnalysisKind.Speedscope,
                "summary",
                [],
                [],
                [],
                []));

        service.Setup(x => x.AnalyzeArtifactAsync("artifact-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var handler = new AnalyzeProfilingArtifactHandler(service.Object);

        var result = await handler.Handle(new AnalyzeProfilingArtifactRequest("artifact-1"), context.Object, CancellationToken.None);

        result.Should().Be(expected);
        service.Verify(x => x.AnalyzeArtifactAsync("artifact-1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
