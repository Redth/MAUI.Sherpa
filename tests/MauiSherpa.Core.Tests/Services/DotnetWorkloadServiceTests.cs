using System.Text.Json;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using MauiSherpa.Workloads.Models;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class DotnetWorkloadServiceTests
{
    private readonly DotnetWorkloadService _service = new(
        Mock.Of<IDotnetUpService>(),
        Mock.Of<ILoggingService>());

    [Fact]
    public async Task TargetsAreGroupedByRootArchitectureAndSdkFeatureBand()
    {
        var list = new DotnetUpListResult
        {
            Installations =
            [
                Sdk("10.0.301"),
                Sdk("10.0.302"),
                Sdk("11.0.100-preview.5.26302.115"),
                Sdk("11.0.100-preview.6.26359.118")
            ]
        };

        var result = await _service.GetTargetsAsync(list);

        result.Should().HaveCount(3);
        result.Single(target => target.FeatureBand.ToString() == "10.0.300")
            .RepresentativeSdkVersion.Should().Be("10.0.302");
        result.Select(target => target.FeatureBand.ToString())
            .Should().Contain(["11.0.100-preview.5", "11.0.100-preview.6"]);
    }

    [Fact]
    public void InstallRequestPinsExactSdkContextAndRootEnvironment()
    {
        var target = Target("10.0.302");

        var request = _service.CreateInstallRequest(target, ["maui", "android"], "10.0.302");

        request.Command.Should().Be(target.DotnetPath);
        request.Arguments.Should().Equal(
            "workload", "install", "maui", "android", "--version", "10.0.302");
        request.Environment!["DOTNET_ROOT"].Should().Be(target.InstallRoot);
        request.Environment["DOTNET_MULTILEVEL_LOOKUP"].Should().Be("0");
        request.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"].Should().Be("0");
        request.Environment["MSBUILDDISABLENODEREUSE"].Should().Be("1");
        request.RequiresElevation.Should().BeFalse();
        request.WorkingDirectory.Should().NotBeNull();

        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(request.WorkingDirectory!, "global.json")));
        document.RootElement.GetProperty("sdk").GetProperty("version").GetString()
            .Should().Be("10.0.302");
        document.RootElement.GetProperty("sdk").GetProperty("rollForward").GetString()
            .Should().Be("disable");
    }

    [Fact]
    public void UpdateAndUninstallRequestsUseSupportedCliCommands()
    {
        var target = Target("10.0.302");

        _service.CreateUpdateSetRequest(target, "10.0.301.1").Arguments
            .Should().Equal("workload", "update", "--version", "10.0.301.1");
        _service.CreateLatestSetUpdateRequest(target).Arguments
            .Should().Equal("workload", "update");
        _service.CreateLatestSetUpdateRequest(Target("11.0.100-preview.6.26359.118")).Arguments
            .Should().Equal("workload", "update", "--include-previews");
        _service.CreateUninstallRequest(target, ["maui"]).Arguments
            .Should().Equal("workload", "uninstall", "maui");
        _service.CreateRepairRequest(target).Arguments
            .Should().Equal("workload", "repair");
    }

    [Fact]
    public void MutationRequestElevatesWhenWorkloadStateIsNotWritable()
    {
        var target = Target("10.0.302") with { CanWrite = false };

        var request = _service.CreateUpdateSetRequest(target, "10.0.300.2");

        request.RequiresElevation.Should().BeTrue();
        request.ElevationPrompt.Should().Contain(target.InstallRoot);
        request.UsePseudoTerminal.Should().BeTrue();
        request.ConfirmationButtonText.Should().Be("Change set");
    }

    [Fact]
    public void RestoreRequestUsesProjectContextAndExactRoot()
    {
        var target = Target("10.0.302");
        var project = Path.Combine(Path.GetTempPath(), $"sherpa-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(project);
        try
        {
            var request = _service.CreateRestoreRequest(target, project);

            request.Arguments.Should().Equal("workload", "restore");
            request.WorkingDirectory.Should().Be(Path.GetFullPath(project));
            request.Environment!["DOTNET_ROOT"].Should().Be(target.InstallRoot);
            request.Environment["DOTNET_MULTILEVEL_LOOKUP"].Should().Be("0");
        }
        finally
        {
            Directory.Delete(project);
        }
    }

    [Fact]
    public void StableTargetDoesNotOfferPreviewSetAsAutomaticUpdate()
    {
        var inventory = new DotnetWorkloadInventory
        {
            Target = Target("10.0.302"),
            UpdateMode = DotnetWorkloadUpdateMode.WorkloadSet,
            ActiveWorkloadVersion = "10.0.300.1",
            AvailableSetVersions =
            [
                new DotnetWorkloadSetVersion { Version = "11.0.100-preview.1", IsPrerelease = true },
                new DotnetWorkloadSetVersion { Version = "10.0.300.2" }
            ]
        };

        inventory.LatestAvailableSetVersion.Should().Be("10.0.300.2");
        inventory.UpdateAvailable.Should().BeTrue();
    }

    private static DotnetUpInstallation Sdk(string version) => new()
    {
        Component = DotnetUpComponent.Sdk,
        ComponentRaw = "SDK",
        Version = version,
        InstallRoot = "/tmp/dotnet",
        Architecture = "arm64"
    };

    private static DotnetWorkloadTarget Target(string version) => new()
    {
        InstallRoot = "/tmp/dotnet",
        DotnetPath = "/tmp/dotnet/dotnet",
        Architecture = "arm64",
        FeatureBand = new SdkFeatureBand(version),
        RepresentativeSdkVersion = version,
        IsManagedByDotnetUp = true,
        CanWrite = true
    };
}
