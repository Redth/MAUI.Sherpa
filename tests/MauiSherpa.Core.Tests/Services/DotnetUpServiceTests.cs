using FluentAssertions;
using MauiSherpa.Core.Services;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Core.Tests.Services;

public class DotnetUpServiceTests
{
    [Fact]
    public void SelectInstalledSdkTargetRejectsAmbiguousRoots()
    {
        var installations = new[]
        {
            SdkInstallation(@"C:\dotnet-a", "x64"),
            SdkInstallation(@"D:\dotnet-b", "x64")
        };

        var result = DotnetUpService.SelectInstalledSdkTarget(
            installations,
            "10.0.302",
            matchingSpec: null);

        result.Should().BeNull();
    }

    [Fact]
    public void SelectInstalledSdkTargetUsesExactSpecRootAndArchitecture()
    {
        var expected = SdkInstallation(@"C:\dotnet", "arm64");
        var installations = new[]
        {
            SdkInstallation(@"C:\dotnet", "x64"),
            expected,
            SdkInstallation(@"D:\dotnet", "arm64")
        };
        var spec = new DotnetUpInstallSpec
        {
            Component = DotnetUpComponent.Sdk,
            ComponentRaw = "SDK",
            VersionOrChannel = "10.0.3xx",
            InstallRoot = @"C:\dotnet",
            Architecture = "arm64"
        };

        var result = DotnetUpService.SelectInstalledSdkTarget(
            installations,
            "10.0.302",
            spec);

        result.Should().BeSameAs(expected);
    }

    private static DotnetUpInstallation SdkInstallation(string root, string architecture) =>
        new()
        {
            Component = DotnetUpComponent.Sdk,
            ComponentRaw = "SDK",
            Version = "10.0.302",
            InstallRoot = root,
            Architecture = architecture
        };
}
