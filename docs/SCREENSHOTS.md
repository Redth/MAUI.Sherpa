# Website screenshots

The homepage's screenshot collection is defined in
[`website/screenshots.json`](../website/screenshots.json). It is the shared shot
list for macOS, Windows, and Linux, including routes, captions, platform support,
and paths to captures that actually exist.

The November 20, 2026 refresh uses the app at revision `6e6fbb1`. The macOS
reference set was captured through MAUI DevFlow from the AppKit head, in dark
mode. These are real app captures with sensitive text redacted, not mockups.
Older images in the parent screenshots directory remain for the README and
detailed guides; they are not substituted for other platforms in the new gallery.

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
not. GTK's own DevFlow **agent integration is currently disabled** in
`src/MauiSherpa.LinuxGtk/MauiSherpa.LinuxGtk.csproj`; this is separate from using
Sherpa's App Inspector to inspect another app.

## Capture contract

- Capture the native desktop app, including its real sidebar and toolbar. Do not
  use browser-rendered app mockups or put macOS chrome around another platform.
- Use a roughly **1440 × 960 logical-pixel window**, **100% app font scale**,
  **dark app theme**, no open menus, and the content scrolled to the top.
  Most current macOS exports are **1440 × 939 pixels**; Dashboard and Keystores
  were recaptured at **1280 × 754** after a window-size change. DevFlow acknowledged
  subsequent resize requests without changing the AppKit window. Other platforms
  may include different title-bar heights; preserve aspect ratio and full native
  chrome rather than stretching or cropping. Match content/state first.
- Use the `id` from the manifest as the filename:
  `website/images/screenshots/<platform>/<id>.webp`. Platform keys are `macos`,
  `windows`, and `linux`.
- Keep PNG originals outside the repository. Export lossless WebP at 1× logical
  resolution so text stays sharp without large Retina files.
- Enable **Settings → Demo Mode** first. It does not cover every identifying
  label:   also redact account names (including native toolbar identity selectors),
  device/emulator names, signing names, bundle
  identifiers, local paths/usernames, project names, secret names, and any visible
  tokens. For public output, use opaque masking, not a reversible image layer.
- Read-only navigation only. Do not install SDKs, start/stop other sessions'
  devices, create/revoke signing assets, publish secrets, send push notifications,
  or delete data for a screenshot.
- Wait for a page's loading state to finish. Record a blocker rather than
  publishing a spinner, authentication error, or half-loaded view. An honest
  empty state is acceptable when no safe sample data exists.
- Do not inspect another session's live app just to populate the App Inspector.
  Its agent-list view is the baseline; detailed inspector captures require an
  explicitly approved demo target.
- Review every exported image at full resolution before adding it to the manifest.
  Restore temporary capture styles and the prior theme/window size afterward.

### Equivalent views

All rows use the top of the page, with no modal open.

| ID / filename stem | Route | Reference state | Platforms |
|---|---|---|---|
| `dashboard` | `/` | Environment health cards and connected-device area | All |
| `dotnet-sdk` | `/dotnet-sdk` | Tracked channels and installed components; groups collapsed | All |
| `doctor` | `/doctor` | Completed analysis, summary and context; no repairs | All |
| `android-sdk` | `/android-sdk` | Installed-package view, categories collapsed | All |
| `android-devices` | `/devices` | Combined physical-device and emulator lists | All |
| `keystores` | `/keystores` | Keystore list, values hidden | All |
| `xcode` | `/xcode-management` | Installed Xcode tab; no downloads or selection changes | macOS |
| `apple-simulators` | `/apple-simulators` | Physical devices and simulator list; no device changes | macOS |
| `certificates` | `/certificates` | Certificates list; no actions | All |
| `profiles` | `/profiles` | Provisioning-profile list; no actions | All |
| `bundle-ids` | `/bundle-ids` | Identifier list; no capabilities dialog | All |
| `secrets` | `/secrets` | Root folder, values hidden | All |
| `publishing` | `/secrets/publish` | Publish-profile list; no review/publish action | All |
| `profiling` | `/profiling` | Session list or honest first-capture empty state | All |
| `app-inspector` | `/devflow` | Connected-agent list, no agent opened | All |
| `firebase-push` | `/firebase-push` | Default form, credentials/targets masked; never send | All |
| `apple-push` | `/push-testing` | Default APNs form, credentials/targets masked; never send | All |

### macOS build and DevFlow

```bash
dotnet build src/MauiSherpa.MacOS -f net10.0-macos
open -n --env MAUI_SHERPA_ENABLE_DEVFLOW=1 \
  "src/MauiSherpa.MacOS/bin/Debug/net10.0-macos/osx-arm64/MAUI Sherpa.app"
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
dotnet maui devflow ui screenshot --agent-port <port> --output <private-output.png>
```

Prefer one persistent `dotnet maui devflow batch --agent-port <port>` session
for multiple mutations. Separate CLI invocations can encounter the previous
invocation's mutation lease; do not force-take another client's lease. Batch
expressions should use single quotes inside their outer double quotes.

Verify readiness with the current heading and visible loading elements rather
than fixed sleeps. Use screenshot-only CSS to mask additional sensitive text;
remove it when finished. Do not replace real values with invented sample data.

## Adding platform captures

The shared integration branch is **`redth-website-refresh`**. Both platform
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
Check out the existing shared branch redth-website-refresh and pull its latest
changes. Add your platform-only changes to that branch and its existing PR.
Use docs/SCREENSHOTS.md and website/screenshots.json from the website refresh,
with website/images/screenshots/macos/ as the visual reference.

Build the current Windows head:
dotnet build src/MauiSherpa -f net10.0-windows10.0.19041.0
Launch the built Debug app and use MAUI DevFlow for navigation and screenshots.
Discover this Sherpa instance's port and target it explicitly. Prefer a single
persistent DevFlow batch session so captures share a mutation lease.

Match a 1440x960 logical-pixel window, dark app theme, 100% font scale, top scroll
position, and the states in the shared capture table. Keep real Windows/WinUI
chrome. Capture all 15 cross-platform IDs; do not capture xcode or apple-simulators.
Enable Demo Mode, then additionally mask identifying labels, paths, account and
project names, bundle IDs, secret names, tokens, and signing names. Review every
image for readable sensitive data. Use existing safe data or an honest empty
state, never fabricated UI or production data created just for marketing.

Navigate read-only: do not install/update SDKs or workloads, pair/start/stop
devices owned by other sessions, create/revoke certificates, publish secrets,
send notifications, or delete anything. Record unavailable or loading views as
blockers rather than publishing an error screen. Do not inspect someone else's
running app to populate the inspector.

Save lossless 1x WebP files as:
website/images/screenshots/windows/<manifest-id>.webp
Keep PNG originals private and outside the repository. Preserve aspect ratio
and full native window chrome. Add only the reviewed Windows paths to each
matching assets.windows entry in website/screenshots.json. Do not change macOS
assets, captions, support declarations, or website design.

Restore temporary UI preferences/styles. Run:
node --test tests/website.test.mjs
Return a contact sheet, the exact captured/missing IDs, dimensions, and any
blockers. Commit only your Windows assets and manifest entries, rebase onto the
latest origin/redth-website-refresh, and push to that existing shared branch.
Do not force-push, overwrite another platform's entries, or open another PR.
```

## Linux handoff prompt

```text
Capture the Linux equivalent of MAUI Sherpa's refreshed macOS website set.
Check out the existing shared branch redth-website-refresh and pull its latest
changes. Add your platform-only changes to that branch and its existing PR.
Use docs/SCREENSHOTS.md and website/screenshots.json from the website refresh,
with website/images/screenshots/macos/ as the visual reference.

Build and run the actual GTK head:
dotnet build src/MauiSherpa.LinuxGtk
dotnet run --project src/MauiSherpa.LinuxGtk --no-build
Inspect the current integration first: Sherpa's GTK DevFlow agent packages and
startup are currently disabled. Do not assume DevFlow can drive this app and do
not enable/upgrade those packages as part of this task. If still disabled, use
the desktop's native window capture and UI automation tools for the real GTK
window. Sherpa's App Inspector page is still a valid feature to capture.

Match a 1440x960 logical-pixel window, dark app theme, 100% font scale, top scroll
position, and the states in the shared capture table. Keep real Linux/GTK chrome
and record the distribution, desktop environment, and display scale. Capture
all 15 cross-platform IDs; do not capture xcode or apple-simulators.

Enable Demo Mode and additionally mask all identifying labels, local usernames
and paths, account/project names, signing names, bundle IDs, secret names, and
tokens. Review every image. Use existing safe data or honest empty states, never
fabricated UI. Do not install/update SDKs or workloads, start/stop other sessions'
devices, create/revoke signing assets, publish secrets, send notifications, or
delete anything. Record blocked/loading views instead of publishing them. Do
not inspect another session's app to populate the inspector.

Save lossless 1x WebP files as:
website/images/screenshots/linux/<manifest-id>.webp
Keep PNG originals private and outside the repository. Preserve aspect ratio
and the full native window. Add only reviewed Linux paths to each matching
assets.linux entry in website/screenshots.json. Do not change macOS assets,
captions, support declarations, app dependencies, or website design.

Restore temporary UI preferences/styles. Run:
node --test tests/website.test.mjs
Return a contact sheet, captured/missing IDs, dimensions, and blockers. Commit
only your Linux assets and manifest entries, rebase onto the latest
origin/redth-website-refresh, and push to that existing shared branch. Do not
force-push, overwrite another platform's entries, or open another PR.
```
