# MauiSherpa Copilot Instructions

## Project Overview

MauiSherpa is a .NET MAUI desktop application for managing developer tools (Android SDK, Apple Development Tools, GitHub Copilot). It targets Mac Catalyst and Windows using .NET 10.

## Build Commands

```bash
# Build for Mac Catalyst
dotnet build -f net10.0-maccatalyst

# Build for Windows (on Windows only)
dotnet build -f net10.0-windows10.0.19041

# Run on Mac Catalyst
dotnet run --project src/MauiSherpa -f net10.0-maccatalyst

# Verbose build for debugging
dotnet build -f net10.0-maccatalyst --verbosity diagnostic
```

## Architecture

### Project Structure

- **MauiSherpa.Core**: Business logic library (ViewModels, interfaces, services contracts)
- **MauiSherpa**: MAUI application with platform-specific implementations

### Key Patterns

**Clean Architecture**: Core contains no platform dependencies. Platform implements interfaces defined in Core.

**MVVM with DI**: ViewModels inherit from `ViewModelBase` → `ObservableObject`. Services are injected via constructor:

```csharp
public class MyViewModel : ViewModelBase
{
    public MyViewModel(IAlertService alertService, ILoggingService logger)
        : base(alertService, logger) { }
}
```

**Service Registration** in `Program.cs`:
```csharp
builder.Services.AddSingleton<IAlertService, AlertService>();
builder.Services.AddSingleton<MyViewModel>();
```

### Core Interfaces (MauiSherpa.Core/Interfaces.cs)

| Interface | Purpose |
|-----------|---------|
| `IAlertService` | Native dialogs and toasts |
| `ILoggingService` | Structured logging |
| `INavigationService` | Page navigation |
| `IDialogService` | Loading indicators, input dialogs, file pickers |
| `IFileSystemService` | File/directory operations |
| `IPlatformService` | Platform detection (IsWindows, IsMacCatalyst) |

### UI Layers

The project has two UI approaches (Blazor is currently disabled):

1. **Native MAUI** (active): `MainPage.cs` using C# UI construction
2. **Blazor Hybrid** (disabled): `.razor` pages in `Pages/` directory intended for BlazorWebView

## Platform Entry Points

MAUI uses platform-specific entry points:

- **Mac Catalyst**: `Platforms/MacCatalyst/Program.cs` → `AppDelegate.cs` → `MauiProgram.CreateMauiApp()`
- **Windows**: `Platforms/Windows/App.xaml.cs` → `MauiProgram.CreateMauiApp()`

The shared `MauiProgram.cs` contains service registration and app configuration.

## Code Conventions

- **No XAML**: All UI is defined in C# code or Razor components
- **Nullable enabled**: All projects use `<Nullable>enable</Nullable>`
- **Property change notification**: Use `SetProperty(ref field, value)` in ViewModels
- **Async naming**: Suffix async methods with `Async`

## Dependencies

| Package | Project | Purpose |
|---------|---------|---------|
| Microsoft.Maui.Controls | Platform | MAUI framework |
| Microsoft.Extensions.DependencyInjection | Core | DI container |
| FluentValidation | Core | Input validation |
| GitHub.Copilot.SDK | Core | Copilot integration |
