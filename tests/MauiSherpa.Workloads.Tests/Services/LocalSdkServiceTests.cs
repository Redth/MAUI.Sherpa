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

    [Fact]
    public async Task GetInstalledWorkloadSetAsync_PreservesManifestIds_WhenManifestBandsAreRemapped()
    {
        var sdks = _service.GetInstalledSdkVersions();

        foreach (var sdk in sdks)
        {
            var workloadSet = await _service.GetInstalledWorkloadSetAsync(sdk.FeatureBand);
            if (workloadSet == null || workloadSet.Workloads.Count == 0)
                continue;

            var remappedEntry = workloadSet.Workloads
                .FirstOrDefault(w => !string.IsNullOrEmpty(w.Value.ManifestFeatureBand)
                    && !string.Equals(w.Value.ManifestFeatureBand, sdk.FeatureBand, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(remappedEntry.Key))
                continue;

            remappedEntry.Value.ManifestId.Should().Be(remappedEntry.Key);
            remappedEntry.Value.ManifestVersion.Should().NotBeNullOrEmpty();
            return;
        }
    }

    [Fact]
    public async Task GetInstalledManifestAsync_ResolvesManifestFromRemappedFeatureBand()
    {
        var sdks = _service.GetInstalledSdkVersions();

        foreach (var sdk in sdks)
        {
            var workloadSet = await _service.GetInstalledWorkloadSetAsync(sdk.FeatureBand);
            if (workloadSet == null || workloadSet.Workloads.Count == 0)
                continue;

            var remappedEntry = workloadSet.Workloads
                .FirstOrDefault(w => !string.IsNullOrEmpty(w.Value.ManifestFeatureBand)
                    && !string.Equals(w.Value.ManifestFeatureBand, sdk.FeatureBand, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(remappedEntry.Key))
                continue;

            var manifest = await _service.GetInstalledManifestAsync(sdk.FeatureBand, remappedEntry.Key);

            manifest.Should().NotBeNull();
            manifest!.Version.Should().Be(remappedEntry.Value.ManifestVersion);
            return;
        }
    }

    [Fact]
    public async Task GetInstalledDependenciesAsync_ResolvesDependenciesFromRemappedFeatureBand()
    {
        var sdks = _service.GetInstalledSdkVersions();

        foreach (var sdk in sdks)
        {
            var workloadSet = await _service.GetInstalledWorkloadSetAsync(sdk.FeatureBand);
            if (workloadSet == null || workloadSet.Workloads.Count == 0)
                continue;

            var remappedEntries = workloadSet.Workloads
                .Where(w => !string.IsNullOrEmpty(w.Value.ManifestFeatureBand)
                    && !string.Equals(w.Value.ManifestFeatureBand, sdk.FeatureBand, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(w => w.Key.Contains("android", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(w => w.Key.Contains("maui", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var remappedEntry in remappedEntries)
            {
                var dependencies = await _service.GetInstalledDependenciesAsync(sdk.FeatureBand, remappedEntry.Key);
                if (dependencies == null)
                    continue;

                dependencies.Entries.Should().NotBeEmpty();
                return;
            }
        }
    }
}
