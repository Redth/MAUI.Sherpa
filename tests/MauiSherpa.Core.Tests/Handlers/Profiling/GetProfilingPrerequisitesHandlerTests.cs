using FluentAssertions;
using MauiSherpa.Core.Handlers.Profiling;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Requests.Profiling;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Handlers.Profiling;

public class GetProfilingPrerequisitesHandlerTests
{
    private readonly Mock<IProfilingPrerequisitesService> _profilingPrerequisitesService = new();
    private readonly Mock<IMediatorContext> _context = new();
    private readonly GetProfilingPrerequisitesHandler _handler;

    public GetProfilingPrerequisitesHandlerTests()
    {
        _handler = new GetProfilingPrerequisitesHandler(_profilingPrerequisitesService.Object);
    }

    [Fact]
    public async Task Handle_ReturnsPrerequisiteReport()
    {
        var requestedCaptureKinds = new[] { ProfilingCaptureKind.Cpu };
        var expectedReport = new ProfilingPrerequisiteReport(
            new ProfilingPrerequisiteContext(
                ProfilingTargetPlatform.Android,
                requestedCaptureKinds,
                "/tmp",
                "/usr/local/share/dotnet/dotnet",
                new DoctorContext("/tmp", "/usr/local/share/dotnet", null, null, null, "10.0.100")),
            [
                new ProfilingPrerequisiteStatus(
                    "dotnet-trace",
                    ProfilingPrerequisiteKind.DotNetTool,
                    DependencyStatusType.Ok,
                    IsRequired: true,
                    RequiredVersion: "10.x",
                    RecommendedVersion: "10.x",
                    InstalledVersion: "10.0.41001",
                    Message: "Ready")
            ],
            DateTimeOffset.UtcNow);

        _profilingPrerequisitesService
            .Setup(x => x.GetPrerequisitesAsync(
                ProfilingTargetPlatform.Android,
                It.Is<IReadOnlyList<ProfilingCaptureKind>>(kinds => kinds.SequenceEqual(requestedCaptureKinds)),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedReport);

        var result = await _handler.Handle(
            new GetProfilingPrerequisitesRequest(ProfilingTargetPlatform.Android, requestedCaptureKinds),
            _context.Object,
            CancellationToken.None);

        result.Should().Be(expectedReport);
    }

    [Fact]
    public void GetKey_ReturnsStablePlatformAndCaptureKey()
    {
        var request = new GetProfilingPrerequisitesRequest(
            ProfilingTargetPlatform.Android,
            [ProfilingCaptureKind.Memory, ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Memory]);

        request.GetKey().Should().Be("profiling:prerequisites:Android:Cpu,Memory");
    }
}
