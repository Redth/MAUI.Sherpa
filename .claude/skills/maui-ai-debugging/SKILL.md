---
name: maui-ai-debugging
description: >
  End-to-end workflow for building, deploying, inspecting, and debugging .NET MAUI and MAUI Blazor Hybrid apps
  as an AI agent. Use when: (1) Building or running a MAUI app on iOS simulator, Android emulator, or Mac Catalyst,
  (2) Deploying a MAUI app to a device/emulator/simulator, (3) Inspecting or interacting with a running MAUI app's
  UI (visual tree, element tapping, filling text, screenshots, property queries), (4) Debugging Blazor WebView
  content inside a MAUI app via CDP, (5) Managing iOS simulators or Android emulators (create, boot, list, install),
  (6) Setting up the MauiDevFlow agent and CLI in a MAUI project, (7) Completing a build-deploy-inspect-fix feedback
  loop for MAUI app development. Covers: maui-devflow CLI, androidsdk.tool (android), appledev.tools (apple),
  adb, xcrun simctl, and dotnet build/run for all MAUI target platforms.
---

# MAUI AI Debugging

Build, deploy, inspect, and debug .NET MAUI apps from the terminal. This skill enables a complete
feedback loop: **build → deploy → inspect → fix → rebuild**.

## Prerequisites

Install (or update) the CLI tool and ensure it's the latest version:

```bash
dotnet tool install --global Redth.MauiDevFlow.CLI || dotnet tool update --global Redth.MauiDevFlow.CLI
```

For platform-specific tools: `dotnet tool install --global androidsdk.tool` (Android)
and `dotnet tool install --global appledev.tools` (iOS/Mac).

**Keep the skill up to date:** Run `maui-devflow update-skill` periodically (or at the start
of a new session) to download the latest version of this skill from GitHub. The skill evolves
alongside the CLI — outdated skill files may reference removed options or miss new commands.

## Integrating MauiDevFlow into a MAUI App

For complete setup instructions including NuGet packages, MauiProgram.cs registration,
Blazor script tag, Mac Catalyst entitlements, and Android port forwarding, see
[references/setup.md](references/setup.md).

**Quick summary:**
1. Add NuGet packages (`Redth.MauiDevFlow.Agent`, and `Redth.MauiDevFlow.Blazor` for Blazor Hybrid)
2. Register in `MauiProgram.cs` inside `#if DEBUG`
3. Create `.mauidevflow` with a random port (see below)
4. For Blazor Hybrid: add `<script src="chobitsu.js"></script>` to `wwwroot/index.html`
5. For Mac Catalyst: ensure `network.server` entitlement
6. For Android: run `adb reverse` for the configured port

**Port configuration:** If no `.mauidevflow` exists in the project directory, create one
with a random port between 9223–9899 to avoid collisions with other projects:

```json
{
  "port": <pick a random number between 9223 and 9899>
}
```

Both the build and the CLI read this file automatically — no need to pass `-p:MauiDevFlowPort`
or `--agent-port` flags. Run CLI commands from the project directory for automatic detection.
If the file already exists, use the port specified in it.

## Core Workflow

### 1. Ensure a Device/Simulator/Emulator is Running

**iOS Simulator:**
```bash
xcrun simctl list devices booted                              # check booted sims
xcrun simctl boot <UDID>                                      # boot if needed
apple simulator list --booted                                 # alternative
apple simulator boot <UDID>                                   # alternative
```

**Android Emulator:**
```bash
android avd list                                              # list AVDs
android avd start --name <avd-name>                           # start emulator
adb devices                                                   # verify connected
```

**Mac Catalyst:** No device setup needed — runs as desktop app.

### 2. Detect the TFM

**IMPORTANT:** Before building, detect the correct Target Framework Moniker from the project.
Do NOT assume `net10.0` — many projects use `net9.0`, `net8.0`, etc.

```bash
# Read TargetFrameworks from the .csproj or Directory.Build.props
grep -i 'TargetFrameworks' *.csproj Directory.Build.props 2>/dev/null
```

Look for entries like `net9.0-ios;net9.0-android;net9.0-maccatalyst`. Use that version
(e.g. `net9.0`) in all build commands below. The examples use `$TFM` as a placeholder.

### 3. Build and Deploy

**CRITICAL:** `-t:Run` keeps the process alive until the app exits. Run it in background
(async bash) or a separate shell — do NOT run it synchronously and expect to execute
subsequent commands in the same shell.

```bash
# iOS Simulator (run in background/async shell)
dotnet build -f $TFM-ios -t:Run -p:_DeviceName=:v2:udid=<UDID>

# Android Emulator (run in background/async shell)
dotnet build -f $TFM-android -t:Run

# Mac Catalyst (run in background/async shell)
dotnet build -f $TFM-maccatalyst -t:Run
```

Replace `$TFM` with the actual version detected in step 2 (e.g. `net9.0`, `net10.0`).
Build + Run can take 30-120+ seconds. Use `initial_wait: 120` or higher for async monitoring.
Wait for "Running app..." or similar output before proceeding to connectivity checks.

**Device/simulator compatibility:** The TFM compile target (e.g. net10.0-android targets API 36,
net10.0-ios targets iOS 26) does NOT mean you need a matching emulator/simulator version. Apps run
on any device at or above `SupportedOSPlatformVersion` (check `Directory.Build.props` or `.csproj`).
Use whatever emulator/simulator is available — don't waste time finding one that matches the TFM.

For Android emulators, set up port forwarding after deploy:
```bash
adb reverse tcp:9223 tcp:9223    # Agent + CDP (single port)
```

### 4. Verify Connectivity

```bash
maui-devflow MAUI status          # Agent connection + CDP readiness
maui-devflow cdp status           # CDP-specific connection check
```

### 5. Inspect and Interact

See **Command Reference** below for the full command set.

**Typical inspection flow:**
1. `maui-devflow MAUI tree` — see the full visual tree with element IDs, types, text, bounds
2. `maui-devflow MAUI query --automationId "MyButton"` — find specific elements
3. `maui-devflow MAUI element <id>` — get full details (type, bounds, visibility, children)
4. `maui-devflow MAUI property <id> Text` — read any property by name
5. `maui-devflow MAUI screenshot --output screen.png` — visual verification

**Debugging styling/layout with property inspection:**
Use `property` to verify runtime values without relying solely on screenshots:
```bash
maui-devflow MAUI property <id> BackgroundColor    # verify dark mode colors
maui-devflow MAUI property <id> TextColor          # check text visibility
maui-devflow MAUI property <id> IsVisible          # check element visibility
maui-devflow MAUI property <id> Width              # verify layout sizing
maui-devflow MAUI property <id> Opacity            # check transparency
```
Combine tree + property for systematic debugging: get element IDs from `tree`, then inspect
specific properties. This is more reliable than screenshots for verifying exact color values,
font sizes, and layout metrics.

**Live editing native MAUI elements (no rebuild needed):**
Use `set-property` to change any writable property at runtime. Changes are immediate and
visible — experiment with colors, sizes, text, and layout before committing to code.

```bash
# Change text
maui-devflow MAUI set-property <id> Text "New Title"

# Change colors (named colors, hex, or rgba)
maui-devflow MAUI set-property <id> TextColor "Tomato"
maui-devflow MAUI set-property <id> BackgroundColor "#2a1f5e"
maui-devflow MAUI set-property <id> TextColor "DodgerBlue"

# Change sizing and layout
maui-devflow MAUI set-property <id> FontSize "24"
maui-devflow MAUI set-property <id> Padding "10,5,10,5"
maui-devflow MAUI set-property <id> Opacity "0.5"
maui-devflow MAUI set-property <id> WidthRequest "200"

# Toggle visibility
maui-devflow MAUI set-property <id> IsVisible "false"
```

Supports: string, bool, int, double, Color (named/hex), Thickness, enums. Changes persist
until the app restarts — safe for experimentation.

**Typical interaction flow:**
1. `maui-devflow MAUI fill <entryId> "text"` — type into Entry/Editor fields
2. `maui-devflow MAUI tap <buttonId>` — tap buttons, checkboxes, list items
3. `maui-devflow MAUI clear <entryId>` — clear text fields
4. Take screenshot to verify result

**Blazor WebView (if applicable):**
1. `maui-devflow cdp snapshot` — DOM tree as accessible text (best for AI)
2. `maui-devflow cdp Input fill "css-selector" "text"` — fill inputs
3. `maui-devflow cdp Input dispatchClickEvent "css-selector"` — click elements
4. `maui-devflow cdp Runtime evaluate "js-expression"` — run JS

**Live CSS/DOM editing in Blazor (no rebuild needed):**
Use `Runtime evaluate` to experiment with styles and DOM changes in-place before committing
to code. Changes are immediate and non-destructive — lost on page reload.

```bash
# Change an element's style directly
maui-devflow cdp Runtime evaluate "document.querySelector('h1').style.color = 'tomato'"

# Bulk-update elements
maui-devflow cdp Runtime evaluate "document.querySelectorAll('.todo-item').forEach(el => el.style.borderRadius = '20px')"

# Set or override CSS variables
maui-devflow cdp Runtime evaluate "document.documentElement.style.setProperty('--bg-color', '#1a1a2e')"

# Inject a whole style rule (great for testing complex changes)
maui-devflow cdp Runtime evaluate "document.head.insertAdjacentHTML('beforeend', '<style>.btn { background: linear-gradient(135deg, #667eea, #764ba2) !important; }</style>')"

# Change element text to preview copy changes
maui-devflow cdp Runtime evaluate "document.querySelector('h1').textContent = 'New Title'"
```

**Reading computed styles (verify actual rendered values):**
```bash
maui-devflow cdp Runtime evaluate "getComputedStyle(document.querySelector('.my-class')).backgroundColor"
maui-devflow cdp Runtime evaluate "getComputedStyle(document.querySelector('.my-class')).borderRadius"
maui-devflow cdp Runtime evaluate "window.matchMedia('(prefers-color-scheme: dark)').matches"
```
Use computed style reads to verify exact color values, font sizes, and layout metrics — more
reliable than screenshots for precise debugging.

### 6. Reading Application Logs

MauiDevFlow automatically captures all `Microsoft.Extensions.Logging` (`ILogger`) output
to rotating log files on the device. This means any `ILogger<T>` calls in the app's code
(or in libraries) are available for remote retrieval — invaluable for debugging.

```bash
maui-devflow MAUI logs                   # fetch 100 most recent log entries
maui-devflow MAUI logs --limit 50        # fetch 50 entries
maui-devflow MAUI logs --skip 100        # skip newest 100, get next batch
```

Output is color-coded by level (red=Critical/Error, yellow=Warning, green=Info, gray=Debug/Trace).
Each entry includes timestamp, log level, category (logger name), and message.

**Debugging workflow with logs:**
1. Reproduce the issue (tap a button, navigate, etc.)
2. `maui-devflow MAUI logs --limit 20` — check recent log entries for errors or warnings
3. If needed, add temporary `ILogger` calls to the app code for more detail:
   ```csharp
   _logger.LogInformation("Button tapped, item count: {Count}", items.Count);
   _logger.LogWarning("Unexpected state: {State}", currentState);
   ```
4. Rebuild, redeploy, reproduce, and fetch logs again

**Log configuration** (in `AddMauiDevFlowAgent` options):
- `EnableFileLogging` (default: `true`) — toggle file logging
- `MaxLogFileSize` (default: 1 MB) — max size per log file before rotation
- `MaxLogFiles` (default: 5) — number of rotated files to keep

The agent also exposes logs via REST: `GET /api/logs?limit=N&skip=N` returns a JSON array.

### 7. Live Preview Before Rebuilding

Before rebuilding, use live editing to prototype small changes directly on the running app.
This skips the build→deploy cycle entirely — test colors, text, sizes, and styles in seconds
instead of minutes. Make several tweaks in a batch, evaluate the result visually, then commit
only the changes you're happy with to code.

**Workflow: tweak → evaluate → tweak → ... → commit to code → rebuild once**

For **native MAUI elements** — use `set-property`:
```bash
maui-devflow MAUI set-property <id> TextColor "DodgerBlue"
maui-devflow MAUI set-property <id> FontSize "20"
maui-devflow MAUI set-property <id> Padding "12,8,12,8"
maui-devflow MAUI screenshot --output preview.png    # check the result
```

For **Blazor Hybrid pages** — use `cdp Runtime evaluate`:
```bash
maui-devflow cdp Runtime evaluate "document.querySelector('h1').style.color = 'tomato'"
maui-devflow cdp Runtime evaluate "document.documentElement.style.setProperty('--primary', '#667eea')"
maui-devflow cdp Runtime evaluate "document.querySelector('.card').style.borderRadius = '16px'"
maui-devflow cdp Page captureScreenshot                # check the result
```

**Mix both** on apps with native + Blazor tabs — tweak native properties on one tab and
Blazor styles on another, all without a single rebuild. Once satisfied, apply the changes
to source code and rebuild once.

### 8. Rebuild

After live preview confirms the desired look, edit source code to make changes permanent.
The full cycle: edit code → `dotnet build -f $TFM-<platform> -t:Run ...` → `maui-devflow MAUI status` → inspect.
If the build fails, see **Troubleshooting** below.

## Command Reference

### maui-devflow MAUI (Native Agent)

Global options: `--agent-host` (default localhost), `--agent-port` (default 9223), `--platform`.

| Command | Description |
|---------|-------------|
| `MAUI status` | Agent connection status, platform, app name |
| `MAUI tree [--depth N]` | Visual tree (IDs, types, text, bounds). Depth 0=unlimited |
| `MAUI query --type T --automationId A --text T` | Find elements (any/all filters) |
| `MAUI tap <elementId>` | Tap an element |
| `MAUI fill <elementId> <text>` | Fill text into Entry/Editor |
| `MAUI clear <elementId>` | Clear text from element |
| `MAUI screenshot [--output path.png]` | PNG screenshot |
| `MAUI property <elementId> <prop>` | Read property (Text, IsVisible, FontSize, etc.) |
| `MAUI set-property <elementId> <prop> <value>` | Set property (live editing — colors, text, sizes, etc.) |
| `MAUI element <elementId>` | Full element JSON (type, bounds, children, etc.) |
| `MAUI navigate <route>` | Shell navigation (e.g. `//native`, `//blazor`) |
| `MAUI logs [--limit N] [--skip N]` | Fetch application logs (newest first) |

Element IDs come from `MAUI tree` or `MAUI query`. AutomationId-based elements use their
AutomationId directly. Others use generated hex IDs. When multiple elements share the same
AutomationId, suffixes are appended: `TodoCheckBox`, `TodoCheckBox_1`, `TodoCheckBox_2`, etc.

### maui-devflow cdp (Blazor WebView CDP)

Global options: `--agent-host` (default localhost), `--agent-port` (default 9223).
CDP commands use the same agent port — all communication goes through a single port.

| Command | Description |
|---------|-------------|
| `cdp status` | CDP connection status |
| `cdp snapshot` | Accessible DOM text (best for AI agents) |
| `cdp Browser getVersion` | Browser/WebView version info |
| `cdp Runtime evaluate <expr>` | Evaluate JavaScript |
| `cdp DOM getDocument` | Full DOM document |
| `cdp DOM querySelector <sel>` | Find first matching element |
| `cdp DOM querySelectorAll <sel>` | Find all matching elements |
| `cdp DOM getOuterHTML <sel>` | Get outer HTML of element |
| `cdp Page navigate <url>` | Navigate to URL |
| `cdp Page reload` | Reload page |
| `cdp Page captureScreenshot` | Screenshot as base64 |
| `cdp Input dispatchClickEvent <sel>` | Click element by CSS selector |
| `cdp Input insertText <text>` | Insert text at focused element |
| `cdp Input fill <selector> <text>` | Focus + fill text into element |

### Agent REST API (Direct HTTP)

The agent exposes JSON endpoints on port 9223 (configurable via `-p:MauiDevFlowPort`):

| Endpoint | Method | Body |
|----------|--------|------|
| `/api/status` | GET | — |
| `/api/tree?depth=N` | GET | — |
| `/api/element/{id}` | GET | — |
| `/api/query?type=&text=&automationId=` | GET | — |
| `/api/action/tap` | POST | `{"elementId":"..."}` |
| `/api/action/fill` | POST | `{"elementId":"...","text":"..."}` |
| `/api/action/clear` | POST | `{"elementId":"..."}` |
| `/api/action/focus` | POST | `{"elementId":"..."}` |
| `/api/screenshot` | GET | — (returns PNG) |
| `/api/property/{id}/{name}` | GET | — |
| `/api/property/{id}/{name}` | POST | `{"value":"..."}` (set property — live editing) |
| `/api/logs?limit=N&skip=N` | GET | — (returns JSON array of log entries) |
| `/api/cdp` | POST | CDP command JSON (e.g. `{"id":1,"method":"Runtime.evaluate","params":{...}}`) |

## Platform Details

For detailed platform-specific setup, simulator/emulator management, and troubleshooting:

- **Setup & Installation**: See [references/setup.md](references/setup.md)
- **iOS / Mac Catalyst**: See [references/ios-and-mac.md](references/ios-and-mac.md)
- **Android**: See [references/android.md](references/android.md)

## Multi-Project / Custom Ports

The default port is 9223. For custom ports, create a `.mauidevflow` file in the project
directory. Both the MSBuild targets and the CLI read this file automatically:

```json
{
  "port": 9225
}
```

With this file in place:
- **Build**: `dotnet build -f $TFM-maccatalyst -t:Run` — automatically uses port 9225
- **CLI**: `maui-devflow MAUI status` — automatically uses port 9225 (reads from cwd)
- **Android**: match the port: `adb reverse tcp:9225 tcp:9225`

No need to pass `-p:MauiDevFlowPort` or `--agent-port` — the config file handles it.

**Port priority:** Code-set `options.Port` > MSBuild `-p:MauiDevFlowPort` > `.mauidevflow` > Default 9223.

The CLI looks for `.mauidevflow` in the current working directory. Run CLI commands from
the project directory (where the file lives) for automatic port detection.

## Troubleshooting

### Connection Refused / Cannot Connect

If `maui-devflow MAUI status` fails with connection refused:

1. **App not running?** Verify the app launched: check the build output for errors.
2. **Wrong port?** Ensure `.mauidevflow` port matches between build and CLI. Run CLI from
   the project directory so it auto-detects the config file.
3. **Port already in use?** Another process may hold the port. Check with:
   ```bash
   lsof -i :<port>       # macOS/Linux
   ```
   Pick a different port in `.mauidevflow` and rebuild.
4. **Android?** Did you run `adb reverse tcp:<port> tcp:<port>`? Re-run it after each deploy.
5. **Mac Catalyst?** Check entitlements include `network.server` (see setup.md step 5).

### Build Failures

**Missing workloads:**
```
error NETSDK1147: To build this project, the following workloads must be installed: maui-ios
```
Fix: `dotnet workload install maui` (installs all MAUI workloads).

**SDK version mismatch:**
```
error : The current .NET SDK does not support targeting .NET 10.0
```
Fix: Install the required .NET SDK version, or check `global.json` for version pins.

**Android SDK not found:**
```
error XA0000: Could not find Android SDK
```
Fix: Install Android SDK via `android sdk install` or set `$ANDROID_HOME`.

**iOS provisioning / signing errors:**
Fix: For simulators, ensure no signing is configured (default). For devices, set up provisioning
profiles via `apple appstoreconnect profiles list`.

**General build failure recovery:**
1. `dotnet clean` then retry the build
2. Delete `bin/` and `obj/` directories: `rm -rf bin obj` then rebuild
3. Check the full build output (not just the last error) — earlier warnings often reveal the root cause

### CDP Not Connecting (Blazor Hybrid)

If `maui-devflow cdp status` fails but `MAUI status` works:

1. **Missing script tag?** Ensure `<script src="chobitsu.js"></script>` is in `wwwroot/index.html`
2. **Blazor not initialized?** Navigate to a Blazor page first, then retry
3. Check app logs: `maui-devflow MAUI logs --limit 20` — look for `[BlazorDevFlow]` errors

### Mac Catalyst: Repeated Permission Dialogs on Rebuild

If macOS prompts "App would like to access your Documents folder" on every rebuild:

**Cause:** TCC permissions are tied to the app's code signature. Ad-hoc Debug builds produce a
different signature each rebuild → macOS forgets the grant and re-prompts. This happens even
with App Sandbox disabled.

**Fix:** Don't access TCC-protected directories (`~/Documents`, `~/Downloads`, `~/Desktop`,
or dotfiles like `~/.myapp/` in the home root) programmatically. Instead use:
- `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` → `~/Library/Application Support/` (not TCC-protected)
- `NSOpenPanel`/`NSSavePanel` for user-initiated file access (grants automatic TCC exemption)

If you can't avoid TCC paths, sign Debug builds with a stable Apple Development certificate
so the code signature stays consistent across rebuilds.

## Tips

- Use `AutomationId` on important MAUI controls for stable element references.
- The visual tree only reflects what's currently rendered. Off-screen items in CollectionView
  may not appear until scrolled into view.
- **Shell navigation**: Use `maui-devflow MAUI navigate "//route"` for Shell-based apps.
  Routes are defined in AppShell.xaml via `Route` property on ShellContent elements.
- For Blazor Hybrid, `cdp snapshot` is the most AI-friendly way to read page state.
- Build times: Mac Catalyst ~5-10s, iOS ~30-60s, Android ~30-90s. Set appropriate timeouts.
- After Android deploy, always run `adb reverse` for port forwarding (match the port in `.mauidevflow` or default 9223).
- **Property inspection** is more reliable than screenshots for verifying exact runtime values
  (colors, sizes, visibility). Use `tree` → `property` workflow for systematic debugging.
- **Application logs** are captured automatically from `ILogger`. Use `MAUI logs` to fetch
  them remotely. Add temporary `ILogger` calls for extra debug output, then fetch logs after
  reproducing issues. This is often faster than attaching a debugger.
- **Single port**: Both MAUI native and CDP commands share port 9223 (configurable).
  No separate WebSocket endpoint needed.
