using FluentAssertions;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class ProfilingArtifactClassifierTests
{
    [Theory]
    [InlineData("capture.nettrace", ProfilingArtifactKind.Trace)]
    [InlineData("capture.speedscope.json", ProfilingArtifactKind.Trace)]
    [InlineData("capture.mibc", ProfilingArtifactKind.Mibc)]
    [InlineData("memory.gcdump", ProfilingArtifactKind.GcDump)]
    [InlineData("capture.log", ProfilingArtifactKind.Log)]
    [InlineData("capture.txt", ProfilingArtifactKind.Log)]
    [InlineData("capture.bin", ProfilingArtifactKind.Other)]
    public void Classify_ReturnsExpectedKind(string path, ProfilingArtifactKind expected)
    {
        ProfilingArtifactClassifier.Classify(path).Should().Be(expected);
    }

    [Fact]
    public void GetBaseName_RemovesFullSpeedscopeSuffix()
    {
        ProfilingArtifactClassifier.GetBaseName("my-profile.speedscope.json")
            .Should().Be("my-profile");
    }
}
