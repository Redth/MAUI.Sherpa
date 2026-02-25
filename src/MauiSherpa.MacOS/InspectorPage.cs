using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform.MacOS;
using Microsoft.Maui.Platform.MacOS.Controls;

namespace MauiSherpa;

/// <summary>
/// A ContentPage that hosts a MacOSBlazorWebView navigated to a specific route.
/// Used for inspector windows (Android device, iOS simulator).
/// </summary>
public class InspectorPage : ContentPage
{
    public InspectorPage(string startPath, string title)
    {
        Console.Error.WriteLine($"[InspectorPage] ctor START — path={startPath}, title={title}");
        Console.Error.Flush();
        try
        {
            Title = title;

            var blazorWebView = new MacOSBlazorWebView
            {
                HostPage = "wwwroot/index.html",
                StartPath = startPath,
            };
            Console.Error.WriteLine("[InspectorPage] MacOSBlazorWebView created");
            Console.Error.Flush();
            blazorWebView.RootComponents.Add(new BlazorRootComponent
            {
                Selector = "#app",
                ComponentType = typeof(Components.App)
            });
            Console.Error.WriteLine("[InspectorPage] RootComponent added");
            Console.Error.Flush();
            Content = blazorWebView;
            Console.Error.WriteLine("[InspectorPage] Content set — ctor DONE");
            Console.Error.Flush();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[InspectorPage] ctor CRASH: {ex}");
            Console.Error.Flush();
            throw;
        }
    }

    protected override void OnAppearing()
    {
        Console.Error.WriteLine("[InspectorPage] OnAppearing START");
        Console.Error.Flush();
        try
        {
            base.OnAppearing();
            // Use the MacOSWindow attached property to keep content below the native titlebar
            if (this.Window is Window window)
            {
                MacOSWindow.SetFullSizeContentView(window, false);
            }
            Console.Error.WriteLine("[InspectorPage] OnAppearing DONE");
            Console.Error.Flush();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[InspectorPage] OnAppearing CRASH: {ex}");
            Console.Error.Flush();
            throw;
        }
    }
}
