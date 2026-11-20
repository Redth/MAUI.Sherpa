# Website screenshots

The homepage's screenshot collection is defined in
[`website/screenshots.json`](../website/screenshots.json). It is the shared shot
list for macOS, Windows, and Linux, including routes, captions, platform support,
and paths to captures that actually exist.

## Screenshot Mode

Use a **Debug** build with `MAUI_SHERPA_SCREENSHOT_MODE=website-v1`.
Restart to enter or leave this mode. Invalid scenarios and Release activation
refuse to start rather than falling back to real data.

The real pages and native chrome show the same fictional **Summit Labs / Trail
Notes** environment on every platform. No SDK, JDK, devices, accounts, signing
keys, or secrets need importing. Platform capabilities stay genuine: Xcode and
local Apple simulators remain macOS-only.

The mode uses in-memory settings, preferences, secure storage, and vault state,
plus a separate per-run `MauiSherpa.Screenshots/<id>` directory for logs and
WebView state. It skips migrations, telemetry, automatic updates, and background
workers. External actions are disabled or explicitly blocked at service
boundaries; a missing fixture read records an error and fails capture readiness.
The WebView's existing Font Awesome, xterm, and Highlight.js assets are bundled
locally, with their upstream licenses, so captures do not depend on CDNs.

All heads start at **1440 × 960 logical pixels**, with a **250-pixel sidebar**,
100% font scale, and dark appearance. This leaves the complete menu visible
without scrolling. **Debug UI is hidden in Screenshot Mode only.** Ordinary
Debug launches retain it. Use a display/workspace large enough for this size;
the runner rejects incorrectly sized captures rather than cropping or stretching.
Use **2× native display scale**, producing **2880 × 1920 image pixels** for this
logical window. Increasing the window size or upscaling a 1× file is not a
substitute for high-DPI rendering.

Screenshot Mode is separate from **Demo Mode**, which blurs real data.
The fixture settings keep Demo Mode off. Fictional labels stay readable; normal
password/secret masking is preserved. Never import real data into a capture run.

### Automated capture

With Node.js 20+ and the MAUI CLI available, discover this Sherpa instance with
`dotnet maui devflow wait` and use its returned port explicitly:

```bash
node build/capture-screenshots.mjs --agent-port <port> --platform macos \
  --output <outside-repository-directory> --theme both
```

The runner waits for fixture readiness, then asks whether the user is ready to
leave the app untouched until the entire set finishes. After an explicit `yes`,
it waits **five seconds**, focuses the native window, captures the set without
further prompts, and closes the isolated app. Losing focus aborts the set;
do not publish a mixture of active and inactive window chrome.

An agent must ask with its user-question tool once the app is ready. Only after
the user agrees may it pass `--confirmed` to avoid asking twice. For preparation,
`--check-only --shots dashboard` verifies readiness without taking images,
prompting, or closing the app. Do not pass `--confirmed` based on an earlier run.

Use `windows` or `linux` for those native heads. `--theme both` captures dark and
light in one persistent DevFlow batch, avoiding competing mutation leases.
`--shots dashboard,doctor` selects views; `--check-only` verifies without saving
PNGs. The runner consumes the manifest, verifies the fixture/platform, route,
heading, appearance, settled loading, no errors or privacy blur, no remote web
resources, no Debug UI link, and no overflowing HTML sidebar before every shot.
Native sidebar visibility must also be reviewed in the actual PNG.

On Linux, DevFlow's GTK screenshot omits the native header bar, so the runner
captures the full X11 window with `build/capture-gtk-window.py` (Python 3 and
Pillow), cropping GTK's client-side shadow. Run the app with
`GDK_BACKEND=x11 GDK_SCALE=2` and a workspace large enough for 2880 × 1920
physical pixels. On Windows, use 200% display scaling and a sufficiently large
display. On macOS, use a Retina display.

Results are `<output>/<platform>/<theme>/<id>.png` plus `capture.json`, recording
revision, dirty-worktree status, fixture version, platform, theme, and actual
logical dimensions, pixel ratio, and actual pixel dimensions. DevFlow captures
use `--scale native`; its default otherwise downsamples to 1×.
Review both appearances; export the dark set as lossless 2× WebP:

```bash
python3 build/export-screenshots.py --platform macos \
  --input <capture-output>/macos/dark --output website/images/screenshots/macos
```

The exporter preserves full resolution and alpha. For Windows 11 compositor
captures, it removes the one-logical-pixel desktop fringe and masks the native
eight-logical-pixel rounded corners, including decontaminating partially covered
edge pixels. It does not color-key the app content or draw fake window chrome.
`--pixel-ratio 1` is available only for cleaning existing legacy assets; new
website captures must use 2×. Retain the PNGs and reports outside the repository.
Do not force-take another DevFlow client's mutation lease.

### Current assets and earlier captures

The seventeen macOS website assets now use the reviewed dark `website-v1`
fixture set. These captures retain the full 2880 × 1920 Retina pixels from the real
1440 × 960 logical AppKit window through MAUI DevFlow. Both dark and light PNG
originals were reviewed before export. The complete set was captured with the
window focused after user confirmation and the five-second countdown. Platform capture
metadata records the base revision and honestly marks the worktree as dirty:
the capture changes were not committed when captured.
Older images in the parent screenshots directory remain for the README and
detailed guides; they are not substituted for other platforms in the gallery.

The fifteen Windows assets use the reviewed dark `website-v1` fixture set from
revision `14c31c1` with a clean worktree. Both dark and light PNG originals
were reviewed. On this Windows setup, the DevFlow screenshot omitted the
WebView and native title bar; capture therefore used the foreground Windows
compositor at 100% display scale, with the DWM visible frame measured at
1440 × 960. DevFlow still provided navigation, appearance, and readiness
checks. The Android SDK capture uses the Installed tab. The checked-in Windows
11 assets have been cleaned to preserve transparent native edges, but remain
1× until a real Windows machine supplies a fresh 2× set. This macOS host cannot
produce genuine Windows captures.

The fifteen Linux assets use the dark `website-v1` fixture set, captured at
1440 × 960 from the real GTK window on Ubuntu 26.04.1 / GNOME Wayland, with the
app running through XWayland (`GDK_BACKEND=x11`) at 1× scale. Both appearances
were reviewed. In the light review set, the Doctor toolbar glyph is not retinted
after the runtime theme switch; the dark website set is unaffected.
The checked-in Linux set also remains 1× pending recapture on a Linux host with
`GDK_SCALE=2`; it has not been artificially enlarged.

## Feature audit

The old homepage omitted several substantial areas and showed separate Android
device/emulator pages that have since been combined.

| Area | Current product truth | Website treatment |
|---|---|---|
| Environment Doctor | Dependency checks, diagnostics, and repair actions | Retained; avoid promising a clean environment or automatic fixes for every issue |
| .NET management | SDK/runtime channels through dotnetup, workload sets, project context | Added to feature overview and gallery |
| Android | SDK packages, combined devices/emulators, USB/Wi-Fi pairing, inspection, keystores and PEPK export | Updated combined-device description and capture |
| Apple local tools | Xcode installations/downloads, simulator runtimes, local Apple devices and simulators | Added Xcode; clearly macOS-only |
| Apple account tools | Certificates/CSRs, bundle IDs/capabilities, registered devices, provisioning profiles | Distinguished from macOS-only local tools |
| Secrets | Encrypted Local Vault and configurable cloud-provider copies | Added to feature overview |
| Publishing | Reusable profiles, destination review, GitHub, GitLab, Gitea, Azure DevOps | Added; do not imply publishing occurs without review |
| Profiling | MAUI CLI startup/interaction captures; trace and GC-dump viewers | Added; Android capture and macOS iOS-simulator capture restrictions stated |
| Push testing | APNs and Firebase payload composition/testing | Added; capture never sends notifications |
| App inspection | DevFlow agent discovery, visual tree/properties, network, logs, WebViews; device-specific tools; standalone inspector CLI | Retained, with current agent-list capture and existing guide links |
| Copilot | In-app assistance with sign-in/access requirements | Retained with access caveat |

Audit sources: `src/MauiSherpa/Components/MainLayout.razor`,
`src/MauiSherpa/Pages/`, `src/MauiSherpa.Core/Services/CloudSecretsProviderFactory.cs`,
`src/MauiSherpa.Core/Services/SecretsPublisherService.cs`, and the platform app
heads. Local Apple tools are gated in `MainLayout.razor`; Apple account tools are
not. GTK's Debug DevFlow agent and Blazor bridge are enabled with a compatible
GTK package set. The Linux runtime is verified: the agent connects and the
runner captures every cross-platform view at the requested native size.

## Capture contract

- Capture the native desktop app, including its real sidebar and toolbar. Do not
  use browser-rendered app mockups or put macOS chrome around another platform.
- Use a **1440 × 960 logical-pixel window**, **100% app font scale**,
  **dark app theme**, no open menus, and the content scrolled to the top.
  Keep the entire native window and verify every menu element is visible without
  scrolling. Do not stretch, crop, or substitute another platform's chrome.
- Use the `id` from the manifest as the filename:
  `website/images/screenshots/<platform>/<id>.webp`. Platform keys are `macos`,
  `windows`, and `linux`.
- Keep PNG originals outside the repository. Export lossless WebP at **2× native
  resolution**, preserving alpha and all 2880 × 1920 pixels. Never resize a 1×
  source to claim 2× quality.
- Use **Screenshot Mode**, not Demo Mode, for new captures. Do not add redaction
  CSS or replace DOM text. For exceptional captures outside Screenshot Mode,
  enable Demo Mode and irreversibly mask every identifying label, native account
  name, path, identifier, and token; privacy blur alone is not sufficient.
- Read-only navigation only. Do not install SDKs, start/stop other sessions'
  devices, create/revoke signing assets, publish secrets, send push notifications,
  or delete data for a screenshot.
- Wait for a page's loading state to finish. Record a blocker rather than
  publishing a spinner, authentication error, half-loaded view, or unexpectedly
  empty fixture list.
- Do not inspect another session's live app just to populate the App Inspector.
  Its agent-list view is the baseline; detailed inspector captures require an
  explicitly approved demo target.
- Review every exported image at full resolution before adding it to the manifest.
  Close the isolated app when finished; ordinary preferences are not changed.

### Equivalent views

All rows use the top of the page, with no modal open.

| ID / filename stem | Route | Reference state | Platforms |
|---|---|---|---|
| `dashboard` | `/` | Environment health cards and connected-device area | All |
| `dotnet-sdk` | `/dotnet-sdk` | Tracked channels and installed components; groups collapsed | All |
| `doctor` | `/doctor` | Completed analysis, summary and context; no repairs | All |
| `android-sdk` | `/android-sdk` | Installed-package view, categories collapsed | All |
| `android-devices` | `/devices` | Combined physical-device and emulator lists | All |
| `keystores` | `/keystores` | Fictional vault-backed keystores, passwords masked | All |
| `xcode` | `/xcode-management` | Installed Xcode tab; no downloads or selection changes | macOS |
| `apple-simulators` | `/apple-simulators` | Physical devices and simulator list; no device changes | macOS |
| `certificates` | `/certificates` | Certificates list; no actions | All |
| `profiles` | `/profiles` | Provisioning-profile list; no actions | All |
| `bundle-ids` | `/bundle-ids` | Identifier list; no capabilities dialog | All |
| `secrets` | `/secrets` | Root folder, values hidden | All |
| `publishing` | `/secrets/publish` | Publish-profile list; no review/publish action | All |
| `profiling` | `/profiling` | Three saved fictional sessions; no capture/viewer opened | All |
| `app-inspector` | `/devflow` | Connected-agent list, no agent opened | All |
| `firebase-push` | `/firebase-push` | Trail Notes Android project, topic and example message; never send | All |
| `apple-push` | `/push-testing` | Trail Notes iOS project and example payload; never send | All |

### macOS build and DevFlow

```bash
dotnet build src/MauiSherpa.MacOS -f net10.0-macos
MAUI_SHERPA_SCREENSHOT_MODE=website-v1 MAUI_SHERPA_ENABLE_DEVFLOW=1 \
  "src/MauiSherpa.MacOS/bin/Debug/net10.0-macos/osx-arm64/MAUI Sherpa.app/Contents/MacOS/MauiSherpa.MacOS"
dotnet maui devflow wait --wait-platform macOS --timeout 30
dotnet maui devflow list
```

The environment variable is required by this app's Debug registration. Do not
edit the app or upgrade DevFlow packages just to enable capture. If another
instance is running, identify the exact process before restarting it, and do not
close another session's app.

Use the port returned for **this** Sherpa instance on every command:

```bash
dotnet maui devflow agent status --agent-port <port>
dotnet maui devflow ui resize 1440 960 --agent-port <port>
dotnet maui devflow webview Runtime evaluate \
  "document.querySelector('a[href=\"dotnet-sdk\"]').click()" --agent-port <port>
dotnet maui devflow ui screenshot --agent-port <port> --scale native --output <private-output.png>
```

Prefer one persistent `dotnet maui devflow batch --agent-port <port>` session
for multiple mutations. Separate CLI invocations can encounter the previous
invocation's mutation lease; do not force-take another client's lease. Batch
expressions should use single quotes inside their outer double quotes.

Prefer the checked-in runner for navigation and readiness checks. It switches
appearance through the real theme service, including native chrome, rather
than changing only CSS. The hidden `#screenshot-diagnostics` marker exposes the
fixture version, platform, initialization state, theme, fixed clock, and blocked
operations; it is absent from ordinary launches.

## Adding platform captures

The shared integration branch is **`redth-website-screenshot-quality`**. Both platform
handoffs should add their captures to that branch and its existing pull request,
not open competing website-refresh PRs. Pull the latest branch before starting
and rebase your platform-only commits onto its latest remote state before
pushing. Never force-push the shared branch.

1. Follow the shared capture contract and table above.
2. Add only reviewed files under the correct platform directory.
3. Add that platform's path to the matching shot's `assets` object. For example:

   ```json
   "assets": {
     "macos": "images/screenshots/macos/dashboard.webp",
     "windows": "images/screenshots/windows/dashboard.webp"
   }
   ```

4. Do not add `windows` or `linux` to macOS-only shots' `platforms` lists.
5. Run `node --test tests/website.test.mjs`, then preview `website/` over HTTP.
   Check platform switching, the download destination, enlargement, keyboard
   navigation, and a reload with the platform preference saved.

The gallery detects desktop OS locally in the browser, without sending the user
agent anywhere. A manual selection is saved when browser storage is available.
If an automatically detected platform has no captures, the gallery explicitly
labels the macOS preview. An explicit selection never silently substitutes
another platform's image. Missing views and macOS-only tools have distinct
messages. No website code change is needed when new assets are registered.

## Windows handoff prompt

```text
Capture the Windows equivalent of MAUI Sherpa's refreshed macOS website set.
Check out the existing shared branch redth-website-screenshot-quality and pull its latest
changes. Add your platform-only changes to that branch and its existing PR.
Use docs/SCREENSHOTS.md and website/screenshots.json from the website refresh,
with website/images/screenshots/macos/ as the visual reference.

Build and launch the current Windows head from PowerShell:
$env:MAUI_SHERPA_SCREENSHOT_MODE = "website-v1"
$env:MAUI_SHERPA_ENABLE_DEVFLOW = "1"
dotnet build src/MauiSherpa -f net10.0-windows10.0.19041.0 -t:Run
Discover this Sherpa instance's port from a second terminal and run:
node build/capture-screenshots.mjs --agent-port <port> --platform windows --output <outside-repo-directory> --theme both
Verify the diagnostics identify website-v1 and Windows. Do not import accounts,
SDKs, JDKs, devices, signing keys, or secrets. Do not upgrade packages for capture.

Match a 1440x960 logical-pixel window, dark app theme, 100% font scale, top scroll
position, and the states in the shared capture table. Keep real Windows/WinUI
chrome. Capture all 15 cross-platform IDs; do not capture xcode or apple-simulators.
Use the shared fictional data with Demo Mode OFF. Debug UI should be absent and
the whole menu visible without scrolling. Keep fictional labels readable; do
not add blur, redaction CSS, DOM text replacements, or fake platform chrome.
Review both appearances and use the dark set for the website.

Navigate read-only: do not install/update SDKs or workloads, pair/start/stop
devices owned by other sessions, create/revoke certificates, publish secrets,
send notifications, or delete anything. Record unavailable or loading views as
blockers rather than publishing an error screen. Do not inspect someone else's
running app to populate the inspector.

Wait until the isolated app is ready, then ask the user whether they are ready
to leave the focused window untouched for the complete set. Wait for an explicit
yes and five seconds before capturing. Use --confirmed only after that answer.
Close the isolated app when done. If focus is lost, discard and repeat the set
after a new readiness confirmation.

Use 200% Windows display scaling: the 1440x960 logical window must produce
2880x1920 native pixels. Keep those pixels; do not upscale a 1x capture.
Export lossless 2x WebP files with build/export-screenshots.py --platform windows.
It preserves transparency and removes the Windows 11 compositor desktop fringe.
Save files as:
website/images/screenshots/windows/<manifest-id>.webp
Keep PNG originals private and outside the repository. Preserve aspect ratio
and full native window chrome. Add only the reviewed Windows paths to each
matching assets.windows entry in website/screenshots.json. Do not change macOS
assets, captions, support declarations, or website design.

Close the isolated app and clear both environment variables when finished. Run:
node --test tests/website.test.mjs
Return a contact sheet, the exact captured/missing IDs, dimensions, and any
blockers. Commit only your Windows assets and manifest entries, rebase onto the
latest origin/redth-website-screenshot-quality, and push to that existing shared branch.
Do not force-push, overwrite another platform's entries, or open another PR.
```

## Linux handoff prompt

```text
Capture the Linux equivalent of MAUI Sherpa's refreshed macOS website set.
Check out the existing shared branch redth-website-screenshot-quality and pull its latest
changes. Add your platform-only changes to that branch and its existing PR.
Use docs/SCREENSHOTS.md and website/screenshots.json from the website refresh,
with website/images/screenshots/macos/ as the visual reference.

Build and run the actual GTK head:
dotnet build src/MauiSherpa.LinuxGtk
GDK_BACKEND=x11 GDK_SCALE=2 MAUI_SHERPA_SCREENSHOT_MODE=website-v1 MAUI_SHERPA_ENABLE_DEVFLOW=1 dotnet run --project src/MauiSherpa.LinuxGtk --no-build
The compatible Debug GTK DevFlow agent and Blazor bridge are enabled. Verify
agent connectivity on Linux; do not use browser mockups or Computer Use as a
substitute. Discover this Sherpa instance's port from a second terminal and run:
node build/capture-screenshots.mjs --agent-port <port> --platform linux --output <outside-repo-directory> --theme both
The runner captures the X11 window (header bar included) with
build/capture-gtk-window.py, which needs Python 3 and Pillow. Report any GTK
runtime/connectivity issue instead of silently publishing partial captures.

Match a 1440x960 logical-pixel window, dark app theme, 100% font scale, top scroll
position, and the states in the shared capture table. Keep real Linux/GTK chrome
and record the distribution, desktop environment, and display scale. Capture
all 15 cross-platform IDs; do not capture xcode or apple-simulators.

Use shared fictional data with Demo Mode OFF. No SDK, JDK, devices, or account
imports are needed. Debug UI should be absent and the full menu visible without
scrolling. Keep fictional labels readable, with no redaction CSS or DOM text
replacement. Review both appearances; use the dark set for the website. Do not
install/update SDKs or workloads, start/stop other sessions'
devices, create/revoke signing assets, publish secrets, send notifications, or
delete anything. Record blocked/loading views instead of publishing them. Do
not inspect another session's app to populate the inspector.

Wait until the isolated app is ready, then ask the user whether they are ready
to leave the focused window untouched for the complete set. Wait for an explicit
yes and five seconds before capturing. Use --confirmed only after that answer.
Close the isolated app when done. If focus is lost, discard and repeat the set
after a new readiness confirmation.

Use GDK_SCALE=2: the 1440x960 logical window must produce 2880x1920 native pixels.
Keep those pixels; do not upscale a 1x capture.
Export lossless 2x WebP files with build/export-screenshots.py --platform linux.
Save files as:
website/images/screenshots/linux/<manifest-id>.webp
Keep PNG originals private and outside the repository. Preserve aspect ratio
and the full native window. Add only reviewed Linux paths to each matching
assets.linux entry in website/screenshots.json. Do not change macOS assets,
captions, support declarations, app dependencies, or website design.

Close the isolated app when finished. Run:
node --test tests/website.test.mjs
Return a contact sheet, captured/missing IDs, dimensions, and blockers. Commit
only your Linux assets and manifest entries, rebase onto the latest
origin/redth-website-screenshot-quality, and push to that existing shared branch. Do not
force-push, overwrite another platform's entries, or open another PR.
```
