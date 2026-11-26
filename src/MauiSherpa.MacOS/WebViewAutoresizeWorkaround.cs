using AppKit;
using CoreGraphics;
using Microsoft.Maui.Platforms.MacOS.Controls;

namespace MauiSherpa;

/// <summary>
/// Works around a Microsoft.Maui.Platforms.MacOS 0.1.0-preview.16 bug: MacOSViewHandler.UpdateGeometry
/// turns AutoresizesSubviews off while it applies a view's frame, so size changes never reach native
/// children. WKWebView sizes its content view (WKFlippedView, WidthSizable | HeightSizable) through
/// autoresizing, so the page renders into a 0×0 view and the content area stays blank even though
/// Blazor is running.
///
/// Fixed upstream in dotnet/maui-labs#930 ("Resize autoresizing children when an AppKit view's logical
/// size changes"). Remove this class once Sherpa references a maui-labs release that includes it.
/// </summary>
static class WebViewAutoresizeWorkaround
{
    public static void Attach(MacOSBlazorWebView webView)
    {
        var lastSize = CGSize.Empty;

        // SizeChanged fires before the handler applies the native frame; replay afterwards.
        webView.SizeChanged += (_, _) => webView.Dispatcher.Dispatch(() =>
        {
            if (webView.Handler?.PlatformView is not NSView platformView)
                return;

            var size = platformView.Bounds.Size;
            if (size == lastSize)
                return;

            ResizeFlexibleChildren(platformView, lastSize, size);
            lastSize = size;
        });
    }

    // Apply what AppKit autoresizing would have done to children with a flexible width or height.
    // NSView.ResizeSubviewsWithOldSize can't be used: AppKit ignores it when the old size is zero,
    // which is exactly the first-arrange case (the web view is created with an empty frame).
    static void ResizeFlexibleChildren(NSView parent, CGSize oldSize, CGSize newSize)
    {
        var deltaWidth = newSize.Width - oldSize.Width;
        var deltaHeight = newSize.Height - oldSize.Height;

        var flexibleChildren = parent.Subviews.Where(child =>
            child.TranslatesAutoresizingMaskIntoConstraints &&
            (child.AutoresizingMask.HasFlag(NSViewResizingMask.WidthSizable) ||
             child.AutoresizingMask.HasFlag(NSViewResizingMask.HeightSizable)));

        foreach (var child in flexibleChildren)
        {
            var frame = child.Frame;
            var width = child.AutoresizingMask.HasFlag(NSViewResizingMask.WidthSizable)
                ? Math.Max(0, frame.Width + deltaWidth)
                : frame.Width;
            var height = child.AutoresizingMask.HasFlag(NSViewResizingMask.HeightSizable)
                ? Math.Max(0, frame.Height + deltaHeight)
                : frame.Height;
            child.Frame = new CGRect(frame.X, frame.Y, width, height);
        }
    }
}
