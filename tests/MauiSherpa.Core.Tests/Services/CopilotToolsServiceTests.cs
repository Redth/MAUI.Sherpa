using FluentAssertions;
using Moq;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class CopilotToolsServiceTests
{
    [Fact]
    public void Constructor_RegistersProfilingToolsAsReadOnly()
    {
        var sut = new CopilotToolsService(
            new Mock<IAppleConnectService>().Object,
            new Mock<IAppleIdentityStateService>().Object,
            new Mock<IAppleIdentityService>().Object,
            new Mock<IAndroidSdkService>().Object,
            new Mock<IProfilingCatalogService>().Object,
            new Mock<IProfilingArtifactLibraryService>().Object,
            new Mock<IProfilingArtifactAnalysisService>().Object,
            new Mock<IProfilingContextService>().Object,
            new Mock<ILoggingService>().Object);

        sut.GetTool("get_profiling_catalog").Should().NotBeNull();
        sut.GetTool("list_profiling_targets").Should().NotBeNull();
        sut.GetTool("list_profiling_artifacts").Should().NotBeNull();
        sut.GetTool("get_profiling_snapshot").Should().NotBeNull();
        sut.GetTool("analyze_profiling_artifact").Should().NotBeNull();
        sut.ReadOnlyToolNames.Should().Contain(new[] { "get_profiling_catalog", "list_profiling_targets", "list_profiling_artifacts", "get_profiling_snapshot", "analyze_profiling_artifact" });
    }
}
