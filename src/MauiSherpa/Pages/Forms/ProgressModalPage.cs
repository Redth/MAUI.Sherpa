using Microsoft.Maui.Controls;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
using Microsoft.Maui.Platform.MacOS.Controls;
#else
using Microsoft.AspNetCore.Components.WebView.Maui;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Forms;

/// <summary>
/// A simple ContentPage that wraps a BlazorWebView navigating to a specific route.
/// Used for progress/operation modals where the entire UI (including buttons) is in Blazor.
/// The native modal sheet provides the frame; the BlazorWebView fills it.
/// </summary>
public class ProgressModalPage : ContentPage
{
    private bool _built;
    private View? _webView;

    public ProgressModalPage(string route, int minWidth = 600, int minHeight = 400)
    {
        this.SetDynamicResource(BackgroundColorProperty, FormTheme.PageBg);
#if MACOSAPP
        // Don't use SizesToContent — BlazorWebViews have no intrinsic height.
        // Use explicit dimensions instead.
        MacOSPage.SetModalSheetWidth(this, minWidth);
        MacOSPage.SetModalSheetHeight(this, minHeight);
#elif LINUXGTK
        GtkPage.SetModalWidth(this, minWidth);
        GtkPage.SetModalHeight(this, minHeight);
#endif
        BuildPage(route);
    }

    private void BuildPage(string route)
    {
        if (_built) return;
        _built = true;

        ModalWebViewReadySignal.Ready += OnBlazorReady;

#if MACOSAPP
        var blazorWebView = new MacOSBlazorWebView
        {
            HostPage = "wwwroot/index.html",
            StartPath = route,
            Opacity = 0,
        };
        blazorWebView.RootComponents.Add(new BlazorRootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.ModalApp)
        });
        _webView = blazorWebView;
        Content = blazorWebView;
#else
        var blazorWebView = new BlazorWebView
        {
            HostPage = "wwwroot/index.html",
            StartPath = route,
            Opacity = 0,
        };
        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.ModalApp)
        });
        HandlerProperties.SetDisconnectPolicy(blazorWebView, HandlerDisconnectPolicy.Manual);
        _webView = blazorWebView;
        Content = blazorWebView;
#endif
    }

    private void OnBlazorReady(double contentHeight)
    {
        ModalWebViewReadySignal.Ready -= OnBlazorReady;
        Dispatcher.Dispatch(async () =>
        {
            if (_webView != null)
                await _webView.FadeToAsync(1, 200, Easing.CubicIn);
        });
    }
}
