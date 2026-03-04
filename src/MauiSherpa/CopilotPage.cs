using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
using Microsoft.Maui.Platform.MacOS.Controls;
#elif LINUXGTK
using Platform.Maui.Linux.Gtk4.Platform;
#else
using Microsoft.AspNetCore.Components.WebView.Maui;
#endif

namespace MauiSherpa;

/// <summary>
/// A ContentPage hosting a dedicated BlazorWebView for the Copilot chat.
/// The page and its WebView are kept alive across show/hide cycles.
/// Presented modally via Navigation.PushModalAsync/PopModalAsync.
/// 
/// The entire UI (header, chat, input) is rendered by the Blazor Copilot component.
/// This page is just a thin shell around a full-screen BlazorWebView.
/// </summary>
public class CopilotPage : ContentPage
{
    private const double ModalPadding = 60;
    private const double MinModalWidth = 480;
    private const double MinModalHeight = 400;

    public CopilotPage(
        ICopilotContextService contextService,
        ICopilotService copilotService,
        IServiceProvider serviceProvider)
    {
        // --- Modal sizing: window minus padding, with minimums ---
        double winWidth = 0, winHeight = 0;

#if MACOSAPP
        var nsWindow = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as AppKit.NSWindow;
        if (nsWindow != null)
        {
            var frame = nsWindow.ContentLayoutRect;
            winWidth = frame.Width;
            winHeight = frame.Height;
        }
#else
        var window = Application.Current?.Windows.FirstOrDefault();
        winWidth = window?.Width ?? 0;
        winHeight = window?.Height ?? 0;
#endif

        if (winWidth <= 0 || winHeight <= 0 || double.IsNaN(winWidth) || double.IsNaN(winHeight))
        {
            winWidth = 900;
            winHeight = 700;
        }

        var modalWidth = Math.Max(winWidth - ModalPadding, MinModalWidth);
        var modalHeight = Math.Max(winHeight - ModalPadding, MinModalHeight);

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, modalWidth);
        MacOSPage.SetModalSheetHeight(this, modalHeight);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, (int)modalWidth);
        GtkPage.SetModalHeight(this, (int)modalHeight);
#endif

        // --- BlazorWebView for the full Copilot UI ---
        View webView;
#if MACOSAPP
        var blazorWebView = new MacOSBlazorWebView
        {
            HostPage = "wwwroot/index.html",
            StartPath = "/copilot-modal",
        };
        blazorWebView.RootComponents.Add(new BlazorRootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.CopilotModalApp)
        });
        webView = blazorWebView;
#elif LINUXGTK
        var blazorWebView = new Platform.Maui.Linux.Gtk4.BlazorWebView.BlazorWebView
        {
            HostPage = "wwwroot/index.html",
            StartPath = "/copilot-modal",
        };
        blazorWebView.RootComponents.Add(new Platform.Maui.Linux.Gtk4.BlazorWebView.BlazorRootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.CopilotModalApp)
        });
        webView = blazorWebView;
#else
        var blazorWebView = new BlazorWebView
        {
            HostPage = "wwwroot/index.html",
            StartPath = "/copilot-modal",
        };
        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.CopilotModalApp)
        });
        HandlerProperties.SetDisconnectPolicy(blazorWebView, HandlerDisconnectPolicy.Manual);
        webView = blazorWebView;
#endif

        // --- Page layout: full-screen BlazorWebView ---
        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
            },
        };
        layout.Add(webView, 0, 0);

        Content = layout;

#if MACCATALYST
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetUseSafeArea(this, false);
#endif
    }
}
