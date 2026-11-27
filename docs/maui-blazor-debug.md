---
name: maui-blazor-debugging
description: |
  Debug .NET MAUI Blazor Hybrid apps using browser developer tools. Use this skill when:
  (1) Debugging Blazor UI issues in a MAUI app, (2) Inspecting console logs/errors from BlazorWebView,
  (3) Debugging JavaScript interop issues, (4) Inspecting network requests from Blazor components,
  (5) Analyzing DOM structure in hybrid apps. Supports Mac Catalyst (Safari), Windows (Edge/Chrome),
  Android (Chrome), and iOS (Safari). Integrates with Playwright browser tools for automated inspection.
---

# MAUI Blazor Hybrid Debugging

Debug .NET MAUI Blazor Hybrid apps by connecting browser developer tools to the BlazorWebView.

## Initial backgrounds in Sherpa

Use `ThemedBlazorWebView` for hybrid content, with `isModal: true` for dialogs.
It binds the native container and browser's blank-page background to the effective
app theme before Blazor renders, and updates them when the theme changes. Keep
the unthemed `html`/`body` background in `wwwroot/index.html` transparent so it
does not cover that initial color. Splash overlays use the same window background
resource and theme-aware text colors.

Wrap secondary hybrid hosts in `HybridLoadingView` after adding their root
component. It shows a native, theme-aware spinner until that specific WebView's
Blazor content has painted, then fades the content in. Main windows retain their
existing splash/loading overlays.

## Prerequisites

Ensure the app has developer tools enabled in `MauiProgram.cs`:

```csharp
builder.Services.AddMauiBlazorWebView();

#if DEBUG
builder.Services.AddBlazorWebViewDeveloperTools();
builder.Logging.AddDebug();
#endif
```

## Debugging Workflow by Platform

### Mac Catalyst

1. Add debug entitlements to `Platforms/MacCatalyst/Entitlements.Debug.plist`:
   ```xml
   <?xml version="1.0" encoding="UTF-8"?>
   <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
   <plist version="1.0">
   <dict>
       <key>com.apple.security.get-task-allow</key>
       <true/>
   </dict>
   </plist>
   ```

2. Add to `.csproj`:
   ```xml
   <PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'maccatalyst' and '$(Configuration)' == 'Debug'">
       <CodeSignEntitlements>Platforms/MacCatalyst/Entitlements.Debug.plist</CodeSignEntitlements>
   </PropertyGroup>
   ```

3. Run the app, then in Safari: **Develop** → **[Device Name]** → **0.0.0.0** (select entry that highlights the BlazorWebView)

### Windows

1. Run the app and navigate to a page with BlazorWebView
2. Press `Ctrl+Shift+I` to open Edge DevTools

### Android

1. Run app in emulator/device
2. Open Chrome/Edge and navigate to `chrome://inspect/#devices` or `edge://inspect/#devices`
3. Click **inspect** next to the BlazorWebView entry

### iOS Simulator

1. Enable Safari developer menu: **Safari** → **Preferences** → **Advanced** → **Show Develop menu**
2. Run app in simulator
3. In Safari: **Develop** → **Simulator** → **0.0.0.0**

## Automated Debugging with Playwright

Use Playwright browser tools to programmatically inspect the BlazorWebView when remote debugging is enabled.

### Common Debugging Tasks

**Get console messages:**
```
playwright-browser_console_messages with level: "error" to see errors
playwright-browser_console_messages with level: "info" for all logs
```

**Take screenshot of current state:**
```
playwright-browser_take_screenshot with filename: "debug-state.png"
```

**Get page snapshot for accessibility/DOM analysis:**
```
playwright-browser_snapshot to see current DOM structure
```

**Inspect network requests:**
```
playwright-browser_network_requests to see API calls and responses
```

**Execute JavaScript to inspect Blazor state:**
```
playwright-browser_evaluate with function: "() => { return !!window.Blazor }"
```

## Debugging Patterns

### Console Log Analysis

1. Get error-level messages: `playwright-browser_console_messages` with `level: "error"`
2. Search for Blazor-specific errors (look for "blazor", "circuit", "render")
3. Check for JavaScript interop failures

### Component State Inspection

Evaluate JS to inspect Blazor component state:
```javascript
// Check if Blazor is initialized
() => !!window.Blazor

// Count Blazor components
() => document.querySelectorAll('[_bl_*]').length
```

### Network Debugging

1. Use `playwright-browser_network_requests` to capture HTTP traffic
2. Filter for failed requests (status >= 400)
3. Check for CORS issues in hybrid scenarios

### DOM Inspection

1. Use `playwright-browser_snapshot` to get accessible DOM tree
2. Look for missing elements or incorrect structure
3. Verify Blazor component rendering

## Troubleshooting

**DevTools won't connect:**
- Verify `AddBlazorWebViewDeveloperTools()` is called
- Check entitlements on macOS (must have `com.apple.security.get-task-allow`)
- Ensure app is on a BlazorWebView page, not a native ContentPage

**Console shows no output:**
- Add `builder.Logging.AddDebug()` to MauiProgram.cs
- Use `Console.WriteLine` or `ILogger` in Blazor components

**Blank WebView:**
- Check `wwwroot/index.html` exists and is set as MauiAsset
- Verify `HostPage` path in BlazorWebView configuration
- Look for JavaScript errors in console
