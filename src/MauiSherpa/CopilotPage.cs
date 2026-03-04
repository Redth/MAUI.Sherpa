using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS.Controls;
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
    public CopilotPage(
        ICopilotContextService contextService,
        ICopilotService copilotService,
        IServiceProvider serviceProvider)
    {
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
