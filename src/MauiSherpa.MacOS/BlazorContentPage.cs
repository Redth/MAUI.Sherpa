using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform.MacOS.Controls;
using MauiSherpa.Core.Interfaces;
using AppKit;
using CoreGraphics;
using Foundation;
using WebKit;

namespace MauiSherpa;

/// <summary>
/// ContentPage hosting a MacOSBlazorWebView for the detail area of the FlyoutPage.
/// Provides methods to navigate the Blazor router from native code.
/// </summary>
public class BlazorContentPage : ContentPage
{
    private readonly MacOSBlazorWebView _blazorWebView;
    private readonly IToolbarService _toolbarService;
    private readonly ISplashService _splashService;
    private AppKit.NSView? _loadingOverlay;
    private string _pendingRoute = "/";

    public BlazorContentPage(IServiceProvider serviceProvider)
    {
        Title = "";

        _toolbarService = serviceProvider.GetRequiredService<IToolbarService>();
        _toolbarService.ToolbarChanged += OnToolbarChanged;

        _splashService = serviceProvider.GetRequiredService<ISplashService>();
        _splashService.OnBlazorReady += OnBlazorReady;

        _blazorWebView = new MacOSBlazorWebView
        {
            HostPage = "wwwroot/index.html",
        };
        _blazorWebView.RootComponents.Add(new BlazorRootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.App),
        });

        Content = _blazorWebView;

        // Add overlay as soon as the native view is connected
        _blazorWebView.HandlerChanged += (s, e) =>
        {
            if (_blazorWebView.Handler != null)
            {
                AddNativeLoadingOverlay();
                MakeWebViewTransparent();
            }
        };

        // Safety timeout for loading overlay
        Dispatcher.StartTimer(TimeSpan.FromSeconds(15), () =>
        {
            if (_loadingOverlay != null)
                HideSplash();
            return false;
        });
    }

    /// <summary>
    /// Add a native NSView overlay on top of the window content once the handler is connected.
    /// This avoids wrapping BlazorWebView in a Grid which breaks safe area layout.
    /// </summary>
    private void AddNativeLoadingOverlay()
    {
        if (_blazorWebView.Handler?.PlatformView is not AppKit.NSView webViewNative) return;
        var superview = webViewNative.Superview ?? webViewNative;

        var bgColor = AppKit.NSColor.WindowBackground;

        _loadingOverlay = new AppKit.NSView(superview.Bounds)
        {
            AutoresizingMask = AppKit.NSViewResizingMask.WidthSizable | AppKit.NSViewResizingMask.HeightSizable,
            WantsLayer = true,
        };
        _loadingOverlay.Layer!.BackgroundColor = bgColor.CGColor;

        // Add a native spinner
        var spinner = new AppKit.NSProgressIndicator(new CoreGraphics.CGRect(0, 0, 24, 24))
        {
            Style = AppKit.NSProgressIndicatorStyle.Spinning,
            IsDisplayedWhenStopped = false,
            ControlSize = AppKit.NSControlSize.Small,
        };
        spinner.StartAnimation(null);
        spinner.TranslatesAutoresizingMaskIntoConstraints = false;
        _loadingOverlay.AddSubview(spinner);
        spinner.CenterXAnchor.ConstraintEqualTo(_loadingOverlay.CenterXAnchor).Active = true;
        spinner.CenterYAnchor.ConstraintEqualTo(_loadingOverlay.CenterYAnchor).Active = true;

        superview.AddSubview(_loadingOverlay, AppKit.NSWindowOrderingMode.Above, webViewNative);
    }

    /// <summary>
    /// Make the WKWebView transparent so the native macOS window background shows through.
    /// </summary>
    private void MakeWebViewTransparent()
    {
        if (_blazorWebView.Handler?.PlatformView is not WebKit.WKWebView webView) return;

        // WKWebView draws an opaque background by default.
        // Use KVC to disable it so the native window background is visible.
        webView.SetValueForKey(NSObject.FromObject(false), new NSString("drawsBackground"));
    }

    private void OnBlazorReady()
    {
        Dispatcher.Dispatch(() => HideSplash());
    }

    private void HideSplash()
    {
        if (_loadingOverlay == null) return;
        NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            // Animate opacity to 0 using CoreAnimation
            CoreAnimation.CATransaction.Begin();
            CoreAnimation.CATransaction.AnimationDuration = 0.3;
            CoreAnimation.CATransaction.CompletionBlock = () =>
            {
                _loadingOverlay?.RemoveFromSuperview();
                _loadingOverlay = null;
            };
            _loadingOverlay!.Layer!.Opacity = 0;
            CoreAnimation.CATransaction.Commit();
        });
    }

    /// <summary>
    /// Navigate the Blazor router to the specified route.
    /// Uses Blazor.navigateTo() JS interop.
    /// </summary>
    public void NavigateToRoute(string route)
    {
        _pendingRoute = route;

        // Use Dispatcher instead of MainThread (Essentials may not be ready during ConnectHandler)
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                var js = $"Blazor.navigateTo('{route}')";
                await EvaluateJavaScriptAsync(js);
            }
            catch
            {
                // Blazor may not be ready yet; store pending route
            }
        });
    }

    async Task EvaluateJavaScriptAsync(string js)
    {
        // The MacOSBlazorWebView exposes the underlying WKWebView.
        // We use the handler's platform view to evaluate JS.
        if (_blazorWebView.Handler?.PlatformView is WebKit.WKWebView webView)
        {
            await webView.EvaluateJavaScriptAsync(js);
        }
    }

    void OnToolbarChanged()
    {
        Dispatcher.Dispatch(() =>
        {
            ToolbarItems.Clear();
            foreach (var action in _toolbarService.CurrentItems)
            {
                var item = new ToolbarItem
                {
                    Text = action.Label,
                    Order = action.IsPrimary ? ToolbarItemOrder.Primary : ToolbarItemOrder.Secondary,
                    Command = new Command(() => _toolbarService.InvokeToolbarItemClicked(action.Id)),
                };
                // Use SF Symbol name as IconImageSource — the toolbar manager handles native rendering
                if (!string.IsNullOrEmpty(action.SfSymbol))
                    item.IconImageSource = action.SfSymbol;
                ToolbarItems.Add(item);
            }

            // Rewire the sidebar toggle to Copilot after toolbar rebuilds
            Dispatcher.Dispatch(RewireSidebarToggleToCopilot);
        });
    }

    void RewireSidebarToggleToCopilot()
    {
        try
        {
            var nsWindow = this.Window?.Handler?.PlatformView as NSWindow;
            var toolbar = nsWindow?.Toolbar;
            if (toolbar == null) return;

            foreach (var nsItem in toolbar.Items)
            {
                if (nsItem.Identifier == "MauiSidebarToggle" && nsItem.View is NSButton toggleBtn)
                {
                    var copilotImage = NSImage.GetSystemSymbol("sparkles", null);
                    if (copilotImage != null)
                    {
                        toggleBtn.Image = copilotImage;
                        toggleBtn.ImagePosition = NSCellImagePosition.ImageOnly;
                        toggleBtn.Title = "";
                    }
                    else
                    {
                        toggleBtn.Title = "✦";
                    }
                    toggleBtn.ToolTip = "Copilot";

                    toggleBtn.Target = null;
                    toggleBtn.Action = null;
                    toggleBtn.Activated += (s, e) =>
                    {
                        Dispatcher.Dispatch(async () =>
                        {
                            try { await EvaluateJavaScriptAsync("document.querySelector('.copilot-fab')?.click()"); }
                            catch { }
                        });
                    };
                    nsItem.Label = "Copilot";
                    nsItem.ToolTip = "Open Copilot";
                    break;
                }
            }
        }
        catch { }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Delay to let the toolbar handler create NSToolbar items first
        Dispatcher.Dispatch(() => Dispatcher.Dispatch(RewireSidebarToggleToCopilot));

        // Disable right-click context menu in the webview
        Dispatcher.Dispatch(async () =>
        {
            try { await EvaluateJavaScriptAsync("document.addEventListener('contextmenu', e => e.preventDefault())"); }
            catch { }
        });

        // Add a transparent drag overlay so the content titlebar area is draggable.
        // The WKWebView extends behind the toolbar due to FullSizeContentView and
        // intercepts mouse events that should initiate window drag.
        Dispatcher.Dispatch(AddTitlebarDragOverlay);
    }

    private TitlebarDragView? _dragOverlay;

    void AddTitlebarDragOverlay()
    {
        if (_dragOverlay != null) return;
        if (_blazorWebView.Handler?.PlatformView is not NSView webViewNative) return;

        var nsWindow = this.Window?.Handler?.PlatformView as NSWindow;
        if (nsWindow == null) return;

        // ContentLayoutRect is the usable area below the toolbar.
        // Everything above it is titlebar/toolbar space.
        var contentLayoutRect = nsWindow.ContentLayoutRect;
        var windowFrame = nsWindow.Frame;
        var titlebarHeight = windowFrame.Height - (contentLayoutRect.GetMaxY());
        if (titlebarHeight < 20) titlebarHeight = 52;

        // Place the overlay directly on top of the WebView's container.
        // This ensures it sits above the WKWebView in the hit-test order.
        var container = webViewNative.Superview ?? webViewNative;
        CGRect frame;
        if (container.IsFlipped)
            frame = new CGRect(0, 0, container.Bounds.Width, titlebarHeight);
        else
            frame = new CGRect(0, container.Bounds.Height - titlebarHeight, container.Bounds.Width, titlebarHeight);

        _dragOverlay = new TitlebarDragView(frame)
        {
            AutoresizingMask = NSViewResizingMask.WidthSizable
                | (container.IsFlipped ? NSViewResizingMask.MaxYMargin : NSViewResizingMask.MinYMargin),
        };
        container.AddSubview(_dragOverlay, NSWindowOrderingMode.Above, webViewNative);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _toolbarService.ToolbarChanged -= OnToolbarChanged;
        _splashService.OnBlazorReady -= OnBlazorReady;
    }
}

/// <summary>
/// Transparent NSView placed over the WebView in the titlebar zone.
/// Initiates window drag on mouseDown so the user can drag the window
/// from the empty toolbar space, even though a WKWebView sits underneath.
/// Overrides hitTest to ensure it captures mouse events before the WebView.
/// </summary>
class TitlebarDragView : NSView
{
    public TitlebarDragView(CGRect frame) : base(frame) { }

    public override NSView? HitTest(CGPoint point)
    {
        // point is in superview coordinates — check if it falls within our frame
        if (!IsHiddenOrHasHiddenAncestor && Frame.Contains(point))
            return this;
        return null;
    }

    public override void MouseDown(NSEvent theEvent)
    {
        Window?.PerformWindowDrag(theEvent);
    }

    public override bool MouseDownCanMoveWindow => true;
}
