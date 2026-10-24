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
    public async Task CheckForUpdateAsync_WhenRollingChannelIsNewest_FindsVersionedUpdate()
    {
        var service = CreateService("1.0.0");
        SetupMockResponse(new[]
        {
            new { tag_name = "windows-latest", name = "Windows Installer", body = "Installation metadata", prerelease = false, draft = false, published_at = DateTime.UtcNow, html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/windows-latest" },
            new { tag_name = "release-metadata", name = "Metadata", body = "", prerelease = false, draft = false, published_at = DateTime.UtcNow.AddDays(-1), html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/release-metadata" },
            new { tag_name = "v2.0.0", name = "New release", body = "New features", prerelease = false, draft = false, published_at = DateTime.UtcNow.AddDays(-2), html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/v2.0.0" }
        });

        var result = await service.CheckForUpdateAsync();
        var releases = await service.GetAllReleasesAsync();

        result.UpdateAvailable.Should().BeTrue();
        result.LatestRelease!.TagName.Should().Be("v2.0.0");
        releases.Should().ContainSingle().Which.TagName.Should().Be("v2.0.0");
    }

    [Fact]
    public async Task CheckForUpdateAsync_WhenOlderVersionPublishedLater_SelectsHighestNumericVersion()
    {
        var service = CreateService("2.9.0");
        SetupMockResponse(new[]
        {
            new { tag_name = "v2.9.0", name = "Republished release", body = "", prerelease = false, draft = false, published_at = DateTime.UtcNow, html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/v2.9.0" },
            new { tag_name = "v2.10.0", name = "Latest version", body = "", prerelease = false, draft = false, published_at = DateTime.UtcNow.AddDays(-1), html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/v2.10.0" }
        });

        var result = await service.CheckForUpdateAsync();

        result.UpdateAvailable.Should().BeTrue();
        result.LatestRelease!.TagName.Should().Be("v2.10.0");
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3.0", false)]
    [InlineData("v1.2.3.0", "1.2.3", false)]
    [InlineData("v1.2.3.0+release.42", "1.2.3+commit", false)]
    [InlineData("v1.2.3.1", "1.2.3", true)]
    [InlineData("v1.2.3", "1.2.3.1", false)]
    [InlineData("v1.2.4", "1.2.3.9", true)]
    [InlineData("1.2.4+build.42", "1.2.3.0+commit", true)]
    [InlineData("V1.2.4", "1.2.3", true)]
    public async Task CheckForUpdateAsync_NormalizesThreeAndFourPartVersions(string tag, string current, bool expected)
    {
        var service = CreateService(current);
        SetupMockResponse(new[]
        {
            new { tag_name = tag, prerelease = false, draft = false }
        });

        var result = await service.CheckForUpdateAsync();

        result.UpdateAvailable.Should().Be(expected);
        result.CurrentVersion.Should().Be(current);
        result.LatestRelease!.TagName.Should().Be(tag);
    }

    [Fact]
    public async Task CheckForUpdateAsync_OrdersByFourthNumericPart()
    {
        var service = CreateService("1.2.3");
        SetupMockResponse(new[]
        {
            new { tag_name = "v1.2.3.9", published_at = DateTime.UtcNow },
            new { tag_name = "v1.2.3.10", published_at = DateTime.UtcNow.AddDays(-1) },
            new { tag_name = "v1.2.3.0", published_at = DateTime.UtcNow.AddDays(1) }
        });

        var result = await service.CheckForUpdateAsync();

        result.UpdateAvailable.Should().BeTrue();
        result.LatestRelease!.TagName.Should().Be("v1.2.3.10");
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
    public async Task CheckForUpdateAsync_IgnoresPrereleaseTagsEvenWhenNotFlagged()
    {
        var service = CreateService("1.0.0");
        SetupMockResponse(new[]
        {
            new { tag_name = "v99.0.0-beta.1", prerelease = false, draft = false },
            new { tag_name = "v1.0.0", prerelease = false, draft = false }
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("windows-latest")]
    [InlineData("Windows-Latest")]
    [InlineData("latest")]
    [InlineData("release-metadata")]
    [InlineData("vnext")]
    [InlineData("v1")]
    [InlineData("v1.2")]
    [InlineData("v1.2.invalid")]
    [InlineData("v1.2.3-notes/metadata")]
    [InlineData("v1.2.3.4.5")]
    [InlineData("v1.2.3garbage")]
    [InlineData("vv1.2.3")]
    [InlineData("v-1.2.3")]
    [InlineData("v1..3")]
    [InlineData("v1.2.3-")]
    [InlineData("v1.2.3+")]
    [InlineData("v1.2.3+build..metadata")]
    [InlineData("v2147483648.0.0")]
    [InlineData(" v1.2.3")]
    [InlineData("v1.2.3+build ")]
    public async Task GetAllReleasesAsync_ExcludesInvalidVersionTags(string? tag)
    {
        var service = CreateService();
        SetupMockResponse(new[] { new { tag_name = tag, prerelease = false, draft = false } });

        var releases = await service.GetAllReleasesAsync();
        var result = await service.CheckForUpdateAsync();

        releases.Should().BeEmpty();
        result.UpdateAvailable.Should().BeFalse();
        result.LatestRelease.Should().BeNull();
    }

    [Fact]
    public async Task GetAllReleasesAsync_PreservesVersionedReleaseMetadataAndAssets()
    {
        var service = CreateService();
        var publishedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        SetupMockResponse(new[]
        {
            new
            {
                tag_name = "v2.0.0+build.42",
                name = "Summit release",
                body = "Release notes",
                prerelease = false,
                draft = false,
                published_at = publishedAt,
                html_url = "https://github.com/Redth/MAUI.Sherpa/releases/tag/v2.0.0+build.42",
                assets = new[] { new { name = "MAUI-Sherpa.macos.zip", browser_download_url = "https://example.test/update.zip", size = 1024 } }
            }
        });

        var releases = await service.GetAllReleasesAsync();
        var release = releases.Should().ContainSingle().Which;

        release.TagName.Should().Be("v2.0.0+build.42");
        release.Name.Should().Be("Summit release");
        release.Body.Should().Be("Release notes");
        release.PublishedAt.Should().Be(publishedAt);
        release.HtmlUrl.Should().Be("https://github.com/Redth/MAUI.Sherpa/releases/tag/v2.0.0+build.42");
        release.Assets.Should().ContainSingle().Which.Should().Be(
            new GitHubReleaseAsset("MAUI-Sherpa.macos.zip", "https://example.test/update.zip", 1024));
    }

    [Fact]
    public async Task GetAllReleasesAsync_KeepsVersionedPrereleasesAndDraftsForReleaseNotes()
    {
        var service = CreateService();
        SetupMockResponse(new[]
        {
            new { tag_name = "v2.0.0-beta.1+build.42", prerelease = true, draft = false },
            new { tag_name = "v2.0.0", prerelease = false, draft = true }
        });

        var releases = await service.GetAllReleasesAsync();
        var result = await service.CheckForUpdateAsync();

        releases.Should().HaveCount(2);
        releases[0].TagName.Should().Be("v2.0.0-beta.1+build.42");
        releases[0].IsPrerelease.Should().BeTrue();
        releases[1].IsDraft.Should().BeTrue();
        result.UpdateAvailable.Should().BeFalse();
        result.LatestRelease.Should().BeNull();
    }

    [Theory]
    [InlineData("Redth", "MAUI.Sherpa")]
    [InlineData("example-owner", "custom.repo")]
    public async Task GetAllReleasesAsync_UsesConfiguredRepository(string owner, string name)
    {
        var service = new UpdateService(_httpClient, _mockLogger.Object, TestVersion, owner, name);
        SetupMockResponse(Array.Empty<object>());

        await service.GetAllReleasesAsync();

        _mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(request =>
                request.RequestUri!.AbsoluteUri == $"https://api.github.com/repos/{owner}/{name}/releases"),
            ItExpr.IsAny<CancellationToken>());
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

    [Fact]
    public async Task DownloadUpdateAssetAsync_WhenRequestFails_RetriesAndDownloadsFile()
    {
        var service = CreateService();
        var content = new byte[] { 1, 2, 3, 4 };
        var asset = new GitHubReleaseAsset("MAUI-Sherpa.macos.zip", "https://example.test/update.zip", content.Length);
        var destinationPath = Path.GetTempFileName();

        _mockHttpMessageHandler
            .Protected()
            .SetupSequence<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Temporary DNS failure"))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });

        try
        {
            await service.DownloadUpdateAssetAsync(asset, destinationPath, retryDelays: [TimeSpan.Zero]);

            File.ReadAllBytes(destinationPath).Should().Equal(content);
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Exactly(2),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
        }
        finally
        {
            File.Delete(destinationPath);
        }
    }

    [Fact]
    public async Task DownloadUpdateAssetAsync_WhenAllAttemptsFail_ThrowsHelpfulError()
    {
        var service = CreateService();
        var asset = new GitHubReleaseAsset("MAUI-Sherpa.macos.zip", "https://example.test/update.zip", 4);
        var destinationPath = Path.GetTempFileName();

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Temporary DNS failure"));

        try
        {
            var act = () => service.DownloadUpdateAssetAsync(
                asset,
                destinationPath,
                retryDelays: [TimeSpan.Zero, TimeSpan.Zero]);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Unable to reach GitHub's download server after 3 attempts*");
        }
        finally
        {
            File.Delete(destinationPath);
        }
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
    [InlineData("v1.2.3.0", "1.2.3", false)]
    [InlineData("v1.2.3", "1.2.3.0", false)]
    [InlineData("v1.2.3.1", "1.2.3", true)]
    [InlineData("v1.2.3+build.42", "1.2.3+commit", false)]
    [InlineData("V1.2.4", "1.2.3", true)]
    public void IsNewerVersion_ComparesCorrectly(string remote, string current, bool expected)
    {
        UpdateService.IsNewerVersion(remote, current).Should().Be(expected);
    }

    private void SetupMockResponse<T>(T content)
    {
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(content)
            });
    }
}
