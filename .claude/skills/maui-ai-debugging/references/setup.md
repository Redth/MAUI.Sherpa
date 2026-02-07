# Setup & Installation

Complete guide for integrating MauiDevFlow into a .NET MAUI app.

## 1. Install CLI Tools

```bash
dotnet tool install --global Redth.MauiDevFlow.CLI    # maui-devflow
dotnet tool install --global androidsdk.tool           # android (Android only)
dotnet tool install --global appledev.tools            # apple (iOS/Mac only)
```

Verify: `maui-devflow --version`

## 2. Add NuGet Packages

Add to your MAUI app's `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Redth.MauiDevFlow.Agent" Version="*" />
  <!-- Blazor Hybrid apps also need: -->
  <PackageReference Include="Redth.MauiDevFlow.Blazor" Version="*" />
</ItemGroup>
```

- `Redth.MauiDevFlow.Agent` — Required for all MAUI apps. Provides the in-app agent
  for visual tree inspection, screenshots, tapping, filling text, etc.
- `Redth.MauiDevFlow.Blazor` — Required for Blazor Hybrid apps. Provides the CDP bridge
  for DOM inspection, JavaScript evaluation, and Blazor debugging.

## 3. Register in MauiProgram.cs

```csharp
using MauiDevFlow.Agent;
using MauiDevFlow.Blazor;  // Blazor Hybrid only

var builder = MauiApp.CreateBuilder();
// ... your existing setup ...

#if DEBUG
builder.Services.AddBlazorWebViewDeveloperTools();          // Blazor Hybrid only
builder.AddMauiDevFlowAgent();
builder.AddMauiBlazorDevFlowTools(); // Blazor Hybrid only
#endif
```

**Agent options:**
- `Port` — HTTP port for the agent REST API (default: 9223). Also configurable via `.mauidevflow` or `-p:MauiDevFlowPort=XXXX`.
- `Enabled` — Enable/disable the agent (default: true)
- `MaxTreeDepth` — Max depth for visual tree queries, 0 = unlimited (default: 0)

## 3b. Port Configuration (.mauidevflow)

Create a `.mauidevflow` file in the project directory to set a custom port. Pick a random
port between 9223–9899 to avoid collisions with other projects:

```json
{
  "port": 9347
}
```

Both the MSBuild targets and the CLI read this file automatically:
- **Build**: `dotnet build -t:Run` — agent starts on the configured port
- **CLI**: `maui-devflow MAUI status` — connects to the configured port (when run from project dir)

No `-p:MauiDevFlowPort` or `--agent-port` flags needed. This file should be committed to
source control so all developers and CI agents use the same port.

**Port priority:** Code-set `options.Port` > `-p:MauiDevFlowPort` > `.mauidevflow` > default 9223.

**Blazor options:**
- `Enabled` — Enable/disable CDP support (default: true)
- `EnableWebViewInspection` — Enable WebView inspection (default: true)
- `EnableLogging` — Log debug messages (default: true in DEBUG)

## 4. Blazor Hybrid: Add Script Tag to index.html

**This step is required for Blazor Hybrid apps.** The `Redth.MauiDevFlow.Blazor` NuGet package
delivers `chobitsu.js` automatically as a static web asset — no file copying or manual downloads
needed. You just need to add one script tag to `wwwroot/index.html`.

Add this line before `</body>`:

```html
<script src="chobitsu.js"></script>
```

Example:
```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <title>My App</title>
    <base href="/" />
    <link rel="stylesheet" href="css/app.css" />
</head>
<body>
    <div id="app"></div>
    <script src="_framework/blazor.webview.js"></script>
    <script src="chobitsu.js"></script>  <!-- ADD THIS LINE -->
</body>
</html>
```

### Why is this needed?

MAUI's `app://` URL scheme blocks dynamic `<script>` tag loading, and `chobitsu.js` uses
`eval`/`new Function` internally which Content Security Policy blocks when injected via
`EvaluateJavaScriptAsync`. A static `<script>` tag in the HTML is the only reliable approach.

### What if I forget?

The library checks at runtime and logs a clear error message:
```
[BlazorDevFlow] ❌ Missing required script tag in wwwroot/index.html.
[BlazorDevFlow] Add this before </body>:  <script src="chobitsu.js"></script>
```

### How the file gets there

The `chobitsu.js` file is included in the NuGet package as a static web asset. It is
automatically available at the root of your app's `wwwroot/` — no `.targets` file copying,
no manual downloads. It works in both Debug and Release builds (though MauiDevFlow itself
should only be referenced in Debug configurations).

## 5. Mac Catalyst: Entitlements

Mac Catalyst apps need the `com.apple.security.network.server` entitlement to allow the
agent and CDP servers to bind ports. Without this, the app will crash or fail silently.

### Option A: Sandbox disabled (simpler for development)

Create or update `Platforms/MacCatalyst/Entitlements.plist` for Debug builds:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>com.apple.security.app-sandbox</key>
    <false/>
    <key>com.apple.security.network.client</key>
    <true/>
  </dict>
</plist>
```

### Option B: Sandbox enabled (required for App Store)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>com.apple.security.app-sandbox</key>
    <true/>
    <key>com.apple.security.network.client</key>
    <true/>
    <key>com.apple.security.network.server</key>
    <true/>
  </dict>
</plist>
```

Reference in your `.csproj` (Debug only, so Release uses the default entitlements):

```xml
<PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)'))
    == 'maccatalyst' and '$(Configuration)' == 'Debug'">
  <CodeSignEntitlements>Platforms/MacCatalyst/Entitlements.Debug.plist</CodeSignEntitlements>
</PropertyGroup>
```

**Avoiding TCC permission dialogs:** Even with sandbox disabled, macOS prompts for access to
`~/Documents`, `~/Downloads`, `~/Desktop`, and dotfiles in `~/` on every rebuild (ad-hoc signing
changes the code signature each build). To avoid this, store app data in
`Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` (`~/Library/Application Support/`)
instead of the home directory root. This path is not TCC-protected.

## 6. Android: Port Forwarding

After deploying to an Android emulator, set up port forwarding so the CLI can reach the agent:

```bash
adb reverse tcp:9223 tcp:9223    # Agent + CDP (single port)
```

This is needed because the emulator runs in its own network namespace. Physical devices
connected via USB also need this. If using a custom port (via `.mauidevflow` or
`-p:MauiDevFlowPort=9347`), forward that port instead: `adb reverse tcp:9347 tcp:9347`.

## 7. Verify Setup

After building and running the app:

```bash
maui-devflow MAUI status          # Should show agent info, platform, app name
maui-devflow cdp status           # Should show "Connected" (Blazor Hybrid only)
```

If status commands fail:
- **Mac Catalyst:** Check entitlements (Step 5)
- **Android:** Check port forwarding (Step 6) — re-run `adb reverse` after each deploy
- **iOS Simulator:** Should work without extra config
- **All platforms:** Ensure the app is running and the `#if DEBUG` block is active
- **Port conflict:** Check if another process holds the port: `lsof -i :9223` (or your configured port)
- **Wrong port:** Ensure CLI is run from the project directory so it reads `.mauidevflow`

## Quick Checklist

For an AI agent setting up MauiDevFlow in a new project:

1. [ ] `Redth.MauiDevFlow.Agent` NuGet package added
2. [ ] `Redth.MauiDevFlow.Blazor` NuGet package added (Blazor Hybrid only)
3. [ ] `builder.AddMauiDevFlowAgent(...)` in MauiProgram.cs inside `#if DEBUG`
4. [ ] `builder.AddMauiBlazorDevFlowTools(...)` in MauiProgram.cs (Blazor Hybrid only)
5. [ ] `<script src="chobitsu.js"></script>` in index.html (Blazor Hybrid only)
6. [ ] Mac Catalyst entitlements include `network.server` (Mac Catalyst only)
7. [ ] `adb reverse` port forwarding (Android only)
