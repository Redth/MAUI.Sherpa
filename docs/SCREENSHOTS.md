# Screenshot Capture Process

This document describes how to capture a complete set of screenshots for MAUI Sherpa's README and documentation.

## Prerequisites

1. **MauiDevFlow CLI** installed and configured (see `dotnet-tools.json`)
2. **Android emulator** available (any API level at or above `SupportedOSPlatformVersion`)
3. **iOS simulator** booted (e.g., iPhone 17 Pro)
4. App built and running on Mac Catalyst

## Setup

### Build and Launch

```bash
cd src/MauiSherpa
dotnet build -f net10.0-maccatalyst
open "bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/MAUI Sherpa.app"
```

> **Important:** macOS `open` reuses a running app instance. If rebuilding, you must `kill` the old process first, then `open` the new binary. Check with `ps aux | grep MauiSherpa`.

### Verify Connectivity

```bash
cd src/MauiSherpa
dotnet maui-devflow MAUI status    # Agent connection
dotnet maui-devflow cdp status     # CDP (Blazor WebView)
```

### Enable Demo Mode

Demo Mode blurs sensitive values (Apple identity keys, cloud secrets, device UDIDs, certificate serials, etc.) so screenshots are safe to share publicly.

```bash
# Navigate to Settings and enable Demo Mode toggle
dotnet maui-devflow cdp Input dispatchClickEvent "a.nav-item[href='settings']"
sleep 2
dotnet maui-devflow cdp Input dispatchClickEvent ".toggle-switch"
# Verify it's on:
dotnet maui-devflow cdp Runtime evaluate "document.querySelector('.toggle-switch input[type=\"checkbox\"]').checked"
```

### Set Dark Mode (Optional)

```bash
dotnet maui-devflow cdp Runtime evaluate "document.body.classList.remove('theme-light'); document.body.classList.add('theme-dark'); document.querySelector('.main-layout').classList.remove('theme-light'); document.querySelector('.main-layout').classList.add('theme-dark');"
```

### Start Devices

- **Android emulator:** Navigate to the Emulators page in the app and click the play button on an AVD, or use `android avd start --name <avd-name>` from CLI.
- **iOS simulator:** Should already be booted (`xcrun simctl list devices booted`). If not, boot one from Apple Simulators page in the app.

## Screenshot Naming Convention

All screenshots use the prefix `MAUI.Sherpa_` and are saved to `docs/screenshots/`.

**Pattern:** `MAUI.Sherpa_<PageOrDialog>[_<Step>].png`

Examples:
- `MAUI.Sherpa_Dashboard.png` — A page screenshot
- `MAUI.Sherpa_Create_Profile_01.png` — Step 1 of a multi-step wizard
- `MAUI.Sherpa_Inspector_Android_Logcat.png` — Inspector tab screenshot

## Capture Commands

### Navigation

```bash
# Navigate via sidebar link
dotnet maui-devflow cdp Input dispatchClickEvent "a.nav-item[href='<page-href>']"

# Page hrefs: (empty string)=Dashboard, doctor, settings, devices, emulators,
#   android-sdk, keystores, apple-simulators, apple-devices, bundle-ids,
#   certificates, profiles, root-certificates
```

### Take Screenshot

```bash
# Always wait 2-3 seconds after navigation/interaction before capturing
sleep 2
dotnet maui-devflow MAUI screenshot --output docs/screenshots/MAUI.Sherpa_<Name>.png
```

### Fill Form Fields (Blazor)

```bash
# Use the native value setter pattern to trigger Blazor binding
dotnet maui-devflow cdp Runtime evaluate "
  const input = document.querySelector('<selector>');
  Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set.call(input, '<value>');
  input.dispatchEvent(new Event('input', { bubbles: true }));
"
```

### Scroll

```bash
# Blazor scrollable container is main.content, NOT document.body
dotnet maui-devflow cdp Runtime evaluate "document.querySelector('main.content').scrollTo(0, <pixels>)"
```

## Screenshot Inventory

### Pages (navigate to each via sidebar)

| Screenshot | Page href | Notes |
|------------|-----------|-------|
| `Dashboard` | `""` | Landing page with quick actions |
| `Doctor` | `doctor` | Before running check |
| `Doctor_Results` | `doctor` | After running check (click Run button, wait) |
| `Settings` | `settings` | Top of page showing Demo Mode toggle |
| `Settings_02` | `settings` | Scrolled to show Apple Identities (blurred) |
| `Android_SDK` | `android-sdk` | SDK packages list |
| `Emulators` | `emulators` | Emulator cards |
| `Android_Devices` | `devices` | With emulator running for inspector button |
| `Keystores` | `keystores` | Keystore cards |
| `Apple_Simulators` | `apple-simulators` | Simulator list with booted sim |
| `Apple_Devices` | `apple-devices` | Registered devices |
| `Bundle_IDs` | `bundle-ids` | Bundle ID list |
| `Certificates` | `certificates` | Certificate cards |
| `Profiles` | `profiles` | Provisioning profiles list |
| `Root_Certificates` | `root-certificates` | Root cert list |

### Dialogs (open from toolbar or card buttons)

| Screenshot | How to Open | Notes |
|------------|-------------|-------|
| `Create_Emulator` | Emulators → toolbar `+` button | Fill name field |
| `Create_Keystore` | Keystores → toolbar `+` button | Fill all form fields |
| `Keystore_Signatures` | Keystores → fingerprint icon on card | Shows cert signatures |
| `Keystore_PEPK` | Keystores → file-export icon on card | PEPK export dialog |
| `Register_Device` | Apple Devices → toolbar `+` button | Fill UDID and name |
| `Create_Simulator` | Apple Simulators → toolbar `+` button | Fill name |
| `Register_Bundle_ID` | Bundle IDs → toolbar `+` button | Fill identifier and name |
| `Bundle_Capabilities` | Bundle IDs → puzzle-piece icon on card | Capabilities editor |
| `Create_Certificate` | Certificates → toolbar `+` button | Fill common name |
| `Certificate_Export` | Certificates → file-export icon on card | Export dialog |
| `Edit_Profile` | Profiles → edit icon on a profile card | Edit/regenerate dialog |
| `Settings_Add_Identity` | Settings → Add Apple Identity button | Identity form |
| `Settings_Add_Cloud` | Settings → Add Cloud Provider button | Cloud provider form |

### Create Profile Wizard (5 steps)

| Screenshot | Step | Action |
|------------|------|--------|
| `Create_Profile_01` | 1 - Type | Select "Development" |
| `Create_Profile_02` | 2 - Bundle ID | Search "sherpa", select match |
| `Create_Profile_03` | 3 - Certificates | Select compatible certs |
| `Create_Profile_04` | 4 - Devices | Select some devices |
| `Create_Profile_05` | 5 - Name | Review and name (do NOT click Create) |

### CI Secrets Wizard (4 steps + publish sub-steps)

| Screenshot | Step | Action |
|------------|------|--------|
| `CI_Secrets_01` | 1 - Platform | iOS selected by default |
| `CI_Secrets_02` | 2 - Distribution | Select distribution type |
| `CI_Secrets_03` | 3 - Resources | Select bundle, cert, profile |
| `CI_Secrets_04` | 4 - Export | Shows secrets and export options |
| `CI_Secrets_Publish_01` | Publish 1 | Select CI/CD Provider (GitHub) |
| `CI_Secrets_Publish_02` | Publish 2 | Select Repository |
| `CI_Secrets_Publish_03` | Publish 3 | Select secrets to publish (do NOT click Publish) |

### Device Inspectors

| Screenshot | Tab | Notes |
|------------|-----|-------|
| `Inspector_Android_Logcat` | Logcat | Open from Devices page → search icon on emulator card |
| `Inspector_Android_Files` | Files | |
| `Inspector_Android_Shell` | Shell | |
| `Inspector_Android_Capture` | Capture | |
| `Inspector_Android_Tools` | Tools | |
| `Inspector_iOS_Logs` | Logs | Open from Apple Simulators page → search icon on booted sim |
| `Inspector_iOS_Apps` | Apps | |
| `Inspector_iOS_Capture` | Capture | |
| `Inspector_iOS_Tools` | Tools | |

## Important Notes

- **Always wait** 2-3 seconds after navigating, clicking, or entering text before capturing a screenshot. The UI may be in a loading state.
- **Never click final action buttons** on creation/publish dialogs during screenshot capture (don't create profiles, don't publish secrets, don't register devices).
- **Demo Mode must be ON** for all screenshots to blur sensitive values.
- For the Create Profile wizard, always select the bundle containing "sherpa" in the name.
- **Audit for new pages/dialogs** before each screenshot session by diffing against the last release tag (e.g., `git diff v0.1.1..HEAD --name-only -- src/MauiSherpa/Pages/ src/MauiSherpa/Components/`).
- **App relaunch gotcha:** After rebuilding, `open` on macOS reuses the old running instance. Kill the old PID first.

## Updating the README

After capturing all screenshots, update the `README.md` screenshots section. Each feature area should be in a `<details>` block with a summary title and the relevant screenshots as `<img>` tags referencing `docs/screenshots/MAUI.Sherpa_<Name>.png`.
