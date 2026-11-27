using Microsoft.Maui.Controls;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
using Microsoft.Maui.Platforms.MacOS.Controls;
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
public class ProgressModalPage : ContentPage, INativeModalDismissible
{
    private bool _built;
    private HybridLoadingView? _loadingView;

    public void OnNativeDismissed() => _loadingView?.StopLoading();

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
#elif WINDOWS
        Platforms.Windows.WindowsModalPresentation.SetPreferredSize(this, minWidth, minHeight);
#endif
        BuildPage(route);
    }

    private void BuildPage(string route)
    {
        if (_built) return;
        _built = true;

#if MACOSAPP
        var blazorWebView = new ThemedBlazorWebView(isModal: true)
        {
            HostPage = "wwwroot/index.html",
            StartPath = route,
        };
        blazorWebView.RootComponents.Add(new BlazorRootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.ModalApp)
        });
#else
        var blazorWebView = new ThemedBlazorWebView(isModal: true)
        {
            HostPage = "wwwroot/index.html",
            StartPath = route,
        };
        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.ModalApp)
        });
        HandlerProperties.SetDisconnectPolicy(blazorWebView, HandlerDisconnectPolicy.Manual);
#endif
        _loadingView = new HybridLoadingView(blazorWebView);
        Content = _loadingView;
    }
}
