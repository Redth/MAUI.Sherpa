---
name: maui-doctor
description: Diagnoses and fixes .NET MAUI development environment issues. Validates .NET SDK, workloads, Java JDK, Android SDK, Xcode, and Windows SDK. All version requirements discovered dynamically from NuGet WorkloadDependencies.json - never hardcoded. Use when: setting up MAUI development, build errors mentioning SDK/workload/JDK/Android, "Android SDK not found", "Java version" errors, "Xcode not found", environment verification after updates, or any MAUI toolchain issues. Works on macOS, Windows, and Linux.
---

# .NET MAUI Doctor

Validate and fix .NET MAUI development environments.

## Key Principle

**All version requirements discovered dynamically from NuGet APIs.** Never hardcode versions. See `references/workload-dependencies-discovery.md`.

## Behavior

- Run through ALL tasks autonomously
- Re-validate after each fix
- Iterate until complete or no further actions possible

## Platform Support

| Check | macOS | Windows | Linux |
|-------|-------|---------|-------|
| .NET SDK | ✅ | ✅ | ✅ |
| Workloads | ✅ | ✅ | Android only |
| Java JDK | ✅ | ✅ | ✅ |
| Android SDK | ✅ | ✅ | ✅ |
| Xcode | ✅ | N/A | N/A |

---

## Workflow

### Task 1: Detect Environment

```bash
# macOS
sw_vers && uname -m

# Windows
systeminfo | findstr /B /C:"OS Name" /C:"OS Version"

# Linux
cat /etc/os-release && uname -m
```

### Task 2: Check .NET SDK

```bash
dotnet --info
curl -s "https://raw.githubusercontent.com/dotnet/core/refs/heads/main/release-notes/releases-index.json"
```

Compare installed vs `latest-sdk` where `support-phase` is `"active"`.

### Task 3: Check MAUI Workloads

| Workload | macOS | Windows | Linux |
|----------|-------|---------|-------|
| `maui` | Required | Required | ❌ Use `maui-android` |
| `maui-android` | Alias | Alias | Required |
| `android` | Required | Required | Required |
| `ios` | Required | Optional | N/A |

**Linux**: Use `maui-android` NOT `maui` (meta-workload includes iOS deps).

### Task 4: Discover Requirements from NuGet

See `references/workload-dependencies-discovery.md` for complete process.

Query NuGet for workload manifest → extract `WorkloadDependencies.json` → get:
- `jdk.version` range and `jdk.recommendedVersion`
- `androidsdk.packages`, `buildToolsVersion`, `apiLevel`
- `xcode.version` range

### Task 5: Validate Java JDK

**Only Microsoft OpenJDK supported.** See `references/microsoft-openjdk.md` for paths and detection.

Verify `java -version` output contains "Microsoft".

> **⚠️ TEMPORARY WORKAROUND**: The `jdk.recommendedVersion` from WorkloadDependencies.json currently returns 17, but **recommend JDK 21 instead**. This will be fixed in a future Android workload update. Rule: If WorkloadDependencies suggests ≤21, recommend 21. If it suggests >21, use that value.

### Task 6: Validate Android SDK

Check packages from `androidsdk.packages`, `buildToolsVersion`, `apiLevel` (Task 4).

See `references/installation-commands.md` for detection commands.

### Task 7: Validate Xcode (macOS Only)

```bash
xcodebuild -version
```

Compare against `xcode.version` range from Task 4.

### Task 8: Validate Windows SDK (Windows Only)

```powershell
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots"
```

---

## Quick Diagnosis (Before Reinstalling)

**⏱️ 2-minute check that can save hours.** Many "missing tool" errors are actually path/environment issues.

| Error Message | Likely Actual Cause | Quick Fix |
|---------------|---------------------|-----------|
| "Xcode not found" | `xcode-select` pointing to wrong path | `sudo xcode-select -s /Applications/Xcode.app` |
| "Android SDK not found" | SDK exists but not in current shell | `source ~/.zshrc` or restart terminal |
| "Java not found" / "JDK not detected" | JDK installed but not in PATH | Restart terminal; tooling auto-detects |
| "SDK mismatch" after OS upgrade | Shell config not reloaded | `source ~/.bashrc` or `source ~/.zshrc` |
| "Xcode license not accepted" | License reset after Xcode update | `sudo xcodebuild -license accept` |

**Diagnostic Steps:**
1. **Is it actually missing?** Check with direct path: `ls /Applications/Xcode.app`, `ls /Library/Java/JavaVirtualMachines/`
2. **Is it a shell issue?** Open NEW terminal and retry
3. **Is it an env var issue?** Check `echo $ANDROID_HOME`, `echo $JAVA_HOME` - often these don't need to be set

**Only reinstall if the tool is genuinely missing from disk.**

---

### Task 9: Remediation

See `references/installation-commands.md` for all commands.

Key rules:
- **Workloads**: Always use `--version` flag. Never use `workload update` or `workload repair`.
- **JDK**: Only install Microsoft OpenJDK.
- **Android SDK**: Use `android sdk install` (if tool available) or `sdkmanager`.

### Task 10: Re-validate

After each fix, re-run validation. Iterate until all pass.

---

## Optional Tools (Third-Party)

Highly recommended to simplify and make validation and remediation more reliable / consistent.

| Tool | Package | Purpose |
|------|---------|---------|
| `android` | `AndroidSdk.Tool` | JDK/Android SDK detection |
| `apple` | `AppleDev.Tools` | Xcode detection (macOS) |

**Ask before installing**: "This is a third-party tool (not Microsoft). Install it?"

---

## Important Notes

- **Linux workloads**: Use `maui-android` + `android` (NOT `maui`)
- **JDK**: Only Microsoft OpenJDK. Look for "Microsoft" in `java -version`.
- **JDK Version**: ⚠️ Recommend JDK 21 (not 17) until WorkloadDependencies.json is updated.
- **JAVA_HOME**: Not required. Only problematic if set to non-Microsoft JDK.

---

## Context-Specific Guidance

### CI/CD Pipelines

For automated builds (Azure DevOps, GitHub Actions, etc.):
- **Pin versions**: Always use `dotnet workload install --version X.Y.Z` for reproducibility
- **Use `--skip-manifest-update`**: Prevents drift between pipeline runs
- **Cache aggressively**: NuGet packages, workloads, Android SDK (~90% hit rate saves 5-10 min)
- **Accept licenses non-interactively**: `echo y | sdkmanager --licenses`
- **Avoid**: `dotnet workload update`, `dotnet workload repair` (non-reproducible)

Expected build times:
| Cache State | Time |
|-------------|------|
| Cold (nothing cached) | 8-12 min |
| Warm (NuGet cached) | 3-5 min |
| Hot (workloads + SDK cached) | 1-2 min |

### Linux (Android-Only)

- **Workloads**: Use `maui-android` + `android` (NOT `maui`)
- **KVM acceleration**: Critical for emulator performance
  ```bash
  sudo apt install qemu-kvm && sudo usermod -aG kvm $USER
  # Log out/in for group change
  kvm-ok  # Should say "can be used"
  ```
- **Disk space**: Expect ~8-10GB for SDK + emulator images
- **Streamline .csproj**: Remove unused TFMs
  ```xml
  <TargetFrameworks>net9.0-android</TargetFrameworks>
  ```

### Productivity Tips

Shell aliases (~/.bashrc or ~/.zshrc):
```bash
alias maui-build='dotnet build -f net9.0-android'
alias maui-run='dotnet build -f net9.0-android -t:Run'
```

macOS parallel setup: Start iOS simulator download in Xcode while continuing with SDK/workload installation.

---

## References

- `references/workload-dependencies-discovery.md` - NuGet API discovery process
- `references/microsoft-openjdk.md` - JDK paths, detection, installation
- `references/installation-commands.md` - All validation and install commands
- `references/platform-requirements.md` - Platform-specific details
- `references/troubleshooting.md` - Common issues and solutions
