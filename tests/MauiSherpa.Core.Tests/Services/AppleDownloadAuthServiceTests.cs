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

    [Fact]
    public void ExtractAppleErrorMessage_ReadsServiceErrorsArrayAndNormalizesNbsp()
    {
        var body = """
            {
              "serviceErrors" : [ {
                "code" : "-20209",
                "message" : "This Apple Account has been\u00a0locked\u00a0for security reasons. Visit iForgot to reset your account (https://iforgot.apple.com).",
                "suppressDismissal" : false
              } ]
            }
            """;

        var message = AppleDownloadAuthService.ExtractAppleErrorMessage(body);

        message.Should().Be("This Apple Account has been locked for security reasons. Visit iForgot to reset your account (https://iforgot.apple.com).");
    }

    [Fact]
    public void ExtractAppleErrorMessage_ReadsTopLevelMessage()
    {
        var body = """{"code":"-20101","message":"Your Apple ID or password was incorrect."}""";

        var message = AppleDownloadAuthService.ExtractAppleErrorMessage(body);

        message.Should().Be("Your Apple ID or password was incorrect.");
    }

    [Theory]
    [InlineData("""{"serviceErrors":[{"code":"-20209","message":"This Apple Account has been locked."}]}""", true)]
    [InlineData("""{"serviceErrors":[{"code":-20209,"message":"This Apple Account has been locked."}]}""", true)]
    [InlineData("""{"code":"-20209","message":"Locked"}""", true)]
    [InlineData("""{"serviceErrors":[{"code":"-99999","message":"This Apple Account has been locked for security reasons. Visit https://iforgot.apple.com."}]}""", true)]
    [InlineData("""{"serviceErrors":[{"code":"-20101","message":"Your Apple ID or password was incorrect."}]}""", false)]
    [InlineData("not json", false)]
    public void IsAccountLockedResponse_DetectsAccountLockAcrossPayloads(string body, bool expected)
    {
        var isLocked = AppleDownloadAuthService.IsAccountLockedResponse(body);

        isLocked.Should().Be(expected);
    }

    [Fact]
    public void CreateCookie_QuotesDesTrustCookieValue()
    {
        var rawDes = new AppleAuthCookie(
            Name: "DES542ffb3b0398b07faaa0163690a8a5e24",
            Value: "HSARMabc==SRVT",
            Domain: "idmsa.apple.com",
            Path: "/",
            Expires: null,
            Secure: true,
            HttpOnly: true);

        var cookie = AppleDownloadAuthService.CreateCookie(rawDes);

        cookie.Value.Should().Be("\"HSARMabc==SRVT\"");

        var alreadyQuoted = rawDes with { Value = "\"HSARMabc==SRVT\"" };
        AppleDownloadAuthService.CreateCookie(alreadyQuoted).Value.Should().Be("\"HSARMabc==SRVT\"");
    }

    [Fact]
    public void ExtractTrustCookies_PreservesTrustCookiesWithFallbackExpiration()
    {
        var now = new DateTime(2026, 10, 23, 15, 0, 0, DateTimeKind.Utc);
        var cookies = new List<AppleAuthCookie>
        {
            new("DES542ffb", "trust-token", "idmsa.apple.com", "/", null, true, true),
            new("aasp", "session-token", "idmsa.apple.com", "/", null, true, true),
            new("myacinfo", "short-lived", ".apple.com", "/", null, true, true)
        };

        var trustCookies = AppleDownloadAuthService.ExtractTrustCookies(cookies, now);

        trustCookies.Should().HaveCount(2);
        trustCookies.Select(c => c.Name).Should().BeEquivalentTo(["DES542ffb", "aasp"]);
        trustCookies.Should().OnlyContain(c => c.Expires == now.Add(AppleDownloadAuthService.PersistedSessionFallbackLifetime));
    }

    [Fact]
    public async Task ImportCookiesAsync_EstablishesSessionAndResolvesEmailFromOlympus()
    {
        var storage = new Dictionary<string, string>();
        var secureStorage = CreateMockSecureStorage(storage);
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.AbsoluteUri.Contains("olympus/v1/session")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"user":{"emailAddress":"dev@example.com"}}""")
            });

        var service = new AppleDownloadAuthService(Mock.Of<ILoggingService>(), secureStorage.Object, handler.Object);
        var cookies = new List<AppleAuthCookie>
        {
            new("myacinfo", "session-token", ".apple.com", "/", null, true, true),
            new("DES542ffb", "trust-token", "idmsa.apple.com", "/", null, true, true)
        };

        var result = await service.ImportCookiesAsync(null, cookies);

        result.Success.Should().BeTrue();
        service.IsAuthenticated.Should().BeTrue();
        service.CurrentAppleId.Should().Be("dev@example.com");
        storage.Should().ContainKey("apple_download_session");
        storage.Should().ContainKey("apple_download_trust_cookies");
        storage["apple_download_appleid"].Should().Be("dev@example.com");
    }

    [Fact]
    public async Task ValidateSessionAsync_WhenUnauthorized_ClearsSessionButPreservesTrustCookiesAndAppleId()
    {
        var storage = new Dictionary<string, string>();
        var secureStorage = CreateMockSecureStorage(storage);
        var olympusStatus = HttpStatusCode.OK;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.AbsoluteUri.Contains("olympus/v1/session")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(olympusStatus)
            {
                Content = new StringContent("""{"user":{"emailAddress":"dev@example.com"}}""")
            });
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.AbsoluteUri.Contains("listDownloads.action")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"resultCode":1100,"resultString":"Your session has expired."}""")
            });

        var service = new AppleDownloadAuthService(Mock.Of<ILoggingService>(), secureStorage.Object, handler.Object);
        await service.ImportCookiesAsync("dev@example.com",
        [
            new("myacinfo", "session-token", ".apple.com", "/", null, true, true),
            new("DES542ffb", "trust-token", "idmsa.apple.com", "/", null, true, true)
        ]);

        olympusStatus = HttpStatusCode.Unauthorized;
        var valid = await service.ValidateSessionAsync();

        valid.Should().BeFalse();
        service.IsAuthenticated.Should().BeFalse();
        service.CurrentAppleId.Should().Be("dev@example.com");
        storage.Should().NotContainKey("apple_download_session");
        storage.Should().ContainKey("apple_download_trust_cookies");
        storage["apple_download_appleid"].Should().Be("dev@example.com");
    }

    private static Mock<ISecureStorageService> CreateMockSecureStorage(Dictionary<string, string> backing)
    {
        var mock = new Mock<ISecureStorageService>();
        mock.Setup(s => s.GetAsync(It.IsAny<string>()))
            .ReturnsAsync((string k) => backing.GetValueOrDefault(k));
        mock.Setup(s => s.SetAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback((string k, string v) => backing[k] = v)
            .Returns(Task.CompletedTask);
        mock.Setup(s => s.RemoveAsync(It.IsAny<string>()))
            .Callback((string k) => backing.Remove(k))
            .Returns(Task.CompletedTask);
        return mock;
    }
}
