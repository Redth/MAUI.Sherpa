# MauiSherpa Copilot Instructions

## Project Overview

MauiSherpa is a .NET 11 MAUI Blazor Hybrid desktop application for managing
Android SDKs and devices, Android keystores, Apple developer assets, .NET MAUI
workloads and SDKs, and GitHub Copilot.

**Platforms:** macOS (AppKit), Windows, and Linux (GTK).

Read `AGENTS.md` for the complete architecture, build, testing, platform, and
debugging guidance. For every Blazor Hybrid UI change, `DESIGN.md` is the
authoritative design specification.

## Build Commands

```bash
# Build the macOS AppKit head
dotnet build src/MauiSherpa.MacOS -f net11.0-macos

# Build for Windows (on Windows only)
dotnet build src/MauiSherpa -f net11.0-windows10.0.19041.0

# Build the solution
dotnet build MauiSherpa.slnx

# Run tests
dotnet test MauiSherpa.slnx

# Launch macOS
dotnet build src/MauiSherpa.MacOS -f net11.0-macos -t:Run
```

## Architecture

- `src/MauiSherpa.Core`: platform-independent business logic, interfaces,
  services, handlers, requests, and ViewModels.
- `src/MauiSherpa`: shared active Blazor Hybrid UI and Windows head.
- `src/MauiSherpa.MacOS`: macOS AppKit head.
- `src/MauiSherpa.LinuxGtk`: Linux GTK head.
- `src/MauiSherpa.Workloads`: .NET workload querying library.
- Follow clean architecture: Core has no platform dependencies; platform heads
  implement Core interfaces. Use constructor DI, MVVM, and Shiny.Mediator request
  handlers as documented in `AGENTS.md`.

## Blazor Hybrid Design System

Follow `DESIGN.md` and use `src/MauiSherpa/wwwroot/css/app.css` as the single
source of truth for CSS tokens and shared styles:

- Use the pure-neutral surface ladder (`--surface-canvas`, `--surface-card`,
  `--surface-well`, `--surface-popover`) with crisp 1px semantic hairlines.
- Never hardcode colors or radii, nest rounded cards, add thick colored side
  stripes, or write page-specific platform CSS.
- Use `<SherpaSelect>` instead of native `<select>`, `<SherpaTabs>` and
  `<SherpaSegmentedControl>` for navigation modes, and shared button/header/list
  components and classes.
- Use only the six `--radius-*` tokens and Font Awesome 6 classes with `fa-fw`;
  use `fa-xmark` rather than raw `×`.
- Platform adaptation is centralized in `.platform-macos`,
  `.platform-windows`, and `.platform-linux` token overrides.
- Validate UI changes in light and dark themes on macOS, Windows, and Linux GTK.

## Code Conventions

- **No XAML**: UI is implemented with active Blazor Hybrid Razor components or
  platform C# code
- **Nullable enabled**: All projects use `<Nullable>enable</Nullable>`
- **Property change notification**: Use `SetProperty(ref field, value)` in ViewModels
- **Async naming**: Suffix async methods with `Async`
- **External APIs**: Prefer official NuGet packages over raw `HttpClient`
- **Tests**: Use xUnit, Moq, and FluentAssertions; test projects target `net11.0`
