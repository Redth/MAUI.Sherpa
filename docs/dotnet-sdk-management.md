# .NET SDK Management with `dotnetup`

MAUI Sherpa can install and update .NET SDKs and runtimes for you by bootstrapping and
driving [`dotnetup`](https://github.com/dotnet/sdk/tree/release/dnup/documentation/general/dotnetup),
the .NET user-level toolchain manager. This powers the **Doctor** "Update available" fix and a
dedicated **Tools → .NET SDK Manager** page.

## What `dotnetup` is

`dotnetup` is a single self-contained executable that installs .NET SDKs/runtimes into a
**user-level** location (no admin required) and, in *Terminal Mode*, configures your shell
profile (`PATH` / `DOTNET_ROOT`) so the command-line `dotnet` resolves to the managed install.

Sherpa never relies on shell `PATH` — it always invokes `dotnetup` by full path
(`~/.dotnetup/dotnetup[.exe]`).

## How Sherpa uses it

### Acquisition
Sherpa downloads the binary directly from `aka.ms` and verifies its SHA-512 before use
(no piping a remote script into a shell):

```
https://aka.ms/dotnet/dotnetup/{quality}/dotnetup-{rid}[.exe]   (+ ".sha512")
```

- `quality` defaults to `daily` (the only published quality today; configurable).
- RIDs: `win-x64 win-arm64 linux-x64 linux-arm64 linux-musl-x64 linux-musl-arm64 osx-x64 osx-arm64`.
  macOS architecture is detected via `hw.optional.arm64` (uname lies under Rosetta).
- Installed into `~/.dotnetup` (matches the official installer, so an existing install is reused).

### Doctor integration
- The `.NET SDK` "Update available" check is now **fixable**. Its `FixAction` is
  `dotnetup-update-sdk:<version>`; applying it bootstraps `dotnetup` if missing, then runs
  `sdk install <version> --set-default-install` (Terminal Mode).
- A **dotnetup presence** check (Info) shows the installed version, or offers an
  `install-dotnetup` fix when it is missing.
- Doctor reconciles **dotnetup-managed SDKs** into the installed-SDK set
  (`MergeManagedSdks`) so an applied update actually clears the warning. This matters because
  the GUI Doctor does not read shell `PATH`, and on macOS the managed root defaults to
  `~/Library/Application Support/dotnet` — which `LocalSdkService` does not otherwise scan.

### SDK Manager page (`/dotnet-sdk`)

The global page is machine-scoped and uses progressive disclosure:

- A passive **dotnetup** status control sits beside the page title. Its outlined status icon keeps
  tool health visible without adding another card. Selecting it opens a native hybrid details dialog
  with version, binary path, managed roots, architecture, install/update, and reinstall actions.
- **Tracked channels** shows SDK channel chips and automatically checks the official release feeds
  for updates. Expanding it shows the exact SDK/runtime/ASP.NET Core/Windows Desktop specs, update
  previews, scoped update actions, source paths, and untrack actions. Each channel row keeps its
  resolved version and check-mark/update state together instead of using a separate aggregate
  "everything is up to date" panel.
- **Installed components** always groups concrete installs into collapsed `.NET X.Y` sections.
  Expanding a version shows spaced SDK/runtime cards, uninstall actions, and workload feature-band
  details directly in the version body without another nested expander.

The toolbar contains only **Refresh** and **Open project folder**. Choosing a folder opens a large,
folder-scoped modal instead of changing the global page. The modal shows the nearest `global.json`,
requested SDK policy, resolved/installed versions, rolling-channel update readiness, the exact
resolved install root and architecture, related runtime-line components, and the matching workload
feature band. It supports only project-relevant operations: install/track/update the project SDK,
edit its workload-set pin, and restore workloads. Closing it returns to the unchanged global view.

## Workload management

`dotnetup` acquires SDKs and runtimes. Workloads are managed by the selected SDK's own
`dotnet workload` commands. Sherpa always invokes the full muxer path from the selected
dotnetup install root and isolates it with `DOTNET_ROOT`, `DOTNET_MULTILEVEL_LOOKUP=0`, and
an exact SDK-pinning command context.

### Feature-band model

Workload state belongs to:

```
install root + architecture + SDK feature band
```

SDK patches in the same feature band share workload state. For example, `10.0.301` and
`10.0.302` both use the `10.0.300` workload state, while `10.0.200` remains separate.
Prerelease SDK bands preserve their prerelease identity.

Each `.NET X.Y` section shows a Workloads card with one row per feature-band target. A row
reports workload-set or loose-manifest mode, the active set/hash, recorded workload IDs,
available updates, and diagnostics.

### Supported operations

- Add workload IDs: `<root>/dotnet workload install <ids>`
- Remove recorded workload IDs: `<root>/dotnet workload uninstall <ids>`
- Change to an exact set: `<root>/dotnet workload update --version <set>`
- Update to the latest compatible set: `<root>/dotnet workload update`
- Repair installed workload packs: `<root>/dotnet workload repair`
- Restore a project's workloads: `<root>/dotnet workload restore`

Workload and set selection uses native hybrid dialogs. The **Workloads** dialog shows installed
IDs selectable for removal and available IDs selectable for installation, then returns both groups
through one **Review & apply changes** action. Its default available list is curated to the common
top-level workloads, with **Show all** exposing the complete compatible list. Linux recommends
`maui-android`, `android`, and `wasm-tools`; macOS and Windows recommend `maui`, the platform
workloads, `android`, and `wasm-tools`.

The **Sets** action opens the workload-set version picker. When an update is available, the button
adds a blue update indicator segment instead of presenting a separate update action. Loose-manifest
targets must explicitly choose a set, which switches that feature band to workload-set mode. Every
command operation uses the hybrid process dialog for its preview, exact command, elevation warning,
terminal output, and completion state. Workload update mode is read from the SDK's
per-feature-band install-state file, avoiding
the `workload config` command's installer initialization and unnecessary permission failures.
Mutations detect whether the workload metadata is writable and request administrator access only
when needed. Sherpa does not edit SDK marker files, manifests, packs, or registry state, and does
not invent a workload-set uninstall operation. The SDK remains responsible for transactions,
rollback, install records, and garbage collection.

### Workload definitions, composition, and pack aliases

Workload IDs can recursively `extend` other workload IDs. The selection dialog calls these
relationships **Includes** and removes redundant child selections when an aggregate workload is
selected. A workload redirect is displayed separately. Pack `alias-to` entries resolve a logical
pack to a RID-specific package; they are pack aliases, not workload aliases.

### Project workload-set pins

The project-context modal reads the official `sdk.workloadVersion` property from `global.json`
and recognizes the older `workloadSet.version` shape for migration. Pin/change/remove operations
show the exact JSON diff, preserve JSONC comments, trailing commas, indentation, newline style,
and unknown properties, then replace the file atomically. **Pin & restore** runs
`dotnet workload restore` from the project folder so the project's real SDK and workload-set pin
participate in resolution.

## CLI surface (verified against the shipping binary)

> The published docs are ahead of the current `daily` binary. Build to these real facts.

- **Machine-readable output = `dotnetup list --format Json`** (note: `--format <Json|Text>`,
  **not** `--json`). It returns both:
  - `installSpecs[]` — tracked channels: `{ component, versionOrChannel, source, globalJsonPath, installRoot, architecture }`
  - `installations[]` — concrete installs: `{ component, version, installRoot, architecture, isValid, frameworkName }`
  - Component values: `SDK`, `Runtime`, `ASPNETCore`, `WindowsDesktop`. Source values: `Explicit`, `GlobalJson`, `All`.
- **`dotnetup --info` is text only** (no `--json`): Version / Commit / Architecture / RID lines.
- Mutating commands are **non-interactive by default** (interactivity is opt-in via `--interactive`):
  - `dotnetup sdk install [<channel>...] [--set-default-install] [--update-global-json] [--no-progress] [...]`
  - `dotnetup sdk update [--no-progress]`, `dotnetup sdk uninstall <channel> [--source All|Explicit|GlobalJson]`
  - `dotnetup runtime install|update|uninstall <spec>` where spec = `<channel>` or `component@version`
    (components: `runtime`, `aspnetcore`)
  - `dotnetup update` updates everything.
- **Terminal Mode** is `--set-default-install` on install (writes the shell profile). Sherpa's
  one-step path is `sdk install <channel> --set-default-install`. On Apple platforms, Sherpa
  hosts dotnetup in a pseudo-terminal and streams its live ANSI progress into the terminal panel.
  Redirected platforms keep using `--no-progress` and receive phase-by-phase output.
- Always resolve the managed install root from `list --format Json` `installRoot` rather than
  hardcoding a path — it differs per OS.

## Architecture

- **`MauiSherpa.Workloads`** (platform-agnostic, unit-tested) — the riskiest, pure logic:
  - `Models/DotnetUpModels.cs` — `DotnetUpComponent`, `DotnetUpInstallSource`,
    `DotnetUpInstallation`, `DotnetUpInstallSpec`, `DotnetUpListResult`, `DotnetUpToolInfo`,
    and dotnetup self-update metadata.
  - `Services/DotnetUpRuntimeIdentifier.cs` — RID detection + download/checksum URL builders.
  - `Services/DotnetUpParser.cs` — `ParseList` (`list --format Json`) and `ParseInfo` (`--info` text).
  - `Services/DotnetUpDownloader.cs` — download + SHA-512 verification.
  - `Services/DotnetUpArguments.cs` — pure command/argument builders.
  - `Models/SdkFeatureBand.cs` and `Models/DotnetWorkloadModels.cs` — SDK-compatible target,
    inventory, graph, preview, and project-pin models.
  - `Services/DotnetWorkloadParser.cs` and `Services/WorkloadGraphResolver.cs` — structured/table
    CLI parsing plus aggregate workload, redirect, and RID pack resolution.
  - `Services/GlobalJsonWorkloadPinEditor.cs` — JSONC-preserving atomic
    `sdk.workloadVersion` edits.
- **`MauiSherpa.Core`** — `IDotnetUpService` (`Interfaces.cs`) + `DotnetUpService`: resolves the
  exe, bootstraps (`EnsureInstalledAsync`), queries (`GetToolInfoAsync`, `GetListAsync`), and
  builds `ProcessRequest`s for install/update/uninstall. `IDotnetWorkloadService` discovers
  feature-band targets, orchestrates inventory queries, builds workload process requests, and
  invalidates per-root caches after writes. Both app heads register these services.
- **Doctor** consumes the same `IDotnetWorkloadService` target, availability result, environment,
  command builder, and refresh path as the SDK Manager so the two surfaces cannot disagree.

## Testing

Pure helpers are covered by unit tests in `tests/MauiSherpa.Workloads.Tests` (RID/URL building,
SHA-512 verify, `list`/`--info` parsing, argument building). Doctor reconciliation and the new
dependency-status shapes are covered in `tests/MauiSherpa.Core.Tests/Services/DoctorDotnetUpTests.cs`.
