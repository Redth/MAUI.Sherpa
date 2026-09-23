# MauiSherpa - Agent Summary

## Overview

**MauiSherpa** is an AI-powered .NET MAUI development environment doctor consisting of two main components:

1. **MauiSherpa.Workloads** - A C# library for programmatically inspecting .NET SDK workloads
2. **MauiSherpa** - A CLI tool that uses the GitHub Copilot SDK to validate and fix MAUI development environments

## Project Structure

```
MauiSherpa/
├── MauiSherpa.slnx                     # Solution file
├── agents.md                       # This documentation file
│
├── MauiSherpa/                         # Main CLI application
│   ├── Program.cs                  # Entry point, Copilot SDK integration
│   ├── MauiSherpa.csproj               # Project file
│   ├── Tools/                      # AI tool definitions
│   │   ├── ToolsFactory.cs         # Creates all tools for the session
│   │   ├── DotNetTools.cs          # .NET SDK and workload tools
│   │   ├── AndroidTools.cs         # Android SDK and JDK tools
│   │   └── AppleTools.cs           # Xcode and simulator tools (macOS only)
│   ├── Services/                   # Support services
│   │   ├── ConsoleService.cs       # Pretty console output with colors/emojis
│   │   └── ConfirmationService.cs  # User confirmation for destructive actions
│   ├── Skills/                     # Skill definitions for Copilot
│   │   └── env-setup.md            # Environment doctor skill
│   └── Prompts/                    # Initial prompts
│       └── maui-check.md           # Default environment check prompt
│
├── MauiSherpa.Workloads/               # Core library
│   ├── Models/                     # Data models
│   │   ├── SdkVersion.cs           # SDK version parsing
│   │   ├── WorkloadSet.cs          # Workload set with manifest mappings
│   │   ├── WorkloadManifest.cs     # Parsed WorkloadManifest.json
│   │   ├── WorkloadDefinition.cs   # Workload details
│   │   ├── PackDefinition.cs       # Pack info
│   │   └── WorkloadDependencies.cs # External dependencies (Xcode, JDK, etc.)
│   ├── Services/                   # Service layer
│   │   ├── ISdkVersionService.cs + SdkVersionService.cs
│   │   ├── IWorkloadSetService.cs + WorkloadSetService.cs
│   │   ├── IWorkloadManifestService.cs + WorkloadManifestService.cs
│   │   ├── ILocalSdkService.cs + LocalSdkService.cs
│   │   └── IGlobalJsonService.cs + GlobalJsonService.cs  # NEW: global.json parsing
│   └── NuGet/                      # NuGet integration
│       ├── INuGetClient.cs
│       └── NuGetClient.cs
│
└── MauiSherpa.Workloads.Sample/        # Sample console app
    └── Program.cs                  # Demonstrates library usage
```

## Target Framework

- **net10.0** - All projects target .NET 10

## NuGet Dependencies

### MauiSherpa.Workloads (Library)
- `Microsoft.Deployment.DotNet.Releases` (2.0.0-preview.1.25277.114) - SDK version lookups
- `NuGet.Protocol` (6.12.4) - Downloading workload packages from NuGet

### MauiSherpa (CLI)
- `GitHub.Copilot.SDK` (0.1.16) - AI agent orchestration
- `AndroidSdk` (0.26.0) - Android SDK management
- `AppleDev` (0.7.4) - Apple development tools
- `System.CommandLine` (2.0.0-beta4) - CLI argument parsing

## CLI Usage

```bash
# Run full environment check with confirmations
dotnet run --project MauiSherpa

# Run with auto-fix (no confirmations)
dotnet run --project MauiSherpa -- --auto-fix

# Specify a workload set version to target
dotnet run --project MauiSherpa -- --workload-set-version 10.0.102

# Use a specific AI model
dotnet run --project MauiSherpa -- --model claude-sonnet-4

# Show help
dotnet run --project MauiSherpa -- --help
```

### CLI Options
- `-y, --auto-fix` - Automatically fix issues without prompting
- `-v, --verbose` - Show verbose output including tool calls
- `-w, --workload-set-version <version>` - Target specific workload set version
- `-m, --model <model>` - AI model to use (default: gpt-4.1)

## AI Tools

### Context Tools (call first!)
| Tool | Description |
|------|-------------|
| `get_context_info` | Get CWD, SDK root, global.json settings, platform info |
| `get_global_json` | Get full global.json contents if present |

### .NET Tools
| Tool | Description |
|------|-------------|
| `get_installed_sdks` | Get all installed SDKs, workloads, manifests as JSON |
| `get_available_sdk_versions` | Get available SDK versions from releases feed |
| `get_available_workload_set_versions` | Get workload set versions for a feature band |
| `get_workload_dependencies` | Get external deps (Xcode, JDK, etc.) for a manifest |
| `dotnet_info` | Run `dotnet --info` |
| `list_workloads` | Run `dotnet workload list` |
| `install_workload` | Install workload with specific workload set version ⚠️ |
| `update_workloads` | Update all workloads to specific version ⚠️ |

### Android/JDK Tools
| Tool | Description |
|------|-------------|
| `get_android_environment_info` | Auto-discover Android SDK and JDKs, list installed packages |
| `get_java_info` | Auto-discover all JDK installations with versions |
| `list_android_sdk_packages` | List installed/available Android SDK packages |
| `install_android_sdk_package` | Install Android SDK package (bootstraps SDK if missing) ⚠️ |
| `accept_android_licenses` | Accept Android SDK licenses ⚠️ |
| `install_microsoft_openjdk` | Install Microsoft OpenJDK ⚠️ |

### Apple Tools (macOS only)
| Tool | Description |
|------|-------------|
| `list_xcode_installations` | List installed Xcode versions |
| `get_selected_xcode` | Get currently selected Xcode |
| `select_xcode` | Change selected Xcode ⚠️ |
| `list_simulators` | List available simulators |
| `create_simulator` | Create new simulator ⚠️ |
| `boot_simulator` | Boot a simulator |
| `suggest_xcode_installation` | Get Xcode installation instructions |

⚠️ = Requires user confirmation (unless --auto-fix)

## Key Concepts

### Context Awareness
The tool respects the working directory where it runs:
- Checks `$CWD/.dotnet/` for local SDK installations
- Respects DOTNET_ROOT environment variables
- Parses `global.json` for pinned SDK and workload set versions
- All dotnet commands use the tool's working directory

### global.json Support
```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  },
  "workloadSet": {
    "version": "10.0.102"
  }
}
```
- SDK version pins are respected
- Workload set version pins are honored for installations
- User is warned before overriding pinned versions

### Workload Set Versions
When installing workloads, always use a specific workload set version:
```bash
dotnet workload install maui --version 10.0.102
```
This ensures reproducible environments across machines.

### SDK Feature Bands
SDK versions like `9.0.105` belong to feature band `9.0.100`. Workload manifests and sets are organized by feature band.

## Code Conventions

### Async Pattern
- File I/O operations use async methods with `CancellationToken`
- Directory enumeration remains synchronous (fast enough)

### Model Design
- **Records** for immutable data models
- **Strongly-typed models** for well-defined structures
- **Raw JSON access** for extensible/variable structures

### Service Design
- Interface + Implementation pattern
- Stateless services, safe to reuse
- Constructor injection for dependencies

### Tool Design
- Tools defined using `AIFunctionFactory.Create()` from Microsoft.Extensions.AI
- Mutating tools require `IConfirmationService` for user confirmation
- All tools return JSON-serialized results
- Platform-specific tools check `RuntimeInformation.IsOSPlatform()`
- Android/JDK tools use `AndroidSdk` NuGet package's `SdkLocator` and `JdkLocator` for auto-discovery
- ANDROID_HOME and JAVA_HOME are NOT required - tools auto-discover paths

## Copilot SDK Integration

The CLI uses GitHub Copilot SDK to create an AI-powered session:

```csharp
await using var client = new CopilotClient();

var tools = ToolsFactory.CreateAllTools(confirmationService);

await using var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-4.1",
    Streaming = true,
    Tools = tools,
    SkillDirectories = ["./Skills"],
    SystemMessage = new SystemMessageConfig { ... }
});

session.On(evt => { /* handle events */ });

await session.SendAsync(new MessageOptions { Prompt = "Check my environment" });
```

### Event Handling
- `AssistantMessageDeltaEvent` - Streaming response chunks
- `ToolExecutionStartEvent` - Tool being called
- `ToolExecutionCompleteEvent` - Tool finished
- `SessionIdleEvent` - Processing complete
- `SessionErrorEvent` - Error occurred

## Sample App Usage

```bash
# Show locally installed workloads
dotnet run --project MauiSherpa.Workloads.Sample -- local

# Query latest SDK and show available workload sets
dotnet run --project MauiSherpa.Workloads.Sample -- available

# Output complete local SDK info as JSON
dotnet run --project MauiSherpa.Workloads.Sample -- json

# Output summary JSON
dotnet run --project MauiSherpa.Workloads.Sample -- json-summary
```

## API Quick Reference

### GlobalJsonService (NEW)
```csharp
var service = new GlobalJsonService();

// Find and parse global.json
GlobalJsonInfo? info = service.GetGlobalJson();

// Quick checks
bool sdkPinned = service.IsSdkVersionPinned();
bool workloadPinned = service.IsWorkloadSetPinned();
string? pinnedVersion = service.GetPinnedWorkloadSetVersion();
```

### LocalSdkService
```csharp
var service = new LocalSdkService();

// Get dotnet path (respects DOTNET_ROOT, $CWD/.dotnet/)
string? path = service.GetDotNetSdkPath();

// Get comprehensive JSON (for AI tools)
string json = await service.GetInstalledSdkInfoAsJsonStringAsync(true, true);
```

### WorkloadSetService
```csharp
var service = new WorkloadSetService();

// Get available versions
var versions = await service.GetAvailableWorkloadSetVersionsAsync("10.0.100");

// Get workload set contents
var set = await service.GetWorkloadSetAsync("10.0.100", versions[0]);
```

## Emoji Conventions

The tool uses consistent emoji indicators:
- ✅ Correctly installed/configured
- ❌ Missing required component
- ⚠️ Missing optional component / warning
- 📌 Version pinned by global.json
- ℹ️ Informational note
- 🔧 Fixing/installing
- 🔍 Checking/validating
- ⏳ In progress

## Current State

- ✅ MauiSherpa.Workloads library fully implemented
- ✅ GlobalJsonService for parsing global.json
- ✅ All AI tools implemented (DotNet, Android, Apple)
- ✅ CLI with Copilot SDK integration
- ✅ Skill and prompt files
- ✅ Pretty console output with emojis/colors
- ✅ User confirmation service
- ✅ Context-aware (respects global.json, $CWD/.dotnet/)
