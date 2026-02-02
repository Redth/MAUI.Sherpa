# MauiSherpa Project - Development Status & Progress

## Project Overview

**MauiSherpa** - A .NET MAUI desktop application for managing developer tools (Android SDK, Apple Development Tools, GitHub Copilot)

- **Target Platforms**: Mac Catalyst (net10.0-maccatalyst) and Windows (net10.0-windows10.0.19041.0)
- **Framework**: .NET 10
- **Architecture**: Clean Architecture with Core (business logic) and Platform (UI) separation
- **UI Technology**: MAUI with Blazor Hybrid WebView (intended, but Blazor disabled due to build issues)

---

## Current Status

### Build Status: ✅ SUCCESS
- Both Core and Platform projects compile without errors
- Only warnings about nullable references in AlertService
- App bundle created at: `src/MauiSherpa/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/MauiSherpa.app`

### Runtime Status: ✅ SUCCESS
The Mac Catalyst app builds and launches successfully. The basic MAUI ContentPage (MainPage.cs) displays correctly.

### Root Cause Analysis
The app exits immediately, likely due to:
1. **Program.cs structure issue** - The current Program.cs builds the MauiApp but doesn't call `.Run()` properly
2. **Missing entry point configuration** - .NET 10 MAUI may require specific entry point pattern
3. **BlazorWebView package removed** - We removed `Microsoft.AspNetCore.Components.WebView.Maui` due to build errors, which may have broken Blazor integration
4. **Incompatible .NET 10 MAUI version** - .NET 10 is very new and may have stability issues

---

## What We've Done So Far

### Phase 1: Initial Project Setup ✅

1. Created solution structure:
   - `MauiSherpa.sln` - Root solution file
   - `src/MauiSherpa.Core/` - Business logic library
   - `src/MauiSherpa/` - MAUI platform application

2. **MauiSherpa.Core Project** (Building successfully):
   - Created `Interfaces.cs` with all service interfaces
   - Created `ViewModels/ViewModelBase.cs` with ObservableObject base class
   - Created `ObservableObject.cs` implementing INotifyPropertyChanged
   - Added packages: Microsoft.Extensions.DependencyInjection, FluentValidation, GitHub.Copilot.SDK
   - All ViewModels use stub services to avoid DI issues

3. **MauiSherpa Project** (Building successfully):
   - Created multi-target MAUI project targeting net10.0-maccatalyst and Windows
   - Added MAUI resources (icons, splash screen)
   - Created Blazor components and pages
   - Configured dependency injection in `Program.cs`

### Phase 2: Service Layer Implementation ✅

Created in `src/MauiSherpa/Services/`:

1. **PlatformService.cs** - Detects runtime platform using DeviceInfo
2. **LoggingService.cs** - Wraps Microsoft.Extensions.Logging
3. **AlertService.cs** - Native MAUI dialogs (uses `Application.Current.Windows[0].Page.DisplayAlertAsync`)
4. **NavigationService.cs** - Stub navigation service
5. **DialogService.cs** - Stub dialog service
6. **FileSystemService.cs** - File operations wrapper

### Phase 3: ViewModels Implementation ✅

Created in `src/MauiSherpa.Core/ViewModels/`:

1. **DashboardViewModel.cs** - Welcome message, navigation cards
2. **AndroidSdkViewModel.cs** - Loading state, status messages
3. **AppleToolsViewModel.cs** - Tools management, loading state
4. **CopilotViewModel.cs** - Chat interface, connection state, query/response
5. **SettingsViewModel.cs** - Preferences (notifications, dark mode, theme)

All ViewModels inherit from `ViewModelBase` with injected services.

### Phase 4: Blazor UI Components ✅

Created in `src/MauiSherpa/Pages/`:

1. **Dashboard.razor** - Navigation cards for each feature
2. **AndroidSdk.razor** - Load button, status display
3. **AppleTools.razor** - Similar to Android SDK
4. **Copilot.razor** - Chat interface with connection toggle
5. **Settings.razor** - Form controls for preferences

Created in `src/MauiSherpa/Components/`:

1. **App.razor** - Router component
2. **MainLayout.razor** - Sidebar navigation layout

Created `wwwroot/index.html` - HTML host for Blazor WebView

### Phase 5: MAUI Pages and Entry Point ✅

1. **MainPage.cs** - Simple MAUI ContentPage with labels
2. **App.cs** - Application class with CreateWindow override
3. **Program.cs** - Main entry point with DI configuration

---

## Current File Structure

```
MauiSherpa.sln
├── src/
│   ├── MauiSherpa.Core/
│   │   ├── MauiSherpa.Core.csproj          ✅ Building
│   │   ├── Interfaces.cs                 ✅ Service interfaces
│   │   ├── ViewModels/
│   │   │   ├── ViewModelBase.cs          ✅ Base class
│   │   │   ├── ObservableObject.cs        ✅ INotifyPC
│   │   │   ├── DashboardViewModel.cs      ✅ With stub services
│   │   │   ├── AndroidSdkViewModel.cs     ✅ With stub services
│   │   │   ├── AppleToolsViewModel.cs    ✅ With stub services
│   │   │   ├── CopilotViewModel.cs       ✅ With stub services
│   │   │   └── SettingsViewModel.cs      ✅ With stub services
│   │   └── Services/
│   │       └── StubServices.cs           ⚠️ Conflicts/errors
│   │
│   └── MauiSherpa/
│       ├── MauiSherpa.csproj       ✅ Building (no BlazorWebView)
│       ├── Program.cs                     ⚠️ Entry point issues
│       ├── App.cs                        ✅ Window creation
│       ├── MainPage.cs                    ✅ Simple ContentPage
│       ├── Services/
│       │   ├── PlatformService.cs          ✅ Platform detection
│       │   ├── LoggingService.cs          ✅ Logging wrapper
│       │   ├── AlertService.cs           ✅ Native alerts
│       │   ├── NavigationService.cs       ✅ Stub
│       │   ├── DialogService.cs           ✅ Stub
│       │   └── FileSystemService.cs       ✅ File operations
│       ├── Pages/
│       │   ├── Dashboard.razor           ✅ Ready but unused
│       │   ├── AndroidSdk.razor          ✅ Ready but unused
│       │   ├── AppleTools.razor          ✅ Ready but unused
│       │   ├── Copilot.razor             ✅ Ready but unused
│       │   └── Settings.razor            ✅ Ready but unused
│       ├── Components/
│       │   ├── App.razor                ✅ Router
│       │   └── MainLayout.razor         ✅ Sidebar
│       ├── wwwroot/
│       │   └── index.html               ✅ HTML host
│       └── Resources/
│           ├── AppIcon/                 ✅ Icons
│           └── Splash/                  ✅ Splash screen
```

---

## Critical Technical Issues

### Issue 1: Program.cs Entry Point (CRITICAL) 🔴

**Current Code:**
```csharp
public static void Main(string[] args)
{
    var builder = MauiApp.CreateBuilder();
    builder.UseMauiApp<App>()
           .ConfigureFonts(...)
           .ConfigureServices(...)  // ERROR: This method doesn't exist
           .Build();  // Missing .Run() call
}
```

**Errors encountered:**
- `MauiAppBuilder` doesn't have `ConfigureServices()` method
- `MauiApp` doesn't have `Run()` method
- Attempted various combinations but none work correctly

**Impact:** App crashes immediately on launch

### Issue 2: BlazorWebView Build Error (HIGH) 🟡

**Error:** `StaticWebAssetsPrepareForRun` target missing in project

**Impact:** Cannot use Blazor Hybrid WebView, had to remove `Microsoft.AspNetCore.Components.WebView.Maui` package

**Workaround:** Created simple ContentPage instead of BlazorWebView

### Issue 3: Service DI Registration (HIGH) 🟡

**Problem:** Services are registered directly on `builder.Services`, but this may not be the correct pattern for .NET 10 MAUI

**Impact:** Cannot properly configure dependency injection

### Issue 4: Silent App Crash (CRITICAL) 🔴

**Symptoms:**
- No error messages displayed
- No console output
- No crash reports
- Process exits silently

**Impact:** Cannot debug the application

---

## What Needs to Be Done Next

### Priority 1: Fix Program.cs Entry Point (CRITICAL) 🔴

The app crashes because Program.cs is incorrectly configured for .NET 10 MAUI.

**Investigation Tasks:**
1. Research correct .NET 10 MAUI Program.cs pattern
2. Check if MauiAppBuilder has different configuration methods
3. Determine if explicit Run() call is needed or handled differently
4. Consider creating a fresh .NET 10 MAUI project to compare Program.cs structure
5. Test if removing all service registrations helps isolate the issue

**Potential Solutions:**
- Find official .NET 10 MAUI documentation for startup
- Create test project to understand correct pattern
- Check if builder pattern changed in .NET 10

### Priority 2: Resolve Service DI Registration (HIGH) 🟡

Current error: `MauiAppBuilder` doesn't have `ConfigureServices()` method

**Research:**
- How to register services in .NET 10 MAUI
- Alternative methods for DI configuration
- Whether Services property on builder is correct

### Priority 3: Enable Blazor WebView Integration (MEDIUM) 🟢

Once app launches successfully:
1. Re-add `Microsoft.AspNetCore.Components.WebView.Maui` package
2. Update MainPage.cs to use BlazorWebView instead of simple ContentPage
3. Connect Blazor pages to ViewModels via @inject
4. Test navigation between pages

### Priority 4: Implement Actual Service Logic (LOW) 🟢

After UI works:
1. Implement real AndroidSdk.Tools integration
2. Implement real AppleDev.Tools integration
3. Implement real Copilot SDK integration
4. Replace stub services with real implementations

### Priority 5: Documentation (LOW) 🟢

1. Create SKILLS_AppleDev.md documentation
2. Update existing SKILLS_AndroidSdk.md if needed

---

## Important Technical Decisions

1. **Using .NET 10** - Latest framework for access to newest features
2. **Clean Architecture** - Core business logic separate from Platform UI
3. **MVVM Pattern** - ViewModels with ObservableObject for data binding
4. **Interface-First Design** - All services depend on interfaces for testability
5. **Stub Services in ViewModels** - Allows ViewModels to compile without DI errors during development
6. **Removed BlazorWebView** - Temporary removal due to build errors with StaticWebAssetsPrepareForRun target
7. **Multi-targeting** - Single project targeting both Mac Catalyst and Windows
8. **MAUI without XAML** - Using C#-only MAUI (source compilation) instead of XAML files

---

## Build Commands Reference

```bash
# Navigate to workspace
cd /Users/redth/code/MauiSherpa

# Build solution
dotnet build -f net10.0-maccatalyst

# Run app (currently failing)
dotnet run -f net10.0-maccatalyst

# Open app bundle (currently failing)
open src/MauiSherpa/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/MauiSherpa.app
```

---

## Next Session Strategy

1. **First Action**: Research or create a fresh .NET 10 MAUI sample to understand correct Program.cs structure
2. **Investigate**: Check .NET 10 MAUI documentation for entry point pattern
3. **Fix**: Update Program.cs with correct startup pattern
4. **Test**: Verify app launches without crashing
5. **Restore**: Re-enable BlazorWebView once MAUI entry point is fixed
6. **Complete**: Implement actual feature functionality

---

## Files to Focus On in Next Session

1. **`src/MauiSherpa/Program.cs`** - Entry point (CRITICAL)
2. **`src/MauiSherpa/MauiSherpa.csproj`** - May need BlazorWebView re-addition
3. **`src/MauiSherpa/MainPage.cs`** - Will need BlazorWebView integration
4. **`src/MauiSherpa/App.cs`** - May need updates based on Program.cs changes

---

## Key User Requests and Preferences

1. **Original Goal**: Create a desktop app for managing Android SDK, Apple Dev Tools, and Copilot
2. **Platform**: Mac Catalyst first, then Windows support
3. **UI Framework**: MAUI with Blazor Hybrid (Blazorise components preferred)
4. **Architecture**: Clean separation of concerns with DI
5. **Context Window Requested**: 128,000 tokens (user requested this but I cannot control it)

---

## Workspace Information

**Working Directory:** `/Users/redth/code/MauiSherpa`

**Solution File:** `/Users/redth/code/MauiSherpa/MauiSherpa.sln`

**Date:** Sun Feb 01 2026

**Platform:** macOS (darwin)

---

## Notes for Other AI Models

### Immediate Blocking Issue
The app cannot run at all - it crashes silently on launch. **Do not attempt to add new features** until the basic app launch is working.

### Recommended First Step
Create a minimal .NET 10 MAUI test project to understand the correct Program.cs structure:

```bash
dotnet new maui -n MauiSherpaTest -f net10.0-maccatalyst
cd MauiSherpaTest
# Compare Program.cs structure with the current one
```

### Dependencies to Check
- .NET 10 SDK version installed
- MAUI workload version
- Platform SDK versions (Xcode on Mac, Windows SDK on Windows)

### Debugging Tips
- Try running with verbose output: `dotnet run -f net10.0-maccatalyst --verbosity diagnostic`
- Check Console.app for macOS crash logs
- Try running on Windows if available to see if it's platform-specific

### Code Style
- No comments in code (unless requested)
- Follow existing naming conventions
- Use dependency injection pattern
- Maintain Clean Architecture separation

---

## End of Status Document

This document provides complete context for continuing development on the MauiSherpa project. The primary focus must be on fixing the Program.cs entry point to enable the app to launch successfully.
