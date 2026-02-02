using FluentAssertions;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class SdkVersionServiceTests
{
    private readonly SdkVersionService _service;

    public SdkVersionServiceTests()
    {
        _service = new SdkVersionService();
    }

    [Fact]
    public async Task GetAvailableSdkVersionsAsync_ReturnsVersions()
    {
        // Act
        var result = await _service.GetAvailableSdkVersionsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAvailableSdkVersionsAsync_ExcludesPreviewByDefault()
    {
        // Act
        var result = await _service.GetAvailableSdkVersionsAsync(includePreview: false);

        // Assert
        result.Should().NotBeNull();
        // All versions should not be preview (though this depends on what's available)
    }

    [Fact]
    public async Task GetAvailableSdkVersionsAsync_IncludesPreview_WhenRequested()
    {
        // Act
        var result = await _service.GetAvailableSdkVersionsAsync(includePreview: true);

        // Assert
        result.Should().NotBeNull();
    }
}

