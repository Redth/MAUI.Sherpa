using FluentAssertions;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Tests.Models;

public class WorkloadSetTests
{
    [Fact]
    public void WorkloadSet_CanBeCreated()
    {
        // Arrange & Act
        var workloadSet = new WorkloadSet
        {
            Version = "10.0.100",
            FeatureBand = "10.0.100",
            Workloads = new Dictionary<string, WorkloadSetEntry>
            {
                ["microsoft.net.sdk.maui"] = new WorkloadSetEntry
                {
                    ManifestId = "microsoft.net.sdk.maui",
                    ManifestVersion = "10.0.100",
                    ManifestFeatureBand = "10.0.100"
                }
            }
        };

        // Assert
        workloadSet.Version.Should().Be("10.0.100");
        workloadSet.FeatureBand.Should().Be("10.0.100");
        workloadSet.Workloads.Should().ContainKey("microsoft.net.sdk.maui");
    }

    [Fact]
    public void WorkloadSetEntry_HasCorrectProperties()
    {
        // Arrange & Act
        var entry = new WorkloadSetEntry
        {
            ManifestId = "microsoft.net.sdk.android",
            ManifestVersion = "35.0.0",
            ManifestFeatureBand = "10.0.100"
        };

        // Assert
        entry.ManifestId.Should().Be("microsoft.net.sdk.android");
        entry.ManifestVersion.Should().Be("35.0.0");
        entry.ManifestFeatureBand.Should().Be("10.0.100");
    }

    [Fact]
    public void WorkloadSetEntry_FeatureBandCanBeNull()
    {
        // Arrange & Act
        var entry = new WorkloadSetEntry
        {
            ManifestId = "test",
            ManifestVersion = "1.0.0",
            ManifestFeatureBand = null
        };

        // Assert
        entry.ManifestFeatureBand.Should().BeNull();
    }
}

public class WorkloadManifestTests
{
    [Fact]
    public void WorkloadManifest_CanBeCreated()
    {
        // Arrange & Act
        var manifest = new WorkloadManifest
        {
            Version = "10.0.100",
            Description = "Test manifest",
            DependsOn = new Dictionary<string, string>
            {
                ["microsoft.net.sdk.android"] = "35.0.0"
            },
            Workloads = new Dictionary<string, WorkloadDefinition>
            {
                ["maui"] = new WorkloadDefinition
                {
                    Id = "maui",
                    Description = "MAUI workload",
                    IsAbstract = false,
                    Kind = "concrete"
                }
            },
            Packs = new Dictionary<string, PackDefinition>
            {
                ["maui.sdk"] = new PackDefinition
                {
                    Id = "maui.sdk",
                    Version = "10.0.100",
                    Kind = "sdk"
                }
            }
        };

        // Assert
        manifest.Version.Should().Be("10.0.100");
        manifest.Description.Should().Be("Test manifest");
        manifest.DependsOn.Should().ContainKey("microsoft.net.sdk.android");
        manifest.Workloads.Should().ContainKey("maui");
        manifest.Packs.Should().ContainKey("maui.sdk");
    }

    [Fact]
    public void WorkloadManifest_DefaultCollectionsAreEmpty()
    {
        // Arrange & Act
        var manifest = new WorkloadManifest
        {
            Version = "1.0.0"
        };

        // Assert
        manifest.DependsOn.Should().BeEmpty();
        manifest.Workloads.Should().BeEmpty();
        manifest.Packs.Should().BeEmpty();
    }
}
