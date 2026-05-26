using Microsoft.AspNetCore.Components.WebView.Maui;

namespace MauiSherpa;

/// <summary>
/// A ContentPage that hosts a BlazorWebView navigated to a specific route.
/// Used for inspector windows (Android device, iOS simulator).
/// </summary>
public class InspectorPage : ContentPage
{
    public InspectorPage(string startPath, string title, Type? rootComponentType = null)
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
            ComponentType = rootComponentType ?? typeof(Components.App)
        });
        Content = blazorWebView;
    }
}
