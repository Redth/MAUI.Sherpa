using FluentAssertions;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class DotnetSdkPresentationBuilderTests
{
    [Fact]
    public void Build_GroupsInstalledComponentsByMajorMinorDescending()
    {
        var list = new DotnetUpListResult
        {
            Installations =
            [
                Installation(DotnetUpComponent.Runtime, "10.0.8", isValid: false),
                Installation(DotnetUpComponent.Sdk, "10.0.302"),
                Installation(DotnetUpComponent.Sdk, "10.0.203"),
                Installation(DotnetUpComponent.AspNetCore, "10.0.8"),
                Installation(DotnetUpComponent.Sdk, "9.0.305"),
                Installation(DotnetUpComponent.Runtime, "9.0.9")
            ]
        };

        var summary = DotnetSdkPresentationBuilder.Build(list, workloadInventories:
        [
            Inventory("10.0.302", "/dotnet", "arm64"),
            Inventory("10.0.203", "/dotnet", "arm64"),
            Inventory("10.0.301", "/alt-dotnet", "x64"),
            Inventory("9.0.305", "/dotnet", "arm64")
        ]);

        summary.InstalledGroups.Select(group => group.MajorMinor)
            .Should().Equal("10.0", "9.0");

        var tenGroup = summary.InstalledGroups[0];
        tenGroup.NewestSdkVersion.Should().Be("10.0.302");
        tenGroup.Counts.Should().BeEquivalentTo(new DotnetInstalledComponentCounts
        {
            Total = 4,
            Sdk = 2,
            Runtime = 1,
            AspNetCore = 1
        });
        tenGroup.HasInvalidComponents.Should().BeTrue();
        tenGroup.WorkloadFeatureBandCount.Should().Be(2);
        tenGroup.WorkloadInventories
            .Select(inventory => inventory.Target.FeatureBand.ToString())
            .Should().Equal("10.0.300", "10.0.300", "10.0.200");
        tenGroup.Installations
            .Select(installation => (installation.Component, installation.Version))
            .Should().Equal(
                (DotnetUpComponent.Sdk, "10.0.302"),
                (DotnetUpComponent.Sdk, "10.0.203"),
                (DotnetUpComponent.Runtime, "10.0.8"),
                (DotnetUpComponent.AspNetCore, "10.0.8"));
    }

    [Fact]
    public void Build_SplitsTrackedSdkChannelsFromNonSdkSpecs()
    {
        var list = new DotnetUpListResult
        {
            InstallSpecs =
            [
                Spec(DotnetUpComponent.Runtime, "10.0"),
                Spec(DotnetUpComponent.Sdk, "preview"),
                Spec(DotnetUpComponent.Sdk, "10.0.103"),
                Spec(DotnetUpComponent.AspNetCore, "9.0")
            ]
        };

        var summary = DotnetSdkPresentationBuilder.Build(list);

        summary.TrackedSdkChannels.Select(channel => (channel.Channel, channel.IsPinned))
            .Should().Equal(("preview", false), ("10.0.103", true));

        summary.TrackedNonSdkSpecs.Select(spec => (spec.Component, spec.VersionOrChannel))
            .Should().Equal(
                (DotnetUpComponent.Runtime, "10.0"),
                (DotnetUpComponent.AspNetCore, "9.0"));
    }

    [Fact]
    public void FindTrackedSdkSpec_MapsInstalledVersionToItsFeatureBandChannel()
    {
        var installation = Installation(DotnetUpComponent.Sdk, "9.0.301");
        var specs = new[]
        {
            Spec(DotnetUpComponent.Sdk, "9.0.1xx"),
            Spec(DotnetUpComponent.Sdk, "9.0.3xx")
        };

        var result = DotnetSdkPresentationBuilder.FindTrackedSdkSpec(installation, specs);

        result.Should().BeSameAs(specs[1]);
    }

    [Fact]
    public void FindTrackedSdkSpec_ReturnsNullWhenMultipleSpecsMatch()
    {
        var installation = Installation(DotnetUpComponent.Sdk, "9.0.301");
        var specs = new[]
        {
            Spec(DotnetUpComponent.Sdk, "9"),
            Spec(DotnetUpComponent.Sdk, "9.0")
        };

        DotnetSdkPresentationBuilder.FindTrackedSdkSpec(installation, specs).Should().BeNull();
    }

    [Fact]
    public void Build_ComputesAggregateUpdateStateIncludingUnresolvedChannels()
    {
        var summary = DotnetSdkPresentationBuilder.Build(
            list: new DotnetUpListResult(),
            updatePreviews:
            [
                new DotnetUpdatePreview
                {
                    Component = DotnetUpComponent.Sdk,
                    Channel = "10.0.1xx",
                    InstalledVersion = "10.0.100",
                    AvailableVersion = "10.0.103",
                    UpdateAvailable = true,
                    IsPinned = false
                },
                new DotnetUpdatePreview
                {
                    Component = DotnetUpComponent.Runtime,
                    Channel = "9.0",
                    InstalledVersion = "9.0.9",
                    AvailableVersion = null,
                    UpdateAvailable = false,
                    IsPinned = false
                },
                new DotnetUpdatePreview
                {
                    Component = DotnetUpComponent.Sdk,
                    Channel = "10.0.103",
                    InstalledVersion = "10.0.103",
                    AvailableVersion = "10.0.103",
                    UpdateAvailable = false,
                    IsPinned = true
                },
                new DotnetUpdatePreview
                {
                    Component = DotnetUpComponent.AspNetCore,
                    Channel = "10.0",
                    InstalledVersion = "10.0.8",
                    AvailableVersion = "10.0.8",
                    UpdateAvailable = false,
                    IsPinned = false
                }
            ]);

        summary.Updates.Should().BeEquivalentTo(new
        {
            IsChecked = true,
            PreviewCount = 4,
            AvailableUpdateCount = 1,
            SdkUpdateCount = 1,
            RuntimeUpdateCount = 0,
            UnresolvedCount = 1,
            HasUpdates = true,
            HasUnresolvedNonPinnedChannels = true
        });
    }

    [Fact]
    public void FilterProjectWorkloadInventories_UsesInstalledVersionRootArchitectureAndFeatureBand()
    {
        var inventories = new[]
        {
            Inventory("10.0.302", "/dotnet", "arm64"),
            Inventory("10.0.302", "/dotnet", "x64"),
            Inventory("10.0.302", "/other", "arm64"),
            Inventory("10.0.203", "/dotnet", "arm64"),
        };
        var resolution = new GlobalJsonResolution
        {
            FolderPath = "/repo",
            Status = GlobalJsonStatus.Resolved,
            InstalledVersion = "10.0.302",
            InstalledSdkInstallRoot = "/dotnet",
            InstalledSdkArchitecture = "arm64"
        };

        var matches = DotnetSdkPresentationBuilder.FilterProjectWorkloadInventories(resolution, inventories);
        var summary = DotnetSdkPresentationBuilder.Build(new DotnetUpListResult(), inventories, projectResolution: resolution);

        matches.Should().ContainSingle();
        matches[0].Target.FeatureBand.Should().Be(new SdkFeatureBand("10.0.302"));
        summary.ProjectWorkloads.Should().NotBeNull();
        summary.ProjectWorkloads!.FeatureBand.Should().Be(new SdkFeatureBand("10.0.302"));
        summary.ProjectWorkloads.MatchingInventories.Should().ContainSingle();
        summary.ProjectWorkloads.SelectedInventory.Should().BeSameAs(matches[0]);
    }

    [Fact]
    public void BuildProjectInstalledGroup_UsesExactSdkAndRelatedRuntimeLineFromResolvedTarget()
    {
        var installations = new[]
        {
            Installation(DotnetUpComponent.Sdk, "10.0.302"),
            Installation(DotnetUpComponent.Sdk, "10.0.301"),
            Installation(DotnetUpComponent.Runtime, "10.0.8"),
            Installation(DotnetUpComponent.AspNetCore, "10.0.8"),
            Installation(DotnetUpComponent.Runtime, "9.0.9"),
            Installation(DotnetUpComponent.Runtime, "10.0.8", installRoot: "/other")
        };
        var inventories = new[]
        {
            Inventory("10.0.302", "/dotnet", "arm64"),
            Inventory("10.0.302", "/other", "arm64")
        };
        var resolution = new GlobalJsonResolution
        {
            FolderPath = "/repo",
            Status = GlobalJsonStatus.Resolved,
            InstalledVersion = "10.0.302",
            InstalledSdkInstallRoot = "/dotnet",
            InstalledSdkArchitecture = "arm64"
        };

        var group = DotnetSdkPresentationBuilder.BuildProjectInstalledGroup(
            resolution,
            installations,
            inventories);

        group.Should().NotBeNull();
        group!.DisplayName.Should().Be(".NET 10.0");
        group.Installations.Select(installation => (installation.Component, installation.Version))
            .Should().Equal(
                (DotnetUpComponent.Sdk, "10.0.302"),
                (DotnetUpComponent.Runtime, "10.0.8"),
                (DotnetUpComponent.AspNetCore, "10.0.8"));
        group.WorkloadInventories.Should().ContainSingle();
        group.WorkloadInventories[0].Target.InstallRoot.Should().Be("/dotnet");
    }

    [Fact]
    public void BuildProjectInstalledGroup_ReturnsNullForAmbiguousSdkTargets()
    {
        var installations = new[]
        {
            Installation(DotnetUpComponent.Sdk, "10.0.302"),
            Installation(DotnetUpComponent.Sdk, "10.0.302", installRoot: "/other")
        };
        var resolution = new GlobalJsonResolution
        {
            FolderPath = "/repo",
            Status = GlobalJsonStatus.Resolved,
            InstalledVersion = "10.0.302"
        };

        DotnetSdkPresentationBuilder.BuildProjectInstalledGroup(
                resolution,
                installations,
                workloadInventories: null)
            .Should().BeNull();
    }

    private static DotnetUpInstallation Installation(
        DotnetUpComponent component,
        string version,
        bool isValid = true,
        string installRoot = "/dotnet",
        string architecture = "arm64") => new()
        {
            Component = component,
            ComponentRaw = component.ToString(),
            Version = version,
            InstallRoot = installRoot,
            Architecture = architecture,
            IsValid = isValid
        };

    private static DotnetUpInstallSpec Spec(
        DotnetUpComponent component,
        string versionOrChannel,
        string installRoot = "/dotnet",
        string architecture = "arm64") => new()
        {
            Component = component,
            ComponentRaw = component.ToString(),
            VersionOrChannel = versionOrChannel,
            Source = DotnetUpInstallSource.Explicit,
            InstallRoot = installRoot,
            Architecture = architecture
        };

    private static DotnetWorkloadInventory Inventory(
        string sdkVersion,
        string installRoot,
        string architecture) => new()
        {
            Target = new DotnetWorkloadTarget
            {
                InstallRoot = installRoot,
                DotnetPath = Path.Combine(installRoot, "dotnet"),
                Architecture = architecture,
                FeatureBand = new SdkFeatureBand(sdkVersion),
                RepresentativeSdkVersion = sdkVersion
            }
        };
}
