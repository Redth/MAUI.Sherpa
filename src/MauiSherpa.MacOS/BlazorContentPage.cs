using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform.MacOS.Controls;
using MauiSherpa.Core.Interfaces;
using AppKit;
using Foundation;

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
                AddNativeLoadingOverlay();
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
                ToolbarItems.Add(item);
            }

            // Post-process: add SF Symbol icons to the native NSToolbar buttons
            Dispatcher.Dispatch(ApplySfSymbolIcons);
        });
    }

    void ApplySfSymbolIcons()
    {
        try
        {
            var nsWindow = this.Window?.Handler?.PlatformView as NSWindow;
            var toolbar = nsWindow?.Toolbar;
            if (toolbar == null) return;

            // Replace the hamburger sidebar toggle with a Copilot button
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

                    // Rewire click to toggle Copilot overlay via JS
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

            // Apply SF Symbol icons to toolbar action items
            var actions = _toolbarService.CurrentItems;
            int actionIndex = 0;

            foreach (var nsItem in toolbar.Items)
            {
                if (!nsItem.Identifier.StartsWith("MauiToolbarItem_"))
                    continue;

                if (actionIndex < actions.Count)
                {
                    var action = actions[actionIndex];
                    if (nsItem.View is NSButton button && !string.IsNullOrEmpty(action.SfSymbol))
                    {
                        var image = NSImage.GetSystemSymbol(action.SfSymbol, null);
                        if (image != null)
                        {
                            button.Image = image;
                            button.ImagePosition = NSCellImagePosition.ImageOnly;
                            button.ToolTip = action.Label;
                            nsItem.Label = action.Label;
                        }
                    }
                    actionIndex++;
                }
            }
        }
        catch
        {
            // Toolbar may not be attached yet
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Delay to let the toolbar handler create NSToolbar items first
        Dispatcher.Dispatch(() => Dispatcher.Dispatch(ApplySfSymbolIcons));

        // Disable right-click context menu in the webview
        Dispatcher.Dispatch(async () =>
        {
            try { await EvaluateJavaScriptAsync("document.addEventListener('contextmenu', e => e.preventDefault())"); }
            catch { }
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _toolbarService.ToolbarChanged -= OnToolbarChanged;
        _splashService.OnBlazorReady -= OnBlazorReady;
    }
}
