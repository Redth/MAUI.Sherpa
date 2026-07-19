using FluentAssertions;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class WorkloadRecommendationPolicyTests
{
    [Fact]
    public void DesktopShowsKeyCrossPlatformWorkloads()
    {
        WorkloadRecommendationPolicy.GetRecommendedWorkloadIds(isLinux: false)
            .Should().Equal("maui", "ios", "macos", "tvos", "maccatalyst", "android", "wasm-tools");
    }

    [Fact]
    public void LinuxUsesMauiAndroidAndExcludesAppleWorkloads()
    {
        var workloads = WorkloadRecommendationPolicy.GetRecommendedWorkloadIds(isLinux: true);

        workloads.Should().Equal("maui-android", "android", "wasm-tools");
        workloads.Should().NotContain(["maui", "ios", "macos", "tvos", "maccatalyst"]);
    }
}
