# .NET SDK Management with `dotnetup`

MAUI Sherpa can install and update .NET SDKs and runtimes for you by bootstrapping and
driving [`dotnetup`](https://github.com/dotnet/sdk/tree/release/dnup/documentation/general/dotnetup),
the .NET user-level toolchain manager. This powers the **Doctor** "Update available" fix and a
dedicated **.NET → SDK Manager** page.

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
  `sdk install <version> --set-default-install --no-progress` (Terminal Mode).
- A **dotnetup presence** check (Info) shows the installed version, or offers an
  `install-dotnetup` fix when it is missing.
- Doctor reconciles **dotnetup-managed SDKs** into the installed-SDK set
  (`MergeManagedSdks`) so an applied update actually clears the warning. This matters because
  the GUI Doctor does not read shell `PATH`, and on macOS the managed root defaults to
  `~/Library/Application Support/dotnet` — which `LocalSdkService` does not otherwise scan.

### SDK Manager page (`/dotnet-sdk`)
Full management UI: install/reinstall `dotnetup`; install an SDK by channel
(`latest`, `lts`, `preview`, feature bands like `10.0.1xx`, `daily`, or a custom channel);
update all / update SDKs / update runtimes; list managed installs grouped by install root;
uninstall a component; view tracked channels (install specs).

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
  one-step path is `sdk install <channel> --set-default-install --no-progress`.
- Always resolve the managed install root from `list --format Json` `installRoot` rather than
  hardcoding a path — it differs per OS.

## Architecture

- **`MauiSherpa.Workloads`** (platform-agnostic, unit-tested) — the riskiest, pure logic:
  - `Models/DotnetUpModels.cs` — `DotnetUpComponent`, `DotnetUpInstallSource`,
    `DotnetUpInstallation`, `DotnetUpInstallSpec`, `DotnetUpListResult`, `DotnetUpToolInfo`.
  - `Services/DotnetUpRuntimeIdentifier.cs` — RID detection + download/checksum URL builders.
  - `Services/DotnetUpParser.cs` — `ParseList` (`list --format Json`) and `ParseInfo` (`--info` text).
  - `Services/DotnetUpDownloader.cs` — download + SHA-512 verification.
  - `Services/DotnetUpArguments.cs` — pure command/argument builders.
- **`MauiSherpa.Core`** — `IDotnetUpService` (`Interfaces.cs`) + `DotnetUpService`: resolves the
  exe, bootstraps (`EnsureInstalledAsync`), queries (`GetToolInfoAsync`, `GetListAsync`), and
  builds `ProcessRequest`s for install/update/uninstall. Registered in `MauiProgram.cs`.
- **Doctor** (`DoctorService`) consumes `IDotnetUpService` (injected optionally) to enrich the
  context, reconcile managed SDKs, and dispatch the two new fix actions.

## Testing

Pure helpers are covered by unit tests in `tests/MauiSherpa.Workloads.Tests` (RID/URL building,
SHA-512 verify, `list`/`--info` parsing, argument building). Doctor reconciliation and the new
dependency-status shapes are covered in `tests/MauiSherpa.Core.Tests/Services/DoctorDotnetUpTests.cs`.
