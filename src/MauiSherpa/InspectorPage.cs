using Microsoft.AspNetCore.Components.WebView.Maui;

namespace MauiSherpa;

/// <summary>
/// A ContentPage that hosts a BlazorWebView navigated to a specific route.
/// Used for inspector windows (Android device, iOS simulator).
/// </summary>
public class InspectorPage : ContentPage
{
    public InspectorPage(string startPath, string title)
    {
        Title = title;

        var blazorWebView = new BlazorWebView
        {
            HostPage = "wwwroot/index.html",
            StartPath = startPath,
        };
        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.App)
        });
        Content = blazorWebView;
    }
}
