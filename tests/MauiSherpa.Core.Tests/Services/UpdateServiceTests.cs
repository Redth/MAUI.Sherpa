using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;
using Moq.Protected;
using Xunit;

namespace MauiSherpa.Core.Tests.Services;

public class UpdateServiceTests
{
    private readonly Mock<ILoggingService> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly UpdateService _service;

    public UpdateServiceTests()
    {
        _mockLogger = new Mock<ILoggingService>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        _service = new UpdateService(_httpClient, _mockLogger.Object);
    }

    [Fact]
    public void GetCurrentVersion_ReturnsVersion()
    {
        var version = _service.GetCurrentVersion();
        
        version.Should().NotBeNullOrEmpty();
        // Note: In test environment, AppInfo.VersionString returns "1.0" by default
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenNewerVersionAvailable_ReturnsUpdateAvailable()
    {
        // Arrange
        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new
                {
                    tag_name = "v0.2.0",
                    name = "v0.2.0 - New Release",
                    body = "New features",
                    prerelease = false,
                    draft = false,
                    published_at = DateTime.UtcNow,
                    html_url = "https://github.com/redth/MAUI.Sherpa/releases/tag/v0.2.0"
                }
            })
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.CheckForUpdateAsync();

        // Assert
        result.UpdateAvailable.Should().BeTrue();
        result.CurrentVersion.Should().Be("0.1.0");
        result.LatestRelease.Should().NotBeNull();
        result.LatestRelease!.TagName.Should().Be("v0.2.0");
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenSameVersion_ReturnsNoUpdate()
    {
        // Arrange - use current version from AppInfo
        var currentVersion = _service.GetCurrentVersion();
        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new
                {
                    tag_name = $"v{currentVersion}",
                    name = $"v{currentVersion}",
                    body = "Current release",
                    prerelease = false,
                    draft = false,
                    published_at = DateTime.UtcNow,
                    html_url = $"https://github.com/redth/MAUI.Sherpa/releases/tag/v{currentVersion}"
                }
            })
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.CheckForUpdateAsync();

        // Assert
        result.UpdateAvailable.Should().BeFalse();
        result.CurrentVersion.Should().Be(currentVersion);
        result.LatestRelease.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenOlderVersion_ReturnsNoUpdate()
    {
        // Arrange - use an old version that's definitely older
        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new
                {
                    tag_name = "v0.0.1",
                    name = "v0.0.1",
                    body = "Old release",
                    prerelease = false,
                    draft = false,
                    published_at = DateTime.UtcNow,
                    html_url = "https://github.com/redth/MAUI.Sherpa/releases/tag/v0.0.1"
                }
            })
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.CheckForUpdateAsync();

        // Assert
        result.UpdateAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CheckForUpdateAsync_IgnoresPrerelease()
    {
        // Arrange - use current version from AppInfo
        var currentVersion = _service.GetCurrentVersion();
        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new
                {
                    tag_name = "v99.0.0",
                    name = "v99.0.0-beta",
                    body = "Beta release",
                    prerelease = true,
                    draft = false,
                    published_at = DateTime.UtcNow.AddDays(1),
                    html_url = "https://github.com/redth/MAUI.Sherpa/releases/tag/v99.0.0"
                },
                new
                {
                    tag_name = $"v{currentVersion}",
                    name = $"v{currentVersion}",
                    body = "Stable release",
                    prerelease = false,
                    draft = false,
                    published_at = DateTime.UtcNow,
                    html_url = $"https://github.com/redth/MAUI.Sherpa/releases/tag/v{currentVersion}"
                }
            })
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.CheckForUpdateAsync();

        // Assert
        result.UpdateAvailable.Should().BeFalse();
        result.LatestRelease!.TagName.Should().Be($"v{currentVersion}");
    }

    [Fact]
    public async Task CheckForUpdateAsync_IgnoresDrafts()
    {
        // Arrange - use current version from AppInfo
        var currentVersion = _service.GetCurrentVersion();
        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new
                {
                    tag_name = "v99.0.0",
                    name = "v99.0.0",
                    body = "Draft release",
                    prerelease = false,
                    draft = true,
                    published_at = DateTime.UtcNow.AddDays(1),
                    html_url = "https://github.com/redth/MAUI.Sherpa/releases/tag/v99.0.0"
                },
                new
                {
                    tag_name = $"v{currentVersion}",
                    name = $"v{currentVersion}",
                    body = "Published release",
                    prerelease = false,
                    draft = false,
                    published_at = DateTime.UtcNow,
                    html_url = $"https://github.com/redth/MAUI.Sherpa/releases/tag/v{currentVersion}"
                }
            })
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.CheckForUpdateAsync();

        // Assert
        result.UpdateAvailable.Should().BeFalse();
        result.LatestRelease!.TagName.Should().Be($"v{currentVersion}");
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenHttpFails_ReturnsNoUpdate()
    {
        // Arrange
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _service.CheckForUpdateAsync();

        // Assert
        result.UpdateAvailable.Should().BeFalse();
        result.LatestRelease.Should().BeNull();
        _mockLogger.Verify(x => x.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public async Task GetAllReleasesAsync_ReturnsAllReleases()
    {
        // Arrange
        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new
                {
                    tag_name = "v0.2.0",
                    name = "v0.2.0",
                    body = "Release 2",
                    prerelease = false,
                    draft = false,
                    published_at = DateTime.UtcNow,
                    html_url = "https://github.com/redth/MAUI.Sherpa/releases/tag/v0.2.0"
                },
                new
                {
                    tag_name = "v0.1.0",
                    name = "v0.1.0",
                    body = "Release 1",
                    prerelease = false,
                    draft = false,
                    published_at = DateTime.UtcNow.AddDays(-1),
                    html_url = "https://github.com/redth/MAUI.Sherpa/releases/tag/v0.1.0"
                }
            })
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.GetAllReleasesAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].TagName.Should().Be("v0.2.0");
        result[1].TagName.Should().Be("v0.1.0");
    }

    [Fact]
    public async Task GetAllReleasesAsync_WhenHttpFails_ReturnsEmpty()
    {
        // Arrange
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _service.GetAllReleasesAsync();

        // Assert
        result.Should().BeEmpty();
        _mockLogger.Verify(x => x.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public async Task CheckForUpdateAsync_HandlesPreReleaseVersionFormat()
    {
        // Arrange - version with pre-release suffix should still compare correctly
        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new
                {
                    tag_name = "v2.0.0-beta.1",
                    name = "v2.0.0-beta.1",
                    body = "Beta with pre-release tag",
                    prerelease = false, // Marked as stable for test purposes
                    draft = false,
                    published_at = DateTime.UtcNow,
                    html_url = "https://github.com/redth/MAUI.Sherpa/releases/tag/v2.0.0-beta.1"
                }
            })
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockResponse);

        // Act
        var result = await _service.CheckForUpdateAsync();

        // Assert
        result.UpdateAvailable.Should().BeTrue(); // 2.0.0 > 1.0
        result.LatestRelease!.TagName.Should().Be("v2.0.0-beta.1");
    }
}
