using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Platform;
using Microsoft.Maui.Platforms.MacOS.Controls;

namespace MauiSherpa;

/// <summary>
/// A ContentPage that hosts a MacOSBlazorWebView navigated to a specific route.
/// Used for inspector windows (Android device, iOS simulator).
/// </summary>
public class InspectorPage : ContentPage
{
    public InspectorPage(string startPath, string title, Type? rootComponentType = null)
    {
        Title = title;

        var blazorWebView = new MacOSBlazorWebView
        {
            HostPage = "wwwroot/index.html",
            StartPath = startPath,
        };
        blazorWebView.RootComponents.Add(new BlazorRootComponent
        {
            Selector = "#app",
            ComponentType = rootComponentType ?? typeof(Components.App)
        });
        Content = blazorWebView;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Use the MacOSWindow attached property to keep content below the native titlebar
        if (this.Window is Window window)
        {
            MacOSWindow.SetFullSizeContentView(window, false);
        }
    }
}
