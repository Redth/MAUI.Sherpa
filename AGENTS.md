# MauiSherpa - AI Agent Instructions

## Project Overview

MauiSherpa is a .NET 10 MAUI Blazor Hybrid desktop application for managing developer tools:
- Android SDK (packages, emulators, devices)
- Android Keystores (creation, signatures, PEPK export, cloud sync)
- Apple Developer Tools (certificates, profiles, devices, bundle IDs)
- .NET MAUI Doctor (dependency checking and workload management)
- GitHub Copilot integration

**Platforms:** Mac Catalyst, Windows
**Bundle Identifier:** `codes.redth.mauisherpa`

## Project Structure

```
MAUI.Sherpa/
├── src/
│   ├── MauiSherpa.Core/          # Business logic, ViewModels, interfaces, services
│   │   ├── Handlers/             # Shiny.Mediator request handlers
│   │   ├── Requests/             # Mediator request records
│   │   ├── Services/             # Service implementations
│   │   ├── ViewModels/           # MVVM ViewModels
│   │   └── Interfaces.cs         # All interface definitions
│   ├── MauiSherpa/               # MAUI app with Blazor UI
│   │   ├── Components/           # Reusable Blazor components
│   │   ├── Pages/                # Blazor page components
│   │   ├── Services/             # Platform-specific service implementations
│   │   ├── Platforms/            # Platform-specific code (MacCatalyst, Windows)
│   │   └── wwwroot/              # Static assets (CSS, JS, index.html)
│   └── MauiSherpa.Workloads/     # .NET SDK workload querying library
│       ├── Models/               # Workload data models
│       ├── Services/             # Workload services
│       └── NuGet/                # NuGet client for package queries
├── tests/
│   ├── MauiSherpa.Core.Tests/    # Unit tests for Core library
│   └── MauiSherpa.Workloads.Tests/ # Unit tests for Workloads library
└── docs/                         # Documentation
```

## Build Commands

```bash
# Build for Mac Catalyst
dotnet build src/MauiSherpa -f net10.0-maccatalyst

# Build for Windows (on Windows only)
dotnet build src/MauiSherpa -f net10.0-windows10.0.19041.0

# Build entire solution (uses default TFM for each project)
dotnet build MauiSherpa.sln

# Run all tests
dotnet test MauiSherpa.sln

# Publish Mac Catalyst app
dotnet publish src/MauiSherpa -f net10.0-maccatalyst -c Release

# Publish Windows app
dotnet publish src/MauiSherpa -f net10.0-windows10.0.19041.0 -c Release
```

### Launching the App

**IMPORTANT:** `dotnet run` does NOT work for .NET MAUI apps (until .NET 11). Use one of:
```bash
# Option 1: Build with -t:Run target (keeps process alive until app exits)
dotnet build src/MauiSherpa -f net10.0-maccatalyst -t:Run

# Option 2: Build then manually open the .app bundle
dotnet build src/MauiSherpa -f net10.0-maccatalyst
open "src/MauiSherpa/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/MAUI Sherpa.app"
```

**Always launch from `bin/` path**, NOT `artifacts/`. The `artifacts/` copy may be stale and missing DLLs.

## Architecture Patterns

### Clean Architecture
- **Core** contains no platform dependencies
- **Platform** implements interfaces defined in Core
- Services are injected via constructor DI

### MVVM with Dependency Injection
ViewModels inherit from `ViewModelBase` → `ObservableObject`:
```csharp
public class MyViewModel : ViewModelBase
{
    public MyViewModel(IAlertService alertService, ILoggingService logger)
        : base(alertService, logger) { }
}
```

### Shiny.Mediator for Caching
Request/Handler pattern with automatic caching:
```csharp
// Request (in Core/Requests/)
[Cache(AbsoluteExpirationSeconds = 300)]
public record GetDataRequest() : IRequest<IReadOnlyList<Data>>;

// Handler (in Core/Handlers/)
public class GetDataHandler : IRequestHandler<GetDataRequest, IReadOnlyList<Data>>
{
    public async Task<IReadOnlyList<Data>> Handle(GetDataRequest request, IMediatorContext context, CancellationToken ct)
    {
        // Implementation
    }
}
```

Handlers must be registered in `MauiProgram.cs`:
```csharp
builder.Services.AddSingletonAsImplementedInterfaces<GetDataHandler>();
```

**IMPORTANT:** `Mediator.Request()` returns a tuple `(IMediatorContext Context, TResult Result)` — use `.Result` to get the value.

### Service Registration
In `MauiProgram.cs`:
```csharp
builder.Services.AddSingleton<IMyService, MyService>();
builder.Services.AddSingleton<MyViewModel>();
```

## Core Interfaces

| Interface | Purpose |
|-----------|---------|
| `IAlertService` | Native dialogs and toasts (`ShowConfirmAsync`, NOT `ShowConfirmationAsync`) |
| `ILoggingService` | Structured logging to `~/Library/Application Support/MauiSherpa/logs/` |
| `INavigationService` | Page navigation |
| `IDialogService` | Loading indicators, input dialogs, file pickers |
| `IAndroidSdkService` | Android SDK operations |
| `IOpenJdkSettingsService` | OpenJDK location detection and override |
| `IKeystoreService` | Android keystore creation, signatures, PEPK export |
| `IKeystoreSyncService` | Cloud sync for Android keystores |
| `IAppleConnectService` | App Store Connect API |
| `IAppleIdentityService` | Apple credential management |
| `IDoctorService` | MAUI dependency checking |
| `ICloudSecretsService` | Cloud secret storage (uses `byte[]` for values) |
| `ISecureStorageService` | Local secure storage (Keychain on macOS) |

## UI Patterns

### Blazor Pages
- Located in `src/MauiSherpa/Pages/`
- Use `@inject` for services
- Use mediator for cached data: `await Mediator.Request(new GetDataRequest())`

### Modals
- `OperationModal` — Single long-running operation with progress
- `MultiOperationModal` — Batch operations with per-item progress
- `ProcessExecutionModal` — CLI process with terminal output

**OperationModalService.RunAsync** signature:
```csharp
RunAsync(string title, string description, Func<IOperationContext, Task<bool>> operation, bool canCancel = true)
```
Both `title` AND `description` are required. Callback returns `Task<bool>`.

**Per-page modal CSS:** Each Blazor page defines its own `.modal-overlay`, `.modal`, `.modal-header`, `.modal-body`, `.modal-footer` CSS in a `<style>` block. These are NOT global styles. **New pages MUST include modal CSS or modals will render inline without overlay/positioning.**

### Modal Keyboard Navigation
All modals use `modalInterop.js` (`wwwroot/js/modalInterop.js`) for focus trapping:
- Tab/Shift+Tab cycles through focusable elements within the modal
- Escape closes the modal
- Auto-focuses `.btn-primary:not([disabled])` on open

**CRITICAL:** In Blazor WebView (Mac Catalyst), browser default Tab navigation does NOT work. All Tab keypresses must be intercepted with `preventDefault()` and explicit `.focus()` calls via JS interop.

### Text Selection Prevention
Global `user-select: none` is applied on `*` to prevent accidental text selection in the hybrid app. Selectively re-enabled on: `input`, `textarea`, `select`, `code`, `pre`, `.mono`, `.terminal-output`, `.log-entry`, `.error-message`, `.chat-message`, and `.text-selectable`.

### Icons
Using Font Awesome. Example:
```razor
<i class="fa-solid fa-check text-success"></i>
```

### Theming
- CSS variables defined in `app.css` with `.theme-light` and `.theme-dark` overrides
- **Always validate UI changes in BOTH light and dark mode**
- Use `var(--text-primary)`, `var(--bg-tertiary)`, `var(--card-bg)`, etc. — never hardcode colors
- Service-specific tags (Google, Firebase, Facebook) need explicit dark mode overrides with `rgba()` backgrounds

### UI Quality Checklist
When making or reviewing UI changes, always verify:
- Padding around elements is consistent and looks polished
- Button alignment is consistent across the app
- Icon usage is appropriate (correct icon, right size)
- Text and button sizes are proportional and readable
- Both light AND dark mode look good

## Code Conventions

- **No XAML** — All UI is Blazor (.razor) or C# code
- **Nullable enabled** — All projects use `<Nullable>enable</Nullable>`
- **Property notification** — Use `SetProperty(ref field, value)` in ViewModels
- **Async naming** — Suffix async methods with `Async`
- **Records for DTOs** — Use record types for data transfer objects
- **NuGet packages for APIs** — Use official NuGet packages (Octokit, AppStoreConnectClient, etc.) instead of raw HttpClient for external API integrations

## Platform-Specific Notes

### Mac Catalyst

**App data path:** Use `AppDataPath.GetAppDataDirectory()` which returns `~/Library/Application Support/MauiSherpa/`. Do NOT use `SpecialFolder.ApplicationData` (resolves to `~/Documents/.config/` which is TCC-protected).

**Secure storage in Debug:** Ad-hoc Debug builds have different code signatures each rebuild, making macOS Keychain entries inaccessible. Debug builds always use fallback file storage (`#if DEBUG _usesFallback = true;` in `SecureStorageService`).

**File save dialogs:** `PickSaveFileAsync` (native `NSSavePanel`) creates an empty file at the chosen path. Tools like `keytool` that refuse to overwrite existing files need the empty file deleted first.

**Mac Catalyst delegate:** Use `DidPickDocumentAtUrls` (plural), NOT `DidPickDocument` (singular). The singular form doesn't fire on modern Mac Catalyst.

**Hardened runtime:** MSBuild `EnableHardenedRuntime=true` does NOT work for .NET MAUI Mac Catalyst. Must re-sign with `codesign --force --options runtime --timestamp` after publish.

**Logging:** Logs saved to `~/Library/Application Support/MauiSherpa/logs/maui-sherpa-{yyyy-MM-dd}.log`.

### Blazor WebView Gotchas

**`@bind` vs JS DOM manipulation:** Setting `element.value = 'x'` via JS does NOT update Blazor two-way binding. Must use the native value setter pattern:
```javascript
Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set.call(input, value);
input.dispatchEvent(new Event('input', { bubbles: true }));
```
Use `@bind:event="oninput"` on inputs for this to work (requires `input` events, not `change`).

## MauiDevFlow (AI Debugging)

The project uses [MauiDevFlow](https://github.com/nicoleeldridge/MauiDevFlow) for AI-assisted debugging. Port **9231** is configured in `.mauidevflow` and `Directory.Build.props`.

```bash
# Always run CLI commands from src/MauiSherpa/ for auto port detection
cd src/MauiSherpa

# Check agent connectivity
dotnet maui-devflow MAUI status

# Take screenshots
dotnet maui-devflow MAUI screenshot --output screen.png

# Blazor DOM snapshot (best for AI)
dotnet maui-devflow cdp snapshot

# Inject dark/light mode for testing (avoids navigating to Settings)
dotnet maui-devflow cdp Runtime evaluate "document.body.classList.remove('theme-light'); document.body.classList.add('theme-dark'); document.querySelector('.main-layout').classList.remove('theme-light'); document.querySelector('.main-layout').classList.add('theme-dark');"
```

## CI/CD

- **GitHub Actions runner:** `macos-26` (NOT `macos-26-arm64`)
- **Xcode:** Select `Xcode_26.2.app` (NOT `Xcode_26.2.0.app` — the `.0` variant has broken SDK lookups)
- **Workflow file:** `.github/workflows/build.yml`

## Key Dependencies

| Package | Purpose |
|---------|---------|
| Microsoft.Maui.Controls | MAUI framework |
| Microsoft.AspNetCore.Components.WebView.Maui | Blazor Hybrid |
| Shiny.Mediator | Request/Response with caching |
| AndroidSdk | Android SDK management |
| AppStoreConnectClient | Apple API client |
| FluentValidation | Input validation |

## Testing

- **xUnit** for test framework
- **Moq** for mocking
- **FluentAssertions** for assertions

Test projects target `net10.0` (not platform-specific) for portability.

## Debugging Workflow

When debugging UI issues:
1. Launch the app, then **ask the user to navigate/reproduce** the issue before inspecting — the app may require user interaction to reach the desired state
2. When using logs for debugging, **wait for the user to confirm** they've performed the action before checking log files
3. Use screenshots (`screencapture` for macOS, `xcrun simctl io screenshot` for iOS simulators) to visually verify the app
4. **Always back up modified files** before git branching/splitting operations to enable restoration if changes are lost
