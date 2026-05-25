using System.Net;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class AppleDownloadAuthServiceTests
{
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
