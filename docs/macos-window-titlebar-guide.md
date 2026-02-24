# macOS Window & Titlebar Guide

How we configure native macOS (AppKit) windows so the titlebar/toolbar is draggable and BlazorWebView content is properly inset.

## Background

The `Platform.Maui.MacOS` backend renders MAUI pages inside `NSWindow`. By default, the framework applies `NSWindowStyle.FullSizeContentView`, which lets the web content extend beneath the titlebar — the same "edge-to-edge" look used on iOS. This causes two problems on macOS:

1. **The titlebar area is not draggable** — the WebView's hit-testing intercepts mouse events.
2. **Content overlaps the titlebar** — text and controls render behind the translucent title area.

## Main Window (FlyoutPage + NavigationPage)

The main window uses a `FlyoutPage` (native `NSSplitView` sidebar) with a `NavigationPage` wrapping the `BlazorContentPage`:

```csharp
// MacOSApp.cs
var flyoutPage = new FlyoutPage
{
    Detail = new NavigationPage(blazorPage),  // ← NavigationPage adds a native toolbar area
    Flyout = new ContentPage { Title = "MAUI Sherpa" },
    FlyoutLayoutBehavior = FlyoutLayoutBehavior.Split,
};
```

**Key insight:** Wrapping the detail page in `NavigationPage` gives us a native `NSToolbar` in the titlebar area. This:
- Provides a proper draggable titlebar region
- Gives us native toolbar buttons (Refresh, Add, Settings, etc.)
- Pushes the BlazorWebView content below the toolbar automatically

### Toolbar Items

Toolbar buttons are driven by `IToolbarService` — each Blazor page registers its actions (with SF Symbol names), and `BlazorContentPage.OnToolbarChanged()` maps them to MAUI `ToolbarItem` objects. A follow-up `ApplySfSymbolIcons()` call patches the native `NSToolbar` buttons with SF Symbol images:

```csharp
// BlazorContentPage.cs
void ApplySfSymbolIcons()
{
    var nsWindow = this.Window?.Handler?.PlatformView as NSWindow;
    var toolbar = nsWindow?.Toolbar;
    // ... iterate toolbar.Items, set NSButton.Image from NSImage.GetSystemSymbol()
}
```

### Sidebar Toggle → Copilot Button

The `FlyoutPage` handler creates a `MauiSidebarToggle` toolbar button. We repurpose it as a Copilot button by replacing its icon and click handler:

```csharp
if (nsItem.Identifier == "MauiSidebarToggle" && nsItem.View is NSButton toggleBtn)
{
    toggleBtn.Image = NSImage.GetSystemSymbol("sparkles", null);
    toggleBtn.Activated += (s, e) => { /* toggle Copilot overlay via JS */ };
}
```

## Inspector Windows (Secondary Windows)

Inspector windows (Android device inspector, iOS simulator inspector) are standalone `NSWindow` instances, each hosting their own `MacOSBlazorWebView`. These do **not** use `NavigationPage` or `FlyoutPage` — they are simple `ContentPage` subclasses.

### The Problem

Without `NavigationPage`, there is no native toolbar. The `FullSizeContentView` style makes the WebView fill the entire window, including the titlebar area. The titlebar text overlaps WebView content, and the window cannot be dragged.

### The Fix: Remove FullSizeContentView

In `OnAppearing()`, we strip the `FullSizeContentView` flag from the window's `StyleMask`. This tells macOS to position the content view **below** the titlebar instead of underneath it:

```csharp
// InspectorPage.cs
protected override void OnAppearing()
{
    base.OnAppearing();
    Dispatcher.Dispatch(() =>
    {
        var nsWindow = NSApplication.SharedApplication.KeyWindow;
        if (nsWindow == null) return;
        nsWindow.StyleMask &= ~NSWindowStyle.FullSizeContentView;
    });
}
```

**Why `Dispatcher.Dispatch`?** The `NSWindow` may not be fully configured when `OnAppearing` fires. Dispatching defers the style change until the next run-loop tick, after the window handler has finished setup.

**Why not `NavigationPage`?** The macOS backend does not fully support `NavigationPage` for secondary windows — wrapping in `NavigationPage` resulted in a blank window with no content rendered.

## Splash / Loading Overlay

The `BlazorContentPage` adds a native `NSView` overlay (with a spinner) on top of the WebView while Blazor initializes. This avoids wrapping the `BlazorWebView` in a MAUI `Grid`, which was found to **break safe area layout** — the content would not properly respect the toolbar inset.

```csharp
// BlazorContentPage.cs — overlay sits on top of the native NSView hierarchy
private void AddNativeLoadingOverlay()
{
    if (_blazorWebView.Handler?.PlatformView is not AppKit.NSView webViewNative) return;
    var superview = webViewNative.Superview ?? webViewNative;

    _loadingOverlay = new AppKit.NSView(superview.Bounds) { /* ... */ };
    superview.AddSubview(_loadingOverlay, NSWindowOrderingMode.Above, webViewNative);
}
```

The overlay fades out with a `CATransaction` animation once `ISplashService.OnBlazorReady` fires (or after a 15-second safety timeout).

## HTML Viewport

The `index.html` uses `viewport-fit=cover` which enables safe area insets:

```html
<meta name="viewport" content="width=device-width, initial-scale=1.0,
      maximum-scale=1.0, user-scalable=no, viewport-fit=cover" />
```

No macOS-specific CSS padding or safe-area overrides are needed — the native window layout handles content inset.

## Gotchas & Lessons Learned

| Issue | Cause | Fix |
|-------|-------|-----|
| Can't drag titlebar | `FullSizeContentView` lets WebView cover titlebar | Remove the flag from `StyleMask`, or use `NavigationPage` |
| Content behind titlebar | Same as above | Same fix |
| Wrapping in Grid breaks inset | MAUI Grid doesn't propagate safe area to WebView | Use native `NSView` overlay instead of MAUI layout containers |
| NavigationPage blank in secondary windows | Not fully implemented in macOS backend | Use `ContentPage` + `StyleMask` fix instead |
| `TitlebarAppearsTransparent` causes overlap | Title text renders on top of content | Don't use it; let the standard opaque titlebar render |
| `MainThread` crashes | Portable stub, not implemented on macOS AppKit | Use `Dispatcher` / `IDispatcher` or `NSApplication.SharedApplication.BeginInvokeOnMainThread()` |
| Toolbar icons need post-processing | MAUI `ToolbarItem` doesn't support SF Symbols directly | Patch `NSToolbar.Items` after MAUI creates them |

## Summary

| Window Type | Page Structure | Titlebar/Toolbar | Content Inset |
|-------------|---------------|-----------------|---------------|
| Main window | `FlyoutPage` → `NavigationPage` → `BlazorContentPage` | Native `NSToolbar` from `NavigationPage` | Automatic (below toolbar) |
| Inspector windows | `InspectorPage` (plain `ContentPage`) | Standard titlebar, no toolbar | `StyleMask &= ~FullSizeContentView` |
