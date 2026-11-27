using MauiSherpa.Pages.Forms;
#if MACOSAPP
using PlatformBlazorWebView = Microsoft.Maui.Platforms.MacOS.Controls.MacOSBlazorWebView;
#else
using PlatformBlazorWebView = Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView;
#endif

namespace MauiSherpa;

public sealed class ThemedBlazorWebView : PlatformBlazorWebView
{
    public ThemedBlazorWebView(bool isModal = false)
    {
        this.SetDynamicResource(BackgroundColorProperty, isModal ? FormTheme.PageBg : FormTheme.WindowBg);
#if WINDOWS
        BlazorWebViewInitialized += (_, _) => UpdatePlatformBackground();
#endif
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        UpdateBackground();
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == BackgroundColorProperty.PropertyName)
            UpdateBackground();
    }

    private void UpdateBackground()
    {
        if (Handler == null)
            return;

        if (Dispatcher.IsDispatchRequired)
            Dispatcher.Dispatch(UpdatePlatformBackground);
        else
            UpdatePlatformBackground();
    }

    private void UpdatePlatformBackground()
    {
        if (BackgroundColor is not { } color)
            return;

        // Set the browser's blank-page color as well as its native container.
#if LINUXGTK
        if (Handler?.PlatformView is Gtk.Box box && box.GetFirstChild() is WebKit.WebView webView)
            webView.SetBackgroundColor(new Gdk.RGBA
            {
                Red = color.Red,
                Green = color.Green,
                Blue = color.Blue,
                Alpha = color.Alpha,
            });
#elif MACOSAPP
        if (Handler?.PlatformView is WebKit.WKWebView webView)
        {
            var nativeColor = AppKit.NSColor.FromRgba(color.Red, color.Green, color.Blue, color.Alpha);
            webView.UnderPageBackgroundColor = nativeColor;
            webView.WantsLayer = true;
            webView.Layer!.BackgroundColor = nativeColor.CGColor;
        }
#elif WINDOWS
        if (Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 webView)
            webView.DefaultBackgroundColor = Windows.UI.Color.FromArgb(
                (byte)Math.Round(color.Alpha * 255),
                (byte)Math.Round(color.Red * 255),
                (byte)Math.Round(color.Green * 255),
                (byte)Math.Round(color.Blue * 255));
#endif
    }
}
