using FluentAssertions;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class DotnetUpdateResolverTests
{
    [Fact]
    public async Task ResolveChannelAsync_ExactVersionReportsMissingWhenNotInstalled()
    {
        var result = await new DotnetUpdateResolver().ResolveChannelAsync(
            DotnetUpComponent.Sdk,
            "10.0.302",
            new DotnetUpListResult());

        result.Available.Should().Be("10.0.302");
        result.Installed.Should().BeNull();
    }

    [Theory]
    [InlineData("10.0.303", "10.0.302", true)]
    [InlineData("10.0.302", "10.0.302", false)]
    [InlineData("10.0.301", "10.0.302", false)]
    [InlineData("11.0.100-preview.6.26359.118", "11.0.100-preview.5.26302.115", true)]
    public void IsUpdateAvailable_UsesSemanticVersionOrdering(
        string available,
        string installed,
        bool expected)
    {
        DotnetUpdateResolver.IsUpdateAvailable(available, installed).Should().Be(expected);
    }
}
