using FluentAssertions;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class DevFlowVersionSupportTests
{
    [Theory]
    [InlineData("0.1.0-beta.1", 0, 1, 0)]
    [InlineData("v0.1.0-preview.3+abc123", 0, 1, 0)]
    [InlineData("V0.24.0-rc.2", 0, 24, 0)]
    public void TryParseComparableVersion_StripsSemVerSuffixes(string versionText, int major, int minor, int build)
    {
        var parsed = DevFlowVersionSupport.TryParseComparableVersion(versionText, out var version);

        parsed.Should().BeTrue();
        version.Should().Be(new Version(major, minor, build));
    }

    [Fact]
    public void TryParseComparableVersion_AllowsPrereleaseAtMinimumFloor()
    {
        var parsed = DevFlowVersionSupport.TryParseComparableVersion("v0.1.0-preview.1", out var version);

        parsed.Should().BeTrue();
        version.Should().Be(DevFlowVersionSupport.MinimumSupportedVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void TryParseComparableVersion_RejectsInvalidInput(string? versionText)
    {
        var parsed = DevFlowVersionSupport.TryParseComparableVersion(versionText, out _);

        parsed.Should().BeFalse();
    }

    [Theory]
    [InlineData("0.1.0-beta.1+abc123", "v0.1.0-beta.1")]
    [InlineData("v0.24.0", "v0.24.0")]
    public void FormatVersionLabel_HidesBuildMetadataButKeepsPrerelease(string versionText, string expected)
    {
        DevFlowVersionSupport.FormatVersionLabel(versionText).Should().Be(expected);
    }
}
