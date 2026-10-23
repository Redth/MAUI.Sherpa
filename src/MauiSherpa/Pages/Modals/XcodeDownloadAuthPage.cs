using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using AppKit;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Platforms.MacOS.Platform;
using WebKit;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class XcodeDownloadAuthPage : WizardFormPage<bool>
{
#if MACOSAPP
    private const int DefaultWidth = 600;
    private const int DefaultHeight = 640;
#else
    private const int DefaultWidth = 560;
    private const int DefaultHeight = 540;
#endif
    private const string WebSignInEntryUrl = "https://developer.apple.com/account";

    protected override string FormTitle => "Apple Developer Sign In";
    protected override string DefaultSubmitText => "Verify";
    protected override string BlazorRoute => "/modal/xcode-download-auth";

#if MACOSAPP
    private NSView? _appleWebSignInContainer;
    private WKWebView? _appleWebView;
    private NSTextField? _urlStatusLabel;
    private Func<IReadOnlyList<AppleAuthCookie>, Task<bool>>? _onCookiesCaptured;
    private bool _isWebSignInActive;
    private bool _isEvaluatingCookies;
    private readonly Dictionary<string, int> _triedMyAcInfoAttempts = new(StringComparer.Ordinal);
#endif

    public XcodeDownloadAuthPage(HybridFormBridgeHolder bridgeHolder)
        : base(bridgeHolder)
    {
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, DefaultWidth);
        MacOSPage.SetModalSheetHeight(this, DefaultHeight);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, DefaultWidth);
        GtkPage.SetModalHeight(this, DefaultHeight);
#endif
    }

    protected override void ConfigureBridge(HybridFormBridge bridge)
    {
#if MACOSAPP
        bridge.Parameters["SupportsWebSignIn"] = true;
        bridge.Parameters["StartWebSignIn"] = new Func<Func<IReadOnlyList<AppleAuthCookie>, Task<bool>>, Task>(StartWebSignInAsync);
        bridge.Parameters["StopWebSignIn"] = new Action(StopWebSignIn);
        bridge.CancelRequested += StopWebSignIn;
        bridge.CloseRequested += StopWebSignIn;
#else
        bridge.Parameters["SupportsWebSignIn"] = false;
#endif
    }

#if MACOSAPP
    private async Task StartWebSignInAsync(Func<IReadOnlyList<AppleAuthCookie>, Task<bool>> onCookiesCaptured)
    {
        _onCookiesCaptured = onCookiesCaptured;
        _triedMyAcInfoAttempts.Clear();

        var cookieStore = WKWebsiteDataStore.DefaultDataStore.HttpCookieStore;
        await ClearShortLivedWebKitCookiesAsync(cookieStore);

        Dispatcher.Dispatch(() =>
        {
            if (FormWebView?.Handler?.PlatformView is not NSView blazorNative)
                return;

            var superview = blazorNative.Superview;
            if (superview == null)
                return;

            TearDownWebViewOnMainThread(restoreSheetSize: false);

            MacOSPage.SetModalSheetWidth(this, DefaultWidth);
            MacOSPage.SetModalSheetHeight(this, DefaultHeight);

            var containerBounds = blazorNative.Frame;
            nfloat barHeight = 34;

            _appleWebSignInContainer = new NSView(containerBounds)
            {
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
                WantsLayer = true,
            };

            var isFlipped = _appleWebSignInContainer.IsFlipped;
            var barY = isFlipped ? 0 : containerBounds.Height - barHeight;
            var webY = isFlipped ? barHeight : 0;
            var webHeight = Math.Max(100, containerBounds.Height - barHeight);

            var topBar = new NSView(new CGRect(0, barY, containerBounds.Width, barHeight))
            {
                AutoresizingMask = NSViewResizingMask.WidthSizable
                    | (isFlipped ? NSViewResizingMask.MaxYMargin : NSViewResizingMask.MinYMargin),
                WantsLayer = true,
            };
            topBar.Layer!.BackgroundColor = NSColor.ControlBackground.CGColor;

            _urlStatusLabel = new NSTextField(new CGRect(14, 7, Math.Max(120, containerBounds.Width - 260), 18))
            {
                Editable = false,
                Bordered = false,
                DrawsBackground = false,
                StringValue = "Loading Apple Developer Sign In (developer.apple.com)...",
                Font = NSFont.SystemFontOfSize(11.5f),
                TextColor = NSColor.SecondaryLabel,
                LineBreakMode = NSLineBreakMode.TruncatingMiddle,
                AutoresizingMask = NSViewResizingMask.WidthSizable,
            };

            var legacyFormButton = NSButton.CreateButton("Password Form", () =>
            {
                _ = Bridge.RequestBackAsync();
            });
            legacyFormButton.BezelStyle = NSBezelStyle.AccessoryBarAction;
            legacyFormButton.ControlSize = NSControlSize.Small;
            legacyFormButton.Font = NSFont.SystemFontOfSize(11f);
            legacyFormButton.Frame = new CGRect(containerBounds.Width - 216, 5, 108, 24);
            legacyFormButton.AutoresizingMask = NSViewResizingMask.MinXMargin;

            var reloadButton = NSButton.CreateButton("Reload", () =>
            {
                _triedMyAcInfoAttempts.Clear();
                _appleWebView?.LoadRequest(new NSUrlRequest(new NSUrl(WebSignInEntryUrl)));
            });
            reloadButton.BezelStyle = NSBezelStyle.AccessoryBarAction;
            reloadButton.ControlSize = NSControlSize.Small;
            reloadButton.Font = NSFont.SystemFontOfSize(11f);
            reloadButton.Frame = new CGRect(containerBounds.Width - 100, 5, 88, 24);
            reloadButton.AutoresizingMask = NSViewResizingMask.MinXMargin;

            topBar.AddSubview(_urlStatusLabel);
            topBar.AddSubview(legacyFormButton);
            topBar.AddSubview(reloadButton);

            var config = new WKWebViewConfiguration
            {
                WebsiteDataStore = WKWebsiteDataStore.DefaultDataStore
            };

            _appleWebView = new WKWebView(new CGRect(0, webY, containerBounds.Width, webHeight), config)
            {
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
                CustomUserAgent = MauiSherpa.Core.Services.AppleDownloadAuthService.DefaultUserAgent,
                UIDelegate = new AppleSignInWebUIDelegate(),
                NavigationDelegate = new AppleSignInNavigationDelegate(this),
            };

            _appleWebSignInContainer.AddSubview(topBar);
            _appleWebSignInContainer.AddSubview(_appleWebView);

            blazorNative.Hidden = true;
            superview.AddSubview(_appleWebSignInContainer);

            _isWebSignInActive = true;
            _appleWebView.LoadRequest(new NSUrlRequest(new NSUrl(WebSignInEntryUrl)));

            Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
            {
                if (!_isWebSignInActive)
                    return false;

                _ = CheckForCompletedWebSignInAsync();
                return _isWebSignInActive;
            });
        });
    }

    private void StopWebSignIn()
    {
        if (Dispatcher.IsDispatchRequired)
            Dispatcher.Dispatch(() => TearDownWebViewOnMainThread(restoreSheetSize: true));
        else
            TearDownWebViewOnMainThread(restoreSheetSize: true);
    }

    private void TearDownWebViewOnMainThread(bool restoreSheetSize)
    {
        _isWebSignInActive = false;
        _isEvaluatingCookies = false;

        if (_appleWebView != null)
        {
            _appleWebView.StopLoading();
            _appleWebView.NavigationDelegate = null;
            _appleWebView.UIDelegate = null;
            _appleWebView.RemoveFromSuperview();
            _appleWebView.Dispose();
            _appleWebView = null;
        }

        if (_appleWebSignInContainer != null)
        {
            _appleWebSignInContainer.RemoveFromSuperview();
            _appleWebSignInContainer.Dispose();
            _appleWebSignInContainer = null;
        }

        _urlStatusLabel = null;

        if (FormWebView?.Handler?.PlatformView is NSView blazorNative)
            blazorNative.Hidden = false;

        if (restoreSheetSize)
        {
            MacOSPage.SetModalSheetWidth(this, DefaultWidth);
            MacOSPage.SetModalSheetHeight(this, DefaultHeight);
        }
    }

    private static async Task ClearShortLivedWebKitCookiesAsync(WKHttpCookieStore cookieStore)
    {
        var allCookies = await GetAllWebKitCookiesAsync(cookieStore);
        foreach (var cookie in allCookies)
        {
            if (string.Equals(cookie.Name, "myacinfo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cookie.Name, "dqsid", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cookie.Name, "itctx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cookie.Name, "itcdq", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cookie.Name, "ADCDownloadAuth", StringComparison.OrdinalIgnoreCase))
            {
                var tcs = new TaskCompletionSource();
                cookieStore.DeleteCookie(cookie, () => tcs.TrySetResult());
                await tcs.Task;
            }
        }
    }

    private static Task<NSHttpCookie[]> GetAllWebKitCookiesAsync(WKHttpCookieStore cookieStore)
    {
        var tcs = new TaskCompletionSource<NSHttpCookie[]>();
        cookieStore.GetAllCookies(cookies => tcs.TrySetResult(cookies ?? []));
        return tcs.Task;
    }

    private void UpdateUrlStatusOnMainThread(WKWebView webView)
    {
        if (_urlStatusLabel == null)
            return;

        var host = webView.Url?.Host;
        if (string.IsNullOrWhiteSpace(host))
            return;

        _urlStatusLabel.StringValue = webView.IsLoading
            ? $"Loading {host}..."
            : $"Apple Sign-In ({host}) — sign in and complete any prompt below";
    }

    private async Task CheckForCompletedWebSignInAsync()
    {
        if (!_isWebSignInActive || _isEvaluatingCookies || _appleWebView == null || _onCookiesCaptured == null)
            return;

        string? host = null;
        bool isLoading = true;
        Dispatcher.Dispatch(() =>
        {
            if (_appleWebView != null)
            {
                UpdateUrlStatusOnMainThread(_appleWebView);
                host = _appleWebView.Url?.Host;
                isLoading = _appleWebView.IsLoading;
            }
        });

        // Wait until Apple redirects off the sign-in/recovery hosts back to developer.apple.com or appstoreconnect.apple.com
        if (isLoading || string.IsNullOrWhiteSpace(host))
            return;

        if (!host.EndsWith("developer.apple.com", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith("appstoreconnect.apple.com", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _isEvaluatingCookies = true;
        try
        {
            var cookieStore = WKWebsiteDataStore.DefaultDataStore.HttpCookieStore;
            var nsCookies = await GetAllWebKitCookiesAsync(cookieStore);

            var myAcInfo = nsCookies.FirstOrDefault(c =>
                string.Equals(c.Name, "myacinfo", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(c.Value));

            if (myAcInfo == null)
                return;

            var attempts = _triedMyAcInfoAttempts.GetValueOrDefault(myAcInfo.Value, 0);
            if (attempts >= 3)
                return;

            _triedMyAcInfoAttempts[myAcInfo.Value] = attempts + 1;

            var appleCookies = new List<AppleAuthCookie>();
            foreach (var c in nsCookies)
            {
                if (string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Domain))
                    continue;

                if (!c.Domain.Contains("apple.com", StringComparison.OrdinalIgnoreCase))
                    continue;

                DateTime? expires = null;
                if (c.ExpiresDate != null)
                {
                    var seconds = (long)c.ExpiresDate.SecondsSince1970;
                    if (seconds > 0)
                        expires = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                }

                appleCookies.Add(new AppleAuthCookie(
                    Name: c.Name,
                    Value: c.Value ?? string.Empty,
                    Domain: c.Domain,
                    Path: string.IsNullOrWhiteSpace(c.Path) ? "/" : c.Path,
                    Expires: expires,
                    Secure: c.IsSecure,
                    HttpOnly: c.IsHttpOnly));
            }

            if (appleCookies.Count == 0)
                return;

            var accepted = await _onCookiesCaptured(appleCookies);
            if (accepted)
                StopWebSignIn();
        }
        finally
        {
            _isEvaluatingCookies = false;
        }
    }

    private sealed class AppleSignInWebUIDelegate : WKUIDelegate
    {
        public override WKWebView? CreateWebView(
            WKWebView webView,
            WKWebViewConfiguration configuration,
            WKNavigationAction navigationAction,
            WKWindowFeatures windowFeatures)
        {
            if (navigationAction.TargetFrame == null && navigationAction.Request != null)
                webView.LoadRequest(navigationAction.Request);

            return null;
        }
    }

    private sealed class AppleSignInNavigationDelegate : WKNavigationDelegate
    {
        private readonly XcodeDownloadAuthPage _owner;

        public AppleSignInNavigationDelegate(XcodeDownloadAuthPage owner) => _owner = owner;

        public override void DidStartProvisionalNavigation(WKWebView webView, WKNavigation navigation) =>
            _owner.UpdateUrlStatusOnMainThread(webView);

        public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
        {
            _owner.UpdateUrlStatusOnMainThread(webView);
            _ = _owner.CheckForCompletedWebSignInAsync();
        }
    }
#endif
}
