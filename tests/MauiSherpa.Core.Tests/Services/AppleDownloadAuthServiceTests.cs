using System.Net;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;
using Moq.Protected;

namespace MauiSherpa.Core.Tests.Services;

public class AppleDownloadAuthServiceTests
{
    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task GetServiceKeyAsync_ReadsCurrentKeyFromRedirect(int statusCode)
    {
        var handler = CreateDiscoveryHandler(
            (HttpStatusCode)statusCode,
            "https://idmsa.apple.com/appleauth/signout?asop=destroy-session&widgetKey=current%2Dapple%2Dkey&rv=3");

        var key = await AppleDownloadAuthService.GetServiceKeyAsync(handler.Object);

        key.Should().Be("current-apple-key");
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(request =>
                request.Method == HttpMethod.Get
                && request.RequestUri == new Uri("https://appstoreconnect.apple.com/logout")
                && !request.Headers.Contains("Cookie")
                && !request.Headers.Contains("Authorization")),
            ItExpr.IsAny<CancellationToken>());
        handler.Protected().Verify(
            "SendAsync", Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task GetServiceKeyAsync_UnexpectedStatusPreservesHttpError(int statusCode)
    {
        var handler = CreateDiscoveryHandler((HttpStatusCode)statusCode);

        var act = () => AppleDownloadAuthService.GetServiceKeyAsync(handler.Object);

        var error = await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage($"*Apple auth service key*HTTP {statusCode}*");
        error.Which.StatusCode.Should().Be((HttpStatusCode)statusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/appleauth/signout?widgetKey=key")]
    [InlineData("http://idmsa.apple.com/appleauth/signout?widgetKey=key")]
    [InlineData("https://example.com/appleauth/signout?widgetKey=key")]
    [InlineData("https://idmsa.apple.com.example.com/appleauth/signout?widgetKey=key")]
    [InlineData("https://idmsa.apple.com:8443/appleauth/signout?widgetKey=key")]
    [InlineData("https://user@idmsa.apple.com/appleauth/signout?widgetKey=key")]
    [InlineData("https://idmsa.apple.com/appleauth/signin?widgetKey=key")]
    public async Task GetServiceKeyAsync_RejectsUnexpectedRedirect(string? location)
    {
        var handler = CreateDiscoveryHandler(HttpStatusCode.Found, location);

        var act = () => AppleDownloadAuthService.GetServiceKeyAsync(handler.Object);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unexpected signout redirect*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("?other=value")]
    [InlineData("?widgetKey=")]
    [InlineData("?widgetKey=%20%20")]
    [InlineData("?widgetKey=invalid%0D%0Akey")]
    public async Task GetServiceKeyAsync_RejectsMissingOrInvalidKey(string query)
    {
        var handler = CreateDiscoveryHandler(
            HttpStatusCode.Found, $"https://idmsa.apple.com/appleauth/signout{query}");

        var act = () => AppleDownloadAuthService.GetServiceKeyAsync(handler.Object);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*did not contain a valid widget key*");
    }

    [Fact]
    public async Task GetServiceKeyAsync_PropagatesNetworkFailure()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network unavailable"));

        var act = () => AppleDownloadAuthService.GetServiceKeyAsync(handler.Object);

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("Network unavailable");
    }

    private static Mock<HttpMessageHandler> CreateDiscoveryHandler(HttpStatusCode statusCode, string? location = null)
    {
        var response = new HttpResponseMessage(statusCode);
        if (location != null)
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
        return handler;
    }

    [Fact]
    public void CaptureCookieDetails_PreservesCookieMetadata()
    {
        var expires = DateTime.UtcNow.AddDays(14);
        var cookies = new CookieCollection
        {
            new Cookie("myacinfo", "session-value", "/account", ".apple.com")
            {
                Expires = expires,
                Secure = true,
                HttpOnly = true
            }
        };

        var result = AppleDownloadAuthService.CaptureCookieDetails(cookies);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(new AppleAuthCookie(
            Name: "myacinfo",
            Value: "session-value",
            Domain: ".apple.com",
            Path: "/account",
            Expires: expires,
            Secure: true,
            HttpOnly: true));
    }

    [Fact]
    public void CalculateSessionExpiresAt_UsesLatestCookieExpiration()
    {
        var now = new DateTime(2026, 5, 25, 18, 0, 0, DateTimeKind.Utc);
        var latestExpiration = now.AddDays(21);
        var cookies = new List<AppleAuthCookie>
        {
            new("short", "value", ".apple.com", "/", now.AddDays(2), false, false),
            new("long", "value", ".apple.com", "/", latestExpiration, true, true)
        };

        var expiresAt = AppleDownloadAuthService.CalculateSessionExpiresAt(cookies, now);

        expiresAt.Should().Be(latestExpiration);
    }

    [Fact]
    public void CalculateSessionExpiresAt_WhenCookiesAreSessionCookies_UsesFallbackLifetime()
    {
        var now = new DateTime(2026, 5, 25, 18, 0, 0, DateTimeKind.Utc);
        var cookies = new List<AppleAuthCookie>
        {
            new("session", "value", ".apple.com", "/", null, true, true)
        };

        var expiresAt = AppleDownloadAuthService.CalculateSessionExpiresAt(cookies, now);

        expiresAt.Should().Be(now.Add(AppleDownloadAuthService.PersistedSessionFallbackLifetime));
    }

    [Fact]
    public void ShouldRenewSession_WhenWithinRenewalThreshold_ReturnsTrue()
    {
        var now = new DateTime(2026, 5, 25, 18, 0, 0, DateTimeKind.Utc);
        var session = new AppleAuthSession("user@example.com", [], now.AddDays(2));

        var shouldRenew = AppleDownloadAuthService.ShouldRenewSession(session, now);

        shouldRenew.Should().BeTrue();
    }

    [Fact]
    public void ShouldRenewSession_WhenOutsideRenewalThreshold_ReturnsFalse()
    {
        var now = new DateTime(2026, 5, 25, 18, 0, 0, DateTimeKind.Utc);
        var session = new AppleAuthSession("user@example.com", [], now.AddDays(14));

        var shouldRenew = AppleDownloadAuthService.ShouldRenewSession(session, now);

        shouldRenew.Should().BeFalse();
    }

    [Fact]
    public void ShouldRenewSession_WhenExpired_ReturnsFalse()
    {
        var now = new DateTime(2026, 5, 25, 18, 0, 0, DateTimeKind.Utc);
        var session = new AppleAuthSession("user@example.com", [], now.AddMinutes(-1));

        var shouldRenew = AppleDownloadAuthService.ShouldRenewSession(session, now);

        shouldRenew.Should().BeFalse();
    }
}
