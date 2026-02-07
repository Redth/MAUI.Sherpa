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
    private const string TestVersion = "1.0.0";

    private UpdateService CreateService(string version = TestVersion)
    {
        return new UpdateService(_httpClient, _mockLogger.Object, version);
    }

    public UpdateServiceTests()
    {
        _mockLogger = new Mock<ILoggingService>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
    }

    [Fact]
    public void GetCurrentVersion_ReturnsInjectedVersion()
    {
        var service = CreateService("2.5.3");
        service.GetCurrentVersion().Should().Be("2.5.3");
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenNewerVersionAvailable_ReturnsUpdateAvailable()
    {
        var service = CreateService("1.0.0");
        SetupMockResponse(new[]
        {
            new { tag_name = "v2.0.0", name = "v2.0.0 - New Release", body = "New features", prerelease = false, draft = false, published_at = DateTime.UtcNow, html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/v2.0.0" }
        });

        var result = await service.CheckForUpdateAsync();

        result.UpdateAvailable.Should().BeTrue();
        result.CurrentVersion.Should().Be("1.0.0");
        result.LatestRelease.Should().NotBeNull();
        result.LatestRelease!.TagName.Should().Be("v2.0.0");
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenSameVersion_ReturnsNoUpdate()
    {
        var service = CreateService("1.0.0");
        SetupMockResponse(new[]
        {
            new { tag_name = "v1.0.0", name = "v1.0.0", body = "Current release", prerelease = false, draft = false, published_at = DateTime.UtcNow, html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/v1.0.0" }
        });

        var result = await service.CheckForUpdateAsync();

        result.UpdateAvailable.Should().BeFalse();
        result.CurrentVersion.Should().Be("1.0.0");
        result.LatestRelease.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenOlderVersion_ReturnsNoUpdate()
    {
        var service = CreateService("2.0.0");
        SetupMockResponse(new[]
        {
            new { tag_name = "v0.0.1", name = "v0.0.1", body = "Old release", prerelease = false, draft = false, published_at = DateTime.UtcNow, html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/v0.0.1" }
        });

        var result = await service.CheckForUpdateAsync();

        result.UpdateAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CheckForUpdateAsync_IgnoresPrerelease()
    {
        var service = CreateService("1.0.0");
        SetupMockResponse(new[]
        {
            new { tag_name = "v99.0.0", name = "v99.0.0-beta", body = "Beta release", prerelease = true, draft = false, published_at = DateTime.UtcNow.AddDays(1), html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/v99.0.0" },
            new { tag_name = "v1.0.0", name = "v1.0.0", body = "Stable release", prerelease = false, draft = false, published_at = DateTime.UtcNow, html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/v1.0.0" }
        });

        var result = await service.CheckForUpdateAsync();

        result.UpdateAvailable.Should().BeFalse();
        result.LatestRelease!.TagName.Should().Be("v1.0.0");
    }

    [Fact]
    public async Task CheckForUpdateAsync_IgnoresDrafts()
    {
        var service = CreateService("1.0.0");
        SetupMockResponse(new[]
        {
            new { tag_name = "v99.0.0", name = "v99.0.0", body = "Draft release", prerelease = false, draft = true, published_at = DateTime.UtcNow.AddDays(1), html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/v99.0.0" },
            new { tag_name = "v1.0.0", name = "v1.0.0", body = "Published release", prerelease = false, draft = false, published_at = DateTime.UtcNow, html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/v1.0.0" }
        });

        var result = await service.CheckForUpdateAsync();

        result.UpdateAvailable.Should().BeFalse();
        result.LatestRelease!.TagName.Should().Be("v1.0.0");
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenHttpFails_ReturnsNoUpdate()
    {
        var service = CreateService();
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var result = await service.CheckForUpdateAsync();

        result.UpdateAvailable.Should().BeFalse();
        result.LatestRelease.Should().BeNull();
        _mockLogger.Verify(x => x.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public async Task GetAllReleasesAsync_ReturnsAllReleases()
    {
        var service = CreateService();
        SetupMockResponse(new[]
        {
            new { tag_name = "v0.2.0", name = "v0.2.0", body = "Release 2", prerelease = false, draft = false, published_at = DateTime.UtcNow, html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/v0.2.0" },
            new { tag_name = "v0.1.0", name = "v0.1.0", body = "Release 1", prerelease = false, draft = false, published_at = DateTime.UtcNow.AddDays(-1), html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/v0.1.0" }
        });

        var result = await service.GetAllReleasesAsync();

        result.Should().HaveCount(2);
        result[0].TagName.Should().Be("v0.2.0");
        result[1].TagName.Should().Be("v0.1.0");
    }

    [Fact]
    public async Task GetAllReleasesAsync_WhenHttpFails_ReturnsEmpty()
    {
        var service = CreateService();
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var result = await service.GetAllReleasesAsync();

        result.Should().BeEmpty();
        _mockLogger.Verify(x => x.LogError(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Theory]
    [InlineData("v2.0.0", "1.0.0", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v0.9.0", "1.0.0", false)]
    [InlineData("v1.0.1", "1.0.0", true)]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("v2.0.0-beta.1", "1.0.0", true)]
    [InlineData("v1.0.0", "1.0.0-beta.1", false)]
    [InlineData("v10.0.0", "9.9.9", true)]
    public void IsNewerVersion_ComparesCorrectly(string remote, string current, bool expected)
    {
        UpdateService.IsNewerVersion(remote, current).Should().Be(expected);
    }

    private void SetupMockResponse<T>(T content)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(content) };
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }
}
