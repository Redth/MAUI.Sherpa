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
    private string _pendingRoute = "/";

    public BlazorContentPage(IServiceProvider serviceProvider)
    {
        Title = "MAUI Sherpa";

        _toolbarService = serviceProvider.GetRequiredService<IToolbarService>();
        _toolbarService.ToolbarChanged += OnToolbarChanged;

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

            var actions = _toolbarService.CurrentItems;
            int actionIndex = 0;

            foreach (var nsItem in toolbar.Items)
            {
                // Skip non-toolbar-item entries (sidebar toggle, title, flexible space, back button)
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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _toolbarService.ToolbarChanged -= OnToolbarChanged;
    }
}
