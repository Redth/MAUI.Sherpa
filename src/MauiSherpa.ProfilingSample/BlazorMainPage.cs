using Microsoft.AspNetCore.Components.WebView.Maui;

namespace MauiSherpa.ProfilingSample;

public sealed class BlazorMainPage : ContentPage
{
    public BlazorMainPage()
    {
        var blazorWebView = new BlazorWebView
        {
            HostPage = "wwwroot/index.html"
        };

        blazorWebView.RootComponents.Add(new RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.App)
        });

        Content = blazorWebView;
    }
}
