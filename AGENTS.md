# MauiSherpa - AI Agent Instructions

## Project Overview

MauiSherpa is a .NET 10 MAUI Blazor Hybrid desktop application for managing developer tools:
- Android SDK (packages, emulators, devices)
- Apple Developer Tools (certificates, profiles, devices, bundle IDs)
- .NET MAUI Doctor (dependency checking and workload management)
- GitHub Copilot integration

**Platforms:** Mac Catalyst, Windows

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
│   ├── MauiSherpa/      # MAUI app with Blazor UI
│   │   ├── Components/           # Reusable Blazor components
│   │   ├── Pages/                # Blazor page components
│   │   ├── Services/             # Platform-specific service implementations
│   │   └── Platforms/            # Platform-specific code (MacCatalyst, Windows)
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

# Run on Mac Catalyst
dotnet run --project src/MauiSherpa -f net10.0-maccatalyst

# Build entire solution (uses default TFM for each project)
dotnet build MauiSherpa.sln

# Run all tests
dotnet test MauiSherpa.sln

# Publish Mac Catalyst app
dotnet publish src/MauiSherpa -f net10.0-maccatalyst -c Release

# Publish Windows app
dotnet publish src/MauiSherpa -f net10.0-windows10.0.19041.0 -c Release
```

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

### Service Registration
In `MauiProgram.cs`:
```csharp
builder.Services.AddSingleton<IMyService, MyService>();
builder.Services.AddSingleton<MyViewModel>();
```

## Core Interfaces

| Interface | Purpose |
|-----------|---------|
| `IAlertService` | Native dialogs and toasts |
| `ILoggingService` | Structured logging |
| `INavigationService` | Page navigation |
| `IDialogService` | Loading indicators, input dialogs, file pickers |
| `IAndroidSdkService` | Android SDK operations |
| `IAppleConnectService` | App Store Connect API |
| `IAppleIdentityService` | Apple credential management |
| `IDoctorService` | MAUI dependency checking |

## UI Patterns

### Blazor Pages
- Located in `src/MauiSherpa/Pages/`
- Use `@inject` for services
- Use mediator for cached data: `await Mediator.Request(new GetDataRequest())`

### Modals
- `OperationModal` - Single long-running operation with progress
- `MultiOperationModal` - Batch operations with per-item progress
- `ProcessExecutionModal` - CLI process with terminal output

### Icons
Using Font Awesome via Blazorise. Example:
```razor
<i class="fa-solid fa-check text-success"></i>
```

## Code Conventions

- **No XAML** - All UI is Blazor (.razor) or C# code
- **Nullable enabled** - All projects use `<Nullable>enable</Nullable>`
- **Property notification** - Use `SetProperty(ref field, value)` in ViewModels
- **Async naming** - Suffix async methods with `Async`
- **Records for DTOs** - Use record types for data transfer objects

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

## Bundle Identifier

`codes.redth.mauisherpa`
