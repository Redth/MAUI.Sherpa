using FluentAssertions;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Tests.Models;

public class SdkFeatureBandTests
{
    [Theory]
    [InlineData("6.0.100", "6.0.100")]
    [InlineData("10.0.512", "10.0.500")]
    [InlineData("11.0.100-preview.6.26359.118", "11.0.100-preview.6")]
    [InlineData("6.0.100-rc.2.21505.57", "6.0.100-rc.2")]
    [InlineData("7.0.100-alpha.1.21558.2", "7.0.100-alpha.1")]
    [InlineData("7.0.100-dev", "7.0.100")]
    [InlineData("7.0.100-ci", "7.0.100")]
    public void ParsesSdkFeatureBandsLikeTheSdk(string version, string expected)
    {
        new SdkFeatureBand(version).ToString().Should().Be(expected);
    }

    [Fact]
    public void SdkVersionUsesPrereleaseFeatureBand()
    {
        SdkVersion.Parse("11.0.100-preview.6.26359.118").FeatureBand
            .Should().Be("11.0.100-preview.6");
    }
}
