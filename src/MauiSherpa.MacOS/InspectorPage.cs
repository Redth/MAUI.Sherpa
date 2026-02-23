using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform.MacOS.Controls;
using AppKit;

namespace MauiSherpa;

/// <summary>
/// A ContentPage that hosts a MacOSBlazorWebView navigated to a specific route.
/// Used for inspector windows (Android device, iOS simulator).
/// </summary>
public class InspectorPage : ContentPage
{
    public InspectorPage(string startPath, string title)
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
            ComponentType = typeof(Components.App)
        });
        Content = blazorWebView;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Remove FullSizeContentView so WebView sits below the native titlebar
        Dispatcher.Dispatch(() =>
        {
            var nsWindow = NSApplication.SharedApplication.KeyWindow;
            if (nsWindow == null) return;
            nsWindow.StyleMask &= ~NSWindowStyle.FullSizeContentView;
        });
    }
}
