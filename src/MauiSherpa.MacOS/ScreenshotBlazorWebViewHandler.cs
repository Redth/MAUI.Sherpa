using CoreGraphics;
using Foundation;
using Microsoft.Maui.Platforms.MacOS.Handlers;
using WebKit;

namespace MauiSherpa;

internal sealed class ScreenshotBlazorWebViewHandler : BlazorWebViewHandler
{
    protected override WKWebView CreatePlatformView()
    {
        // Preserve the framework's scheme and interop wiring, but not its persistent data store.
        using var template = base.CreatePlatformView();
        using var configuration = template.Configuration;
        configuration.WebsiteDataStore = WKWebsiteDataStore.NonPersistentDataStore;
        var view = new WKWebView(CGRect.Empty, configuration);
        view.SetValueForKey(NSObject.FromObject(false), new NSString("drawsBackground"));
        return view;
    }
}
