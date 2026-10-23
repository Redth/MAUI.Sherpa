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
    private Func<IReadOnlyList<AppleAuthCookie>, Task<bool>>? _onCookiesCaptured;
    private bool _isWebSignInActive;
    private bool _isEvaluatingCookies;
    private readonly Dictionary<string, int> _triedMyAcInfoAttempts = new(StringComparer.Ordinal);

    private const string CleanSignInLayoutScript = """
        (function() {
            var style = document.createElement('style');
            style.textContent = `
                #ac-globalfooter, footer { display: none !important; }
                html, body {
                    overflow-x: hidden !important;
                    margin: 0 !important;
                    padding: 0 !important;
                }
                body:has(#auth-container), html:has(#auth-container) {
                    overflow: hidden !important;
                    height: 100% !important;
                }
                main.widget-v2, .wrapper, .wrapper.window, #auth-container, .si-frame {
                    height: 100% !important;
                    min-height: 100% !important;
                    max-height: 100% !important;
                    margin: 0 !important;
                    padding: 0 !important;
                    overflow: hidden !important;
                }
                #auth-container iframe, #aid-auth-widget-iFrame {
                    height: 100% !important;
                    min-height: 100% !important;
                    max-height: 100% !important;
                    border: 0 !important;
                }
            `;
            (document.head || document.documentElement).appendChild(style);
        })();
        """;
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
        bridge.Parameters["ReloadWebSignIn"] = new Action(ReloadWebSignIn);
        bridge.CancelRequested += StopWebSignIn;
        bridge.CloseRequested += StopWebSignIn;
        bridge.SetHeaderActions(
            new HybridHeaderAction("password-form", "key", "Sign in with Apple ID & password form", "⌨"),
            new HybridHeaderAction("reload-web", "arrow.clockwise", "Reload sign-in page", "↻"));
#else
        bridge.Parameters["SupportsWebSignIn"] = false;
#endif
    }

#if MACOSAPP
    private void ReloadWebSignIn()
    {
        Dispatcher.Dispatch(() =>
        {
            _triedMyAcInfoAttempts.Clear();
            _appleWebView?.LoadRequest(new NSUrlRequest(new NSUrl(WebSignInEntryUrl)));
        });
    }

    private Task StartWebSignInAsync(Func<IReadOnlyList<AppleAuthCookie>, Task<bool>> onCookiesCaptured)
    {
        _onCookiesCaptured = onCookiesCaptured;
        _triedMyAcInfoAttempts.Clear();

        Dispatcher.Dispatch(async () =>
        {
            var cookieStore = WKWebsiteDataStore.DefaultDataStore.HttpCookieStore;
            await ClearShortLivedWebKitCookiesAsync(cookieStore);

            if (FormWebView?.Handler?.PlatformView is not NSView blazorNative)
                return;

            TearDownWebViewOnMainThread(restoreSheetSize: false);

            MacOSPage.SetModalSheetWidth(this, DefaultWidth);
            MacOSPage.SetModalSheetHeight(this, DefaultHeight);

            FormWebView.Opacity = 1;
            blazorNative.Hidden = false;
            blazorNative.AlphaValue = 1;

            var containerBounds = blazorNative.Bounds;
            if (containerBounds.Width <= 0 || containerBounds.Height <= 0)
                containerBounds = new CGRect(0, 0, DefaultWidth, DefaultHeight - 125);

            _appleWebSignInContainer = new NSView(containerBounds)
            {
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
                WantsLayer = true,
            };
            _appleWebSignInContainer.Layer!.BackgroundColor = NSColor.WindowBackground.CGColor;

            var userContentController = new WKUserContentController();
            userContentController.AddUserScript(new WKUserScript(
                new NSString(CleanSignInLayoutScript),
                WKUserScriptInjectionTime.AtDocumentEnd,
                isForMainFrameOnly: false));

            var config = new WKWebViewConfiguration
            {
                WebsiteDataStore = WKWebsiteDataStore.DefaultDataStore,
                UserContentController = userContentController,
            };

            _appleWebView = new WKWebView(new CGRect(0, 0, containerBounds.Width, containerBounds.Height), config)
            {
                AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable,
                UIDelegate = new AppleSignInWebUIDelegate(),
                NavigationDelegate = new AppleSignInNavigationDelegate(this),
            };

            _appleWebSignInContainer.AddSubview(_appleWebView);
            blazorNative.AddSubview(_appleWebSignInContainer);

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

        return Task.CompletedTask;
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

        if (FormWebView != null)
            FormWebView.Opacity = 1;

        if (FormWebView?.Handler?.PlatformView is NSView blazorNative)
        {
            blazorNative.Hidden = false;
            blazorNative.AlphaValue = 1;
        }

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

    private async Task CheckForCompletedWebSignInAsync()
    {
        if (!_isWebSignInActive || _isEvaluatingCookies || _appleWebView == null || _onCookiesCaptured == null)
            return;

        // Called from Dispatcher.StartTimer and WKNavigationDelegate (both on the main thread).
        var host = _appleWebView.Url?.Host ?? string.Empty;

        _isEvaluatingCookies = true;
        try
        {
            var cookieStore = WKWebsiteDataStore.DefaultDataStore.HttpCookieStore;
            var accepted = await TryCaptureWebKitCookiesFromStoreAsync(cookieStore, keyPrefix: host);
            if (accepted)
                StopWebSignIn();
        }
        finally
        {
            _isEvaluatingCookies = false;
        }
    }

    private async Task<bool> TryCaptureWebKitCookiesFromStoreAsync(WKHttpCookieStore cookieStore, string keyPrefix)
    {
        if (_onCookiesCaptured == null)
            return false;

        var nsCookies = await GetAllWebKitCookiesAsync(cookieStore);

        var myAcInfo = nsCookies.FirstOrDefault(c =>
            string.Equals(c.Name, "myacinfo", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(c.Value));

        if (myAcInfo == null)
            return false;

        var attemptKey = $"{keyPrefix}|{myAcInfo.Value}";
        var attempts = _triedMyAcInfoAttempts.GetValueOrDefault(attemptKey, 0);
        if (attempts >= 2)
            return false;

        _triedMyAcInfoAttempts[attemptKey] = attempts + 1;

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
            return false;

        return await _onCookiesCaptured(appleCookies);
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

        public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
        {
            _ = _owner.CheckForCompletedWebSignInAsync();
        }
    }
#endif
}
