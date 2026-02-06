<p align="center">
  <img src="docs/maui.sherpa.logo.png" width="150" alt="MAUI Sherpa Logo">
</p>

<h1 align="center">MAUI Sherpa</h1>

<p align="center">
  <em>Your guide to managing .NET MAUI dev environments, Apple Developer resources, Android SDKs, and CI/CD workflows — all in one app.</em>
</p>

<p align="center">
  <a href="https://github.com/Redth/MAUI.Sherpa/actions/workflows/build.yml"><img src="https://github.com/Redth/MAUI.Sherpa/actions/workflows/build.yml/badge.svg" alt="Build"></a>
  <a href="https://opensource.org/licenses/MIT"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT"></a>
</p>

MAUI Sherpa is a .NET MAUI Blazor Hybrid desktop app for **macOS** (Mac Catalyst) that helps manage your .NET MAUI development environment. It provides a unified interface for Apple Developer tools, Android SDK management, environment diagnostics, CI/CD secret management, and GitHub Copilot integration.

> **Note:** Windows support is planned but not yet available.

![MAUI Sherpa Dashboard](docs/screenshots/MAUI_Sherpa_Dashboard.png)

## ✨ Features

### 🤖 GitHub Copilot Integration
- Chat with Copilot directly in the app with context-aware tool access
- AI-powered fix suggestions for environment issues found by Doctor
- Copilot can query and manage Apple Developer resources, Android SDK, and more on your behalf

### 🩺 MAUI Doctor
- Check your development environment health at a glance
- Diagnose .NET SDK versions, workloads, and platform dependencies
- Detect missing or outdated tools (Xcode, Android SDK, JDK, etc.)
- AI-powered fix suggestions via Copilot integration

### 📦 Android SDK Management
- Browse and install SDK packages (platforms, build tools, system images)
- Search, filter, and track installed vs available packages
- Configure SDK and JDK paths

### 📱 Android Emulators
- Create, edit, and delete Android emulators
- Start and stop emulators with snapshot support
- View emulator details and configuration

### 🍎 Apple Developer Tools (macOS)
- **Bundle IDs**: Create and manage App IDs with capability configuration
- **Devices**: Register devices for development and ad-hoc distribution
- **Certificates**: Create, download, revoke, and sync signing certificates to local keychain
- **Provisioning Profiles**: Create, edit, and manage development and distribution profiles
- **Root Certificates**: Install Apple root certificates for development
- **Multiple Identities**: Switch between multiple App Store Connect API accounts

### 🔐 CI/CD Secrets & Cloud Storage
- **CI Secrets Wizard**: Step-by-step workflow to export Apple signing certificates, provisioning profiles, and identities for CI/CD pipelines
- **Cloud Secrets Storage**: Store and sync secrets with Azure Key Vault, AWS Secrets Manager, Google Secret Manager, or Infisical
- **CI/CD Publishers**: Automatically publish secrets to GitHub Actions, Azure DevOps, and other CI/CD systems
- **Backup & Restore**: Password-protected encrypted export/import of all settings and credentials (AES-256-GCM)

### ⚡ Performance
- Request-level caching via Shiny.Mediator for fast, responsive UI
- Background refresh with animated indicators and toast notifications
- Smart cache invalidation when data changes

## 📸 Screenshots

<details>
<summary><strong>Copilot</strong></summary>

![Copilot](docs/screenshots/MAUI_Sherpa_Copilot.png)

</details>

<details>
<summary><strong>Doctor</strong></summary>

![Doctor 1](docs/screenshots/MAUI_Sherpa_Doctor_01.png)
![Doctor 2](docs/screenshots/MAUI_Sherpa_Doctor_02.png)
![Doctor 3](docs/screenshots/MAUI_Sherpa_Doctor_03.png)

</details>

<details>
<summary><strong>Android SDK Packages</strong></summary>

![Android SDK Packages](docs/screenshots/MAUI_Sherpa_Android_SDK_Packages.png)

</details>

<details>
<summary><strong>Android Emulators</strong></summary>

![Android Emulators 1](docs/screenshots/MAUI_Sherpa_Android_Emulators_01.png)
![Android Emulators 2](docs/screenshots/MAUI_Sherpa_Android_Emulators_02.png)
![Android Emulators 3](docs/screenshots/MAUI_Sherpa_Android_Emulators_03.png)

</details>

<details>
<summary><strong>Apple Bundle IDs</strong></summary>

![Apple Bundle IDs](docs/screenshots/MAUI_Sherpa_Apple_Bundle_IDs.png)

</details>

<details>
<summary><strong>Apple Devices</strong></summary>

![Apple Devices](docs/screenshots/MAUI_Sherpa_Apple_Devices.png)

</details>

<details>
<summary><strong>Apple Certificates</strong></summary>

![Apple Certificates 1](docs/screenshots/MAUI_Sherpa_Apple_Certificates_01.png)
![Apple Certificates 2](docs/screenshots/MAUI_Sherpa_Apple_Certificates_02.png)

</details>

<details>
<summary><strong>Apple Provisioning Profiles</strong></summary>

![Apple Provisioning Profiles](docs/screenshots/MAUI_Sherpa_Apple_Provisioning_Profiles.png)

</details>

<details>
<summary><strong>Apple Root Certificates</strong></summary>

![Apple Root Certificates](docs/screenshots/MAUI_Sherpa_Apple_Root_Certificates.png)

</details>

## 🚀 Getting Started

### Download

Download the latest build from the [Actions](https://github.com/Redth/MAUI.Sherpa/actions/workflows/build.yml) page (select the latest successful run and download the `MauiSherpa-MacCatalyst` artifact).

#### macOS
1. Download and extract `MauiSherpa-MacCatalyst.zip`
2. Move `MAUI Sherpa.app` to `/Applications`
3. Remove the quarantine flag (required until notarization is enabled):
   ```bash
   sudo xattr -rd com.apple.quarantine "/Applications/MAUI Sherpa.app"
   ```
4. Launch the app

### Apple Developer Tools Setup

To use the Apple Developer tools, you'll need App Store Connect API credentials:

1. Go to [App Store Connect](https://appstoreconnect.apple.com/) → Users and Access → Integrations → Individual Keys
2. Create a new API key with **Developer** or **Admin** access
3. Download the `.p8` key file
4. In MAUI Sherpa, go to **Settings → Apple Identities** and add your credentials:
   - **Name**: A friendly label for this account
   - **Issuer ID**: Found on the Keys page
   - **Key ID**: The ID of your API key
   - **Private Key**: Browse to the `.p8` file or paste its contents

Your credentials are stored securely in the macOS Keychain. You can add multiple identities and switch between them using the identity picker on any Apple page.

### GitHub Copilot Setup

To use the Copilot integration:

1. Install [GitHub Copilot CLI](https://docs.github.com/en/copilot/github-copilot-in-the-cli)
2. Authenticate with `gh auth login`
3. MAUI Sherpa will automatically detect and connect to Copilot

## 🛠️ Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (see `global.json` for exact version)
- .NET MAUI workload: `dotnet workload install maui`
- **macOS**: Xcode 26.2+ (for Mac Catalyst builds)

### Build & Run

```bash
# Clone the repository
git clone https://github.com/Redth/MAUI.Sherpa.git
cd MAUI.Sherpa

# Restore and build for Mac Catalyst
dotnet build src/MauiSherpa -f net10.0-maccatalyst

# Run on Mac Catalyst
dotnet build src/MauiSherpa -f net10.0-maccatalyst -t:Run

# Run tests
dotnet test tests/MauiSherpa.Core.Tests
dotnet test tests/MauiSherpa.Workloads.Tests
```

### Publishing

```bash
# Publish Mac Catalyst (Release, signed)
dotnet publish src/MauiSherpa/MauiSherpa.csproj \
  -f net10.0-maccatalyst \
  -c Release \
  -p:CreatePackage=false \
  -p:EnableCodeSigning=true \
  -p:CodesignKey="Your Signing Identity"
```

## 🏗️ Architecture

MAUI Sherpa follows a **clean architecture** pattern with the MAUI Blazor Hybrid app layer separated from core business logic.

```
MAUI.Sherpa/
├── src/
│   ├── MauiSherpa/               # MAUI Blazor Hybrid app
│   │   ├── Components/           # Reusable Blazor components
│   │   ├── Pages/                # Blazor page components
│   │   ├── Services/             # Platform-specific service implementations
│   │   └── Platforms/            # Platform entry points (MacCatalyst, Windows)
│   ├── MauiSherpa.Core/          # Business logic (no platform dependencies)
│   │   ├── Handlers/             # Shiny.Mediator request handlers with caching
│   │   ├── Requests/             # Mediator request/command records
│   │   ├── Services/             # Service implementations
│   │   └── ViewModels/           # MVVM ViewModels
│   └── MauiSherpa.Workloads/     # .NET SDK workload querying library
│       ├── Models/               # Workload data models
│       ├── Services/             # SDK and workload services
│       └── NuGet/                # NuGet client for package queries
├── tests/
│   ├── MauiSherpa.Core.Tests/
│   └── MauiSherpa.Workloads.Tests/
└── docs/
```

### Key Patterns

- **Shiny.Mediator**: Request/Handler pattern with built-in caching for API calls
- **Dependency Injection**: Services registered in `MauiProgram.cs`, injected via constructor or `@inject`
- **No XAML**: All UI is built with Blazor Razor components
- **Secure Storage**: Apple identities and sensitive credentials stored in macOS Keychain via `ISecureStorageService`

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
- [Shiny.Mediator](https://github.com/shinyorg/mediator) - Mediator pattern with caching
- [AndroidSdk](https://github.com/AvantiPoint/androidsdk.tool) - Android SDK management APIs
- [AppleDev.Tools](https://github.com/AvantiPoint/appledev.tools) - Apple Developer Tools APIs and App Store Connect API client
- [GitHub Copilot](https://github.com/github/copilot-sdk) - AI-powered assistance via Copilot SDK
