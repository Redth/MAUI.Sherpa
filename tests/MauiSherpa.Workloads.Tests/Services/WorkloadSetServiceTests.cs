using FluentAssertions;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.NuGet;
using MauiSherpa.Workloads.Services;
using Moq;
using NuGet.Versioning;

namespace MauiSherpa.Workloads.Tests.Services;

public class WorkloadSetServiceTests
{
    private readonly Mock<INuGetClient> _mockNuGetClient;
    private readonly WorkloadSetService _service;

    public WorkloadSetServiceTests()
    {
        _mockNuGetClient = new Mock<INuGetClient>();
        _service = new WorkloadSetService(_mockNuGetClient.Object);
    }

    [Fact]
    public async Task GetAvailableWorkloadSetVersionsAsync_ReturnsVersionsFromNuGet()
    {
        // Arrange
        var expectedVersions = new List<NuGetVersion>
        {
            new("10.0.100"),
            new("10.0.99")
        };
        _mockNuGetClient
            .Setup(c => c.GetPackageVersionsAsync(
                "Microsoft.NET.Workloads.10.0.100",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedVersions);

        // Act
        var result = await _service.GetAvailableWorkloadSetVersionsAsync("10.0.100");

        // Assert
        result.Should().BeEquivalentTo(expectedVersions);
    }

    [Fact]
    public async Task GetWorkloadSetAsync_ParsesWorkloadSetJson()
    {
        // Arrange
        var featureBand = "10.0.100";
        var version = new NuGetVersion("10.0.100");
        var workloadSetJson = """
        {
            "microsoft.net.sdk.maui": "10.0.100/10.0.100",
            "microsoft.net.sdk.android": "35.0.0/10.0.100"
        }
        """;

        _mockNuGetClient
            .Setup(c => c.GetPackageFileContentAsync(
                "Microsoft.NET.Workloads.10.0.100",
                version,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string packageId, NuGetVersion v, string path, CancellationToken ct) =>
                path == "data/microsoft.net.workloads.workloadset.json" ? workloadSetJson : null);

        // Act
        var result = await _service.GetWorkloadSetAsync(featureBand, version);

        // Assert
        result.Should().NotBeNull();
        result!.Version.Should().Be("10.0.100");
        result.FeatureBand.Should().Be(featureBand);
        result.Workloads.Should().ContainKey("microsoft.net.sdk.maui");
        result.Workloads["microsoft.net.sdk.maui"].ManifestVersion.Should().Be("10.0.100");
        result.Workloads["microsoft.net.sdk.maui"].ManifestFeatureBand.Should().Be("10.0.100");
    }

    [Fact]
    public async Task GetWorkloadSetAsync_ReturnsNull_WhenFileNotFound()
    {
        // Arrange
        var featureBand = "10.0.100";
        var version = new NuGetVersion("10.0.100");

        _mockNuGetClient
            .Setup(c => c.GetPackageFileContentAsync(
                It.IsAny<string>(),
                It.IsAny<NuGetVersion>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _service.GetWorkloadSetAsync(featureBand, version);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestWorkloadSetAsync_ReturnsLatestVersion()
    {
        // Arrange
        var featureBand = "10.0.100";
        var versions = new List<NuGetVersion>
        {
            new("10.0.100"),
            new("10.0.99")
        };
        var workloadSetJson = """
        {
            "microsoft.net.sdk.maui": "10.0.100/10.0.100"
        }
        """;

        _mockNuGetClient
            .Setup(c => c.GetPackageVersionsAsync(
                "Microsoft.NET.Workloads.10.0.100",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(versions);

        _mockNuGetClient
            .Setup(c => c.GetPackageFileContentAsync(
                It.IsAny<string>(),
                versions[0],
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(workloadSetJson);

        // Act
        var result = await _service.GetLatestWorkloadSetAsync(featureBand);

        // Assert
        result.Should().NotBeNull();
        result!.Version.Should().Be("10.0.100");
    }

    [Fact]
    public async Task GetLatestWorkloadSetAsync_ReturnsNull_WhenNoVersions()
    {
        // Arrange
        _mockNuGetClient
            .Setup(c => c.GetPackageVersionsAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NuGetVersion>());

        // Act
        var result = await _service.GetLatestWorkloadSetAsync("10.0.100");

        // Assert
        result.Should().BeNull();
    }
}
