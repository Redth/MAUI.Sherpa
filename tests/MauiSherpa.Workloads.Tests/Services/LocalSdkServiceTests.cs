using FluentAssertions;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class LocalSdkServiceTests
{
    private readonly LocalSdkService _service;

    public LocalSdkServiceTests()
    {
        _service = new LocalSdkService();
    }

    [Fact]
    public void GetDotNetSdkPath_ReturnsPath_WhenSdkInstalled()
    {
        // Act
        var result = _service.GetDotNetSdkPath();

        // Assert - SDK should be installed on the test machine
        result.Should().NotBeNullOrEmpty();
        Directory.Exists(result).Should().BeTrue();
        Directory.Exists(Path.Combine(result!, "sdk")).Should().BeTrue();
    }

    [Fact]
    public void GetInstalledSdkVersions_ReturnsVersions()
    {
        // Act
        var result = _service.GetInstalledSdkVersions();

        // Assert - at least one SDK should be installed
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(v =>
        {
            v.Major.Should().BeGreaterThanOrEqualTo(6);
            v.Minor.Should().BeGreaterThanOrEqualTo(0);
            v.Version.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public void GetInstalledSdkVersions_ReturnsOrderedByVersionDescending()
    {
        // Act
        var result = _service.GetInstalledSdkVersions();

        // Assert
        result.Should().BeInDescendingOrder(v => v.Major)
            .And.ThenBeInDescendingOrder(v => v.Minor)
            .And.ThenBeInDescendingOrder(v => v.Patch);
    }

    [Fact]
    public void GetInstalledWorkloadManifests_ReturnsManifests_ForValidFeatureBand()
    {
        // Arrange
        var sdks = _service.GetInstalledSdkVersions();
        if (sdks.Count == 0)
            return; // Skip if no SDKs installed

        var featureBand = sdks.First().FeatureBand;

        // Act
        var result = _service.GetInstalledWorkloadManifests(featureBand);

        // Assert - may be empty if no workloads installed, but should not throw
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetInstalledWorkloadManifests_ReturnsEmpty_ForInvalidFeatureBand()
    {
        // Act
        var result = _service.GetInstalledWorkloadManifests("99.99.999");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInstalledSdkInfoAsJsonStringAsync_ReturnsValidJson()
    {
        // Act
        var result = await _service.GetInstalledSdkInfoAsJsonStringAsync(
            includeManifestDetails: false, 
            indented: true);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("dotnetPath");
        result.Should().Contain("totalInstalledSdks");

        // Should be valid JSON
        var action = () => System.Text.Json.JsonDocument.Parse(result);
        action.Should().NotThrow();
    }

    [Fact]
    public async Task GetInstalledSdkInfoAsJsonAsync_ReturnsJsonDocument()
    {
        // Act
        var result = await _service.GetInstalledSdkInfoAsJsonAsync(includeManifestDetails: false);

        // Assert
        result.Should().NotBeNull();
        result.RootElement.TryGetProperty("dotnetPath", out _).Should().BeTrue();
        result.RootElement.TryGetProperty("totalInstalledSdks", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetInstalledWorkloadSetAsync_ReturnsWorkloadSet_WhenAvailable()
    {
        // Arrange
        var sdks = _service.GetInstalledSdkVersions();
        if (sdks.Count == 0)
            return; // Skip if no SDKs

        var featureBand = sdks.First().FeatureBand;

        // Act
        var result = await _service.GetInstalledWorkloadSetAsync(featureBand);

        // Assert - may be null if using loose manifests, but should not throw
        // If a workload set is found, validate its properties
        if (result != null)
        {
            result.Version.Should().NotBeNullOrEmpty();
            result.FeatureBand.Should().Be(featureBand);
        }
    }
}
