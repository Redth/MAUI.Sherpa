<p align="center">
  <img src="docs/maui.sherpa.logo.png" width="150" alt="MAUI Sherpa Logo">
</p>

<h1 align="center">MAUI Sherpa</h1>

<p align="center">
  <em>Your guide to .NET MAUI development</em>
</p>

<p align="center">
  <a href="https://github.com/Redth/MAUI.Sherpa/actions/workflows/build.yml"><img src="https://github.com/Redth/MAUI.Sherpa/actions/workflows/build.yml/badge.svg" alt="Build"></a>
  <a href="https://opensource.org/licenses/MIT"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT"></a>
</p>

MAUI Sherpa is a desktop application for **macOS**, **Windows**, and **Linux** that helps manage your .NET MAUI development environment. It provides a unified interface for Android SDK management, Apple Developer tools, environment diagnostics, DevFlow app inspection, and GitHub Copilot integration.

🌐 **[Visit the website →](https://redth.github.io/MAUI.Sherpa/)**

![MAUI Sherpa Dashboard](website/images/screenshots/Dashboard.png)

## ✨ Features

### 🩺 MAUI Doctor
- Check your development environment health
- Diagnose .NET SDK, workloads, and dependencies
- AI-powered fix suggestions via Copilot
- One-click environment repairs

### 📦 Android SDK Management
- Browse and install SDK packages
- Manage platform tools, build tools, and system images
- Search and filter packages
- Track installed vs available packages

### 📱 Android Emulators
- Create, edit, and delete emulators
- Start and stop emulators
- Create snapshots for quick boot
- View emulator details and configuration

### 🔑 Android Keystores
- Create and manage Android signing keystores
- View certificate signatures and details
- Export PEPK keys for Google Play
- Cloud sync keystores across machines

### 🍎 Apple Developer Tools (macOS only)
- **Simulators**: Manage iOS/iPadOS/tvOS/watchOS simulators with built-in inspector
- **Bundle IDs**: Create and manage App IDs with capabilities editor
- **Devices**: Register devices for development and ad-hoc distribution
- **Certificates**: Create, download, export, and revoke signing certificates
- **Provisioning Profiles**: Create, edit, and manage profiles with CI secrets export
- **Root Certificates**: Install Apple root certificates for development

### 🔍 Device Inspectors
- **Android**: Logcat viewer, file browser, shell, screen capture, and device tools
- **iOS Simulator**: Log viewer, app manager, screen capture, and simulator tools

### 🔬 DevFlow App Inspector
- Remote visual tree inspection of running MAUI apps
- Interactive screenshot with element highlighting
- Network request monitoring with detailed views
- Application log streaming (native + WebView)
- Blazor WebView DOM inspection via CDP
- Live property editing (colors, sizes, brushes)

### 📋 Publish Profiles
- Bundle Apple + Android signing configs into reusable profiles
- Publish secrets to GitHub, GitLab, Azure DevOps repositories
- Multi-destination publishing with review workflow

### 🤖 GitHub Copilot Integration
- Chat with Copilot directly in the app
- Get AI-assisted help with your development environment
- Suggested prompts for common tasks

## 📸 Screenshots

<details>
<summary><strong>🩺 Doctor</strong></summary>

![Doctor Analyzing](website/images/screenshots/Doctor-Analyzing.png)
![Doctor Results](website/images/screenshots/Doctor-Results.png)

</details>

<details>
<summary><strong>📦 Android SDK Packages</strong></summary>

![Android SDK](website/images/screenshots/Android-SDK-Packages-List.png)

</details>

<details>
<summary><strong>📱 Android Emulators</strong></summary>

![Emulators](website/images/screenshots/Android-Emulators-List.png)

</details>

<details>
<summary><strong>📲 Android Devices</strong></summary>

![Android Devices](website/images/screenshots/Android-Devices-List.png)

</details>

<details>
<summary><strong>🔑 Android Keystores</strong></summary>

![Keystores](website/images/screenshots/Android-Keystores-List.png)
![Create Keystore](website/images/screenshots/Android-Keystores-Create.png)
![Keystore Signatures](website/images/screenshots/Android-Keystores-Signatures.png)

</details>

<details>
<summary><strong>🍎 Apple Simulators</strong></summary>

![Apple Simulators](website/images/screenshots/Apple-Simulators-Devices-List.png)

</details>

<details>
<summary><strong>🍎 Apple Registered Devices</strong></summary>

![Apple Devices](website/images/screenshots/Apple-Registered-Devices-List.png)
![Register Device](website/images/screenshots/Apple-Registered-Devices-Create.png)

</details>

<details>
<summary><strong>🍎 Apple Bundle IDs</strong></summary>

![Bundle IDs](website/images/screenshots/Apple-BundleIDs-List.png)
![Register Bundle ID](website/images/screenshots/Apple-BundleIDs-Create.png)
![Bundle Capabilities](website/images/screenshots/Apple-BundleIDs-Edit-Capabilities.png)

</details>

<details>
<summary><strong>🍎 Apple Certificates</strong></summary>

![Certificates](website/images/screenshots/Apple-Certificates-List.png)

</details>

<details>
<summary><strong>🍎 Apple Provisioning Profiles</strong></summary>

![Provisioning Profiles](website/images/screenshots/Apple-Profiles-List.png)
![Edit Profile](website/images/screenshots/Apple-Profiles-Edit.png)

</details>

<details>
<summary><strong>🍎 Root Certificates</strong></summary>

![Root Certificates](website/images/screenshots/Apple-Root-Intermediate-Certificates.png)

</details>

<details>
<summary><strong>🔍 Android Device Inspector</strong></summary>

![Logcat](website/images/screenshots/Android-Inspector-Logcat.png)
![Files](website/images/screenshots/Android-Inspector-Files.png)
![Shell](website/images/screenshots/Android-Inspector-Shell.png)
![Capture](website/images/screenshots/Android-Inspector-Capture.png)
![Tools](website/images/screenshots/Android-Inspector-Tools.png)
![Apps](website/images/screenshots/Android-Inspector-Apps.png)

</details>

<details>
<summary><strong>🔍 iOS Simulator Inspector</strong></summary>

![Logs](website/images/screenshots/Apple-Inspector-Logs.png)
![Apps](website/images/screenshots/Apple-Inspector-Apps.png)
![Capture](website/images/screenshots/Apple-Inspector-Capture.png)
![Tools](website/images/screenshots/Apple-Inspector-Tools.png)

</details>

<details>
<summary><strong>🔬 DevFlow App Inspector</strong></summary>

![Agent List](website/images/screenshots/DevFlow-Agent-List.png)
![Visual Tree](website/images/screenshots/DevFlow-VisualTree.png)
![Network](website/images/screenshots/DevFlow-Network.png)
![Logs](website/images/screenshots/DevFlow-Logs.png)
![WebView](website/images/screenshots/DevFlow-WebView.png)

</details>

<details>
<summary><strong>📋 Publish Profiles</strong></summary>

![Publish Profiles](website/images/screenshots/Publish-Profiles-List.png)
![Create Profile](website/images/screenshots/Publish-Profiles-Create-General-Tab.png)

</details>

<details>
<summary><strong>⚙️ Settings</strong></summary>

![Settings](website/images/screenshots/Settings.png)

</details>

<details>
<summary><strong>🤖 GitHub Copilot</strong></summary>

![Copilot Chat](website/images/screenshots/Copilot-Chat.png)

</details>

## 🚀 Getting Started

### Installation

Download the latest release from the [Releases](https://github.com/Redth/MAUI.Sherpa/releases) page, or see the [Getting Started guide](https://redth.github.io/MAUI.Sherpa/getting-started.html) for detailed instructions.

#### macOS (Homebrew)
```bash
brew install --cask redth/tap/maui-sherpa
```

#### macOS (Manual)
1. Download `MAUI-Sherpa.macos.zip`
2. Extract and move `MAUI Sherpa.app` to Applications
3. Right-click and select "Open" on first launch (to bypass Gatekeeper)

#### Windows
1. Download `MAUI-Sherpa.windows-x64.zip` or `MAUI-Sherpa.windows-arm64.zip`
2. Extract to your preferred location
3. Run `MauiSherpa.exe`

#### Linux
Download AppImage, .deb, or Flatpak from the releases page for your architecture (x64 or arm64).

### Apple Developer Tools Setup

To use the Apple Developer tools, you'll need to configure your App Store Connect credentials:

1. Go to [App Store Connect](https://appstoreconnect.apple.com/) → Users and Access → Integrations → Individual Keys
2. Create a new API key with "Developer" access
3. Download the `.p8` key file
4. In MAUI Sherpa, click the identity picker and add your credentials:
   - **Issuer ID**: Found on the Keys page
   - **Key ID**: The ID of your API key
   - **Private Key**: Contents of the `.p8` file

Your credentials are stored securely in the system keychain.

### GitHub Copilot Setup

To use the Copilot integration:

1. Install [GitHub Copilot CLI](https://docs.github.com/en/copilot/github-copilot-in-the-cli)
2. Authenticate with `gh auth login`
3. MAUI Sherpa will automatically detect and connect to Copilot

## 🛠️ Building from Source

```bash
# Clone the repository
git clone https://github.com/Redth/MAUI.Sherpa.git
cd MAUI.Sherpa

# Restore dependencies
dotnet restore

# Build for macOS (AppKit)
dotnet build src/MauiSherpa.MacOS -f net10.0-macos

# Build for Mac Catalyst
dotnet build src/MauiSherpa -f net10.0-maccatalyst

# Build for Windows
dotnet build src/MauiSherpa -f net10.0-windows10.0.19041.0

# Run tests
dotnet test
```

## 🏗️ Project Structure

```
MAUI.Sherpa/
├── src/
│   ├── MauiSherpa/               # Main MAUI Blazor Hybrid app
│   │   ├── Components/           # Reusable Blazor components
│   │   ├── Pages/                # Blazor page components
│   │   ├── Services/             # Platform-specific services
│   │   └── Platforms/            # Platform code (MacCatalyst, Windows)
│   ├── MauiSherpa.MacOS/         # macOS AppKit app head
│   ├── MauiSherpa.LinuxGtk/      # Linux GTK4 app head
│   ├── MauiSherpa.Core/          # Business logic library
│   │   ├── Handlers/             # Mediator request handlers
│   │   ├── Requests/             # Request records
│   │   ├── Services/             # Service implementations
│   │   └── ViewModels/           # MVVM ViewModels
│   └── MauiSherpa.Workloads/     # .NET workload querying library
├── tests/
│   ├── MauiSherpa.Core.Tests/    # Core library tests
│   └── MauiSherpa.Workloads.Tests/ # Workloads library tests
├── website/                      # GitHub Pages website
└── docs/                         # Documentation
```

## 🧪 Running Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test tests/MauiSherpa.Core.Tests
```

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- [.NET MAUI](https://github.com/dotnet/maui) - Cross-platform UI framework
- [Platform.Maui.MacOS](https://github.com/nicoleeldridge/mauiplatforms) - macOS AppKit backend for .NET MAUI
- [Platform.Maui.Linux.Gtk4](https://github.com/nicoleeldridge/Maui.Gtk) - Linux GTK4 backend for .NET MAUI
- [Shiny.Mediator](https://github.com/shinyorg/mediator) - Mediator pattern with caching
- [AndroidSdk](https://github.com/redth/androidsdk.tool) - Android SDK management APIs
- [AppleDev.Tools](https://github.com/redth/appledev.tools) - Apple Developer Tools APIs and AppStoreConnect API client
- [MauiDevFlow](https://github.com/Redth/MauiDevFlow) - Remote app inspection agent
- [GitHub Copilot](https://github.com/github/copilot-sdk) - AI-powered assistance via Copilot SDK
