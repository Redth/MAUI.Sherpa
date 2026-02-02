using Microsoft.AspNetCore.Components.WebView.Maui;

namespace MauiSherpa.Platform;

public class MainPage : ContentPage
{
    public MainPage()
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
