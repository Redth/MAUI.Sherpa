using System.CommandLine;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands.Apple;

public static class XcodeCommand
{
    private const string ApplicationsDirectory = "/Applications";
    private const string ManagedXcodeAppPath = "/Applications/Xcode.app";
    private const string ManagedXcodeAppTempLinkPath = "/Applications/.Xcode.app.maui-sherpa-tmp";
    private const string XcodesAppName = "Xcodes.app";
    private const string XcodeReleasesUrl = "https://xcodereleases.com/data.json";

    public static Command Create()
    {
        var cmd = new Command("xcode", "Manage Xcode installations — list, switch, and browse available releases.");

        // Default action (no subcommand) shows current active Xcode
        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            await ShowActiveAsync(json);
        });

        cmd.Add(CreateListCommand());
        cmd.Add(CreateAvailableCommand());
        cmd.Add(CreateSelectCommand());
        cmd.Add(CreateDownloadCommand());

        return cmd;
    }

    // ── maui-sherpa apple xcode (default) ──

    private static async Task ShowActiveAsync(bool json)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Output.WriteError("Xcode is only available on macOS.");
            return;
        }

        var pathResult = await ProcessRunner.RunAsync("xcode-select", "-p");
        if (pathResult.ExitCode != 0)
        {
            if (json)
                Output.WriteJson(new { installed = false, error = "Xcode not installed or xcode-select not configured." });
            else
                Output.WriteError("Xcode not installed or xcode-select not configured.");
            return;
        }

        var xcodePath = pathResult.Output.Trim();
        var versionResult = await ProcessRunner.RunAsync("xcodebuild", "-version");

        string? version = null;
        string? build = null;

        if (versionResult.ExitCode == 0)
        {
            var lines = versionResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            version = lines.FirstOrDefault()?.Replace("Xcode ", "").Trim();
            build = lines.Skip(1).FirstOrDefault()?.Replace("Build version ", "").Trim();
        }

        var cltResult = await ProcessRunner.RunAsync("pkgutil", "--pkg-info=com.apple.pkg.CLTools_Executables");
        string? cltVersion = null;
        if (cltResult.ExitCode == 0)
        {
            var vLine = cltResult.Output.Split('\n').FirstOrDefault(l => l.StartsWith("version:"));
            cltVersion = vLine?.Replace("version:", "").Trim();
        }

        if (json)
        {
            Output.WriteJson(new
            {
                installed = true,
                path = xcodePath,
                version,
                build,
                commandLineToolsVersion = cltVersion,
            });
        }
        else
        {
            Output.WriteSuccess($"Xcode {version ?? "unknown"}");
            Output.WriteInfo($"Path: {xcodePath}");
            if (build is not null)
                Output.WriteInfo($"Build: {build}");
            if (cltVersion is not null)
                Output.WriteInfo($"Command Line Tools: {cltVersion}");
        }
    }

    // ── maui-sherpa apple xcode list ──

    private static Command CreateListCommand()
    {
        var cmd = new Command("list", "List all installed Xcode versions in /Applications.");
        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            await ListInstalledAsync(json);
        });
        return cmd;
    }

    private static async Task ListInstalledAsync(bool json)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Output.WriteError("Xcode is only available on macOS.");
            return;
        }

        var selectedPath = await GetSelectedDeveloperDirAsync();
        var installations = await DiscoverInstallationsAsync(selectedPath);

        if (json)
        {
            Output.WriteJson(new { xcodes = installations });
            return;
        }

        if (installations.Count == 0)
        {
            Output.WriteWarning("No Xcode installations found in /Applications.");
            return;
        }

        Output.WriteTable(
            ["Version", "Build", "Path", "Selected", "Default"],
            installations.Select(x => new[]
            {
                x.Version ?? "unknown",
                x.Build ?? "",
                x.Path,
                x.IsActive ? "✓" : "",
                x.IsDefault ? "✓" : "",
            }));
    }

    // ── maui-sherpa apple xcode available ──

    private static Command CreateAvailableCommand()
    {
        var cmd = new Command("available", "Browse available Xcode releases from xcodereleases.com.");
        var betaOpt = new Option<bool>("--beta", "-b") { Description = "Include beta and RC releases" };
        var limitOpt = new Option<int>("--limit", "-l") { Description = "Maximum releases to show", DefaultValueFactory = _ => 20 };
        cmd.Add(betaOpt);
        cmd.Add(limitOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            var showBetas = parseResult.GetValue(betaOpt);
            var limit = parseResult.GetValue(limitOpt);
            await ListAvailableAsync(json, showBetas, limit, ct);
        });
        return cmd;
    }

    private static async Task ListAvailableAsync(bool json, bool showBetas, int limit, CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "MauiSherpa-CLI");

        JsonElement[]? rawReleases;
        try
        {
            rawReleases = await http.GetFromJsonAsync<JsonElement[]>(XcodeReleasesUrl, ct);
        }
        catch (Exception ex)
        {
            Output.WriteError($"Failed to fetch releases: {ex.Message}");
            return;
        }

        if (rawReleases is null || rawReleases.Length == 0)
        {
            Output.WriteWarning("No releases found.");
            return;
        }

        var releases = new List<ReleaseInfo>();
        var seen = new HashSet<string>();
        foreach (var r in rawReleases)
        {
            var info = ParseRelease(r);
            if (info is null) continue;
            if (!showBetas && info.IsBeta) continue;
            var key = $"{info.Version}|{info.Build}";
            if (!seen.Add(key)) continue;
            releases.Add(info);
            if (releases.Count >= limit) break;
        }

        if (json)
        {
            Output.WriteJson(new
            {
                releases = releases.Select(r => new
                {
                    r.Version,
                    r.Build,
                    r.Date,
                    r.IsBeta,
                    r.MinMacOS,
                    r.DownloadUrl,
                    r.Sdks,
                }),
            });
            return;
        }

        if (releases.Count == 0)
        {
            Output.WriteWarning("No matching releases found.");
            return;
        }

        Output.WriteTable(
            ["Version", "Build", "Date", "Type", "Min macOS", "SDKs"],
            releases.Select(r => new[]
            {
                r.Version,
                r.Build ?? "",
                r.Date ?? "",
                r.IsBeta ? "Beta" : "Release",
                r.MinMacOS ?? "",
                string.Join(", ", r.Sdks.Take(3)) + (r.Sdks.Count > 3 ? $" +{r.Sdks.Count - 3}" : ""),
            }));
    }

    // ── maui-sherpa apple xcode select ──

    private static Command CreateSelectCommand()
    {
        var cmd = new Command("select", "Switch the selected/default Xcode and update /Applications/Xcode.app (requires admin privileges).\n\nExamples:\n  maui-sherpa apple xcode select /Applications/Xcode_26.1.1_17B100.app\n  maui-sherpa apple xcode select 26.1.1");
        var targetArg = new Argument<string>("target") { Description = "Xcode.app path or version number (e.g. 26.1.1)" };
        cmd.Add(targetArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            var target = parseResult.GetValue(targetArg)!;
            await SelectAsync(json, target, ct);
        });
        return cmd;
    }

    private static async Task SelectAsync(bool json, string target, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Output.WriteError("Xcode is only available on macOS.");
            return;
        }

        // If target looks like a version number, resolve to a path
        var appPath = target;
        if (!target.StartsWith("/"))
        {
            var selectedPath = await GetSelectedDeveloperDirAsync();
            var installations = await DiscoverInstallationsAsync(selectedPath);
            var match = installations.FirstOrDefault(x =>
                string.Equals(x.Version, target, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                if (json)
                    Output.WriteJson(new { success = false, error = $"No installed Xcode matches version '{target}'." });
                else
                    Output.WriteError($"No installed Xcode matches version '{target}'. Use 'maui-sherpa apple xcode list' to see available versions.");
                return;
            }
            appPath = match.Path;
        }

        var developerDir = Path.Combine(appPath, "Contents", "Developer");
        if (!Directory.Exists(developerDir))
        {
            if (json)
                Output.WriteJson(new { success = false, error = $"Not a valid Xcode path: {appPath}" });
            else
                Output.WriteError($"Not a valid Xcode path: {appPath}");
            return;
        }

        try
        {
            var managedDefaultState = await GetManagedDefaultStateAsync();
            var existingPaths = Directory.GetDirectories(ApplicationsDirectory, "Xcode*.app")
                .Where(p => !Path.GetFileName(p).Equals(XcodesAppName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var selectionPlan = CreateSelectionPlan(appPath, managedDefaultState, existingPaths);
            var result = await RunElevatedShellScriptAsync(CreateSelectionScript(selectionPlan), ct);

            if (result.exitCode == 0)
            {
                if (json)
                    Output.WriteJson(new { success = true, path = selectionPlan.SelectedAppPath });
                else
                    Output.WriteSuccess($"Switched default Xcode to {Path.GetFileName(selectionPlan.SelectedAppPath)}");
            }
            else
            {
                var err = result.error.Trim();
                if (json)
                    Output.WriteJson(new { success = false, error = err });
                else
                    Output.WriteError(err.Contains("cancel", StringComparison.OrdinalIgnoreCase)
                        ? "Cancelled by user."
                        : $"Failed: {err}");
            }
        }
        catch (Exception ex)
        {
            if (json)
                Output.WriteJson(new { success = false, error = ex.Message });
            else
                Output.WriteError($"Failed to switch: {ex.Message}");
        }
    }

    // ── maui-sherpa apple xcode download ──

    private static Command CreateDownloadCommand()
    {
        var cmd = new Command("download", "Open the Apple Developer download page for an Xcode version.\n\nExamples:\n  maui-sherpa apple xcode download 16.2\n  maui-sherpa apple xcode download 16.2 --open");
        var versionArg = new Argument<string>("version") { Description = "Xcode version to download (e.g. 16.2)" };
        var openOpt = new Option<bool>("--open", "-o") { Description = "Open the download URL in the default browser", DefaultValueFactory = _ => true };
        cmd.Add(versionArg);
        cmd.Add(openOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            var version = parseResult.GetValue(versionArg)!;
            var open = parseResult.GetValue(openOpt);
            await DownloadAsync(json, version, open, ct);
        });
        return cmd;
    }

    private static async Task DownloadAsync(bool json, string version, bool open, CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "MauiSherpa-CLI");

        JsonElement[]? rawReleases;
        try
        {
            rawReleases = await http.GetFromJsonAsync<JsonElement[]>(XcodeReleasesUrl, ct);
        }
        catch (Exception ex)
        {
            Output.WriteError($"Failed to fetch releases: {ex.Message}");
            return;
        }

        if (rawReleases is null)
        {
            Output.WriteError("No releases found.");
            return;
        }

        // Find matching release
        ReleaseInfo? match = null;
        foreach (var r in rawReleases)
        {
            var info = ParseRelease(r);
            if (info is null) continue;
            if (string.Equals(info.Version, version, StringComparison.OrdinalIgnoreCase)
                || info.Version.StartsWith(version, StringComparison.OrdinalIgnoreCase))
            {
                match = info;
                break;
            }
        }

        if (match is null)
        {
            if (json)
                Output.WriteJson(new { success = false, error = $"No release found matching '{version}'." });
            else
                Output.WriteError($"No release found matching '{version}'. Use 'maui-sherpa apple xcode available' to list releases.");
            return;
        }

        if (string.IsNullOrEmpty(match.DownloadUrl))
        {
            if (json)
                Output.WriteJson(new { success = false, error = $"No download URL available for Xcode {match.Version}." });
            else
                Output.WriteError($"No download URL available for Xcode {match.Version}.");
            return;
        }

        if (json)
        {
            Output.WriteJson(new
            {
                success = true,
                version = match.Version,
                build = match.Build,
                downloadUrl = match.DownloadUrl,
            });
        }
        else
        {
            Output.WriteSuccess($"Xcode {match.Version} ({match.Build})");
            Output.WriteInfo($"URL: {match.DownloadUrl}");
        }

        if (open && OperatingSystem.IsMacOS())
        {
            await ProcessRunner.RunAsync("open", match.DownloadUrl);
        }
    }

    // ── Shared Helpers ──

    private static async Task<string?> GetSelectedDeveloperDirAsync()
    {
        var result = await ProcessRunner.RunAsync("xcode-select", "-p");
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    private static async Task<List<InstalledXcode>> DiscoverInstallationsAsync(string? selectedDeveloperDir)
    {
        var installations = new List<InstalledXcode>();
        var managedDefaultState = await GetManagedDefaultStateAsync();
        var managedDefaultTargetPath = managedDefaultState.PhysicalTargetPath;

        if (!Directory.Exists(ApplicationsDirectory)) return installations;

        var xcodeApps = Directory.GetDirectories(ApplicationsDirectory, "Xcode*.app")
            .Where(p => !Path.GetFileName(p).Equals(XcodesAppName, StringComparison.OrdinalIgnoreCase))
            .Where(p => !(managedDefaultState.IsSymlink && PathsEqual(p, ManagedXcodeAppPath)))
            .OrderBy(p => p);

        foreach (var appPath in xcodeApps)
        {
            var devDir = Path.Combine(appPath, "Contents", "Developer");
            if (!Directory.Exists(devDir)) continue;

            var (version, build) = await GetXcodeVersionAsync(appPath);
            if (version is null) continue;

            var isDefault = managedDefaultTargetPath != null &&
                PathsEqual(managedDefaultTargetPath, appPath);
            var isActive = IsSelectedDeveloperDir(selectedDeveloperDir, appPath, isDefault);

            installations.Add(new InstalledXcode(appPath, version, build, isActive, isDefault));
        }

        return installations;
    }

    private static async Task<(string? version, string? build)> GetXcodeVersionAsync(string xcodeAppPath)
    {
        var infoResult = await ProcessRunner.RunAsync(
            "defaults",
            $"read \"{Path.Combine(xcodeAppPath, "Contents", "Info")}\" CFBundleShortVersionString");
        var buildResult = await ProcessRunner.RunAsync(
            "defaults",
            $"read \"{Path.Combine(xcodeAppPath, "Contents", "version")}\" ProductBuildVersion");

        return (
            infoResult.ExitCode == 0 ? infoResult.Output.Trim() : null,
            buildResult.ExitCode == 0 ? buildResult.Output.Trim() : null
        );
    }

    private static async Task<XcodeManagedDefaultState> GetManagedDefaultStateAsync()
    {
        var linkTargetPath = TryResolveDirectoryLinkTarget(ManagedXcodeAppPath);
        var exists = Directory.Exists(ManagedXcodeAppPath);
        var isSymlink = linkTargetPath != null;

        if (!exists || isSymlink)
        {
            return new XcodeManagedDefaultState(
                CanonicalAppPath: ManagedXcodeAppPath,
                Exists: exists,
                IsSymlink: isSymlink,
                LinkTargetPath: linkTargetPath,
                Version: null,
                BuildNumber: null);
        }

        var (version, build) = await GetXcodeVersionAsync(ManagedXcodeAppPath);
        return new XcodeManagedDefaultState(
            CanonicalAppPath: ManagedXcodeAppPath,
            Exists: true,
            IsSymlink: false,
            LinkTargetPath: null,
            Version: version,
            BuildNumber: build ?? "unknown");
    }

    private static bool IsSelectedDeveloperDir(string? selectedDeveloperDir, string appPath, bool isDefault)
    {
        if (string.IsNullOrWhiteSpace(selectedDeveloperDir))
            return false;

        var directDeveloperDir = Path.Combine(appPath, "Contents", "Developer");
        if (PathStartsWith(selectedDeveloperDir, directDeveloperDir))
            return true;

        if (!isDefault)
            return false;

        var canonicalDeveloperDir = Path.Combine(ManagedXcodeAppPath, "Contents", "Developer");
        return PathStartsWith(selectedDeveloperDir, canonicalDeveloperDir);
    }

    private static XcodeSelectionPlan CreateSelectionPlan(
        string selectedAppPath,
        XcodeManagedDefaultState managedDefaultState,
        IEnumerable<string> existingPaths)
    {
        var normalizedSelectedAppPath = selectedAppPath;
        if (managedDefaultState.IsSymlink &&
            !string.IsNullOrWhiteSpace(managedDefaultState.LinkTargetPath) &&
            PathsEqual(selectedAppPath, managedDefaultState.CanonicalAppPath))
        {
            normalizedSelectedAppPath = managedDefaultState.LinkTargetPath;
        }

        if (!managedDefaultState.IsRealBundle)
        {
            return new XcodeSelectionPlan(
                CanonicalAppPath: managedDefaultState.CanonicalAppPath,
                SelectedAppPath: normalizedSelectedAppPath,
                MigrationSourcePath: null,
                MigrationDestinationPath: null);
        }

        if (string.IsNullOrWhiteSpace(managedDefaultState.Version))
            throw new InvalidOperationException("Cannot migrate /Applications/Xcode.app without a detected Xcode version.");

        var migrationDestinationPath = ResolveManagedXcodeBundlePath(
            Path.GetDirectoryName(managedDefaultState.CanonicalAppPath) ?? ApplicationsDirectory,
            managedDefaultState.Version,
            managedDefaultState.BuildNumber ?? "unknown",
            existingPaths.Where(path => !PathsEqual(path, managedDefaultState.CanonicalAppPath)));

        if (PathsEqual(normalizedSelectedAppPath, managedDefaultState.CanonicalAppPath))
            normalizedSelectedAppPath = migrationDestinationPath;

        return new XcodeSelectionPlan(
            CanonicalAppPath: managedDefaultState.CanonicalAppPath,
            SelectedAppPath: normalizedSelectedAppPath,
            MigrationSourcePath: managedDefaultState.CanonicalAppPath,
            MigrationDestinationPath: migrationDestinationPath);
    }

    private static string ResolveManagedXcodeBundlePath(
        string targetDirectory,
        string version,
        string buildNumber,
        IEnumerable<string> existingPaths)
    {
        var preferredPath = Path.Combine(targetDirectory, GetManagedXcodeBundleName(version, buildNumber));
        var normalizedExistingPaths = new HashSet<string>(
            existingPaths.Select(NormalizePath),
            StringComparer.OrdinalIgnoreCase);

        if (!normalizedExistingPaths.Contains(NormalizePath(preferredPath)))
            return preferredPath;

        var baseName = Path.GetFileNameWithoutExtension(preferredPath);
        for (var suffix = 2; ; suffix++)
        {
            var candidatePath = Path.Combine(targetDirectory, $"{baseName}_{suffix}.app");
            if (!normalizedExistingPaths.Contains(NormalizePath(candidatePath)))
                return candidatePath;
        }
    }

    private static string GetManagedXcodeBundleName(string version, string buildNumber) =>
        $"Xcode_{SanitizeXcodeBundleSegment(version)}_{SanitizeXcodeBundleSegment(buildNumber)}.app";

    private static string SanitizeXcodeBundleSegment(string value)
    {
        var sanitized = Regex.Replace(value.Trim(), @"[^A-Za-z0-9.\-]+", "_");
        sanitized = Regex.Replace(sanitized, @"_+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static string CreateSelectionScript(XcodeSelectionPlan plan)
    {
        var canonicalAppPath = EscapeShellSingleQuotedString(plan.CanonicalAppPath);
        var selectedAppPath = EscapeShellSingleQuotedString(plan.SelectedAppPath);
        var migrationSourcePath = EscapeShellSingleQuotedString(plan.MigrationSourcePath ?? string.Empty);
        var migrationDestinationPath = EscapeShellSingleQuotedString(plan.MigrationDestinationPath ?? string.Empty);
        var tempLinkPath = EscapeShellSingleQuotedString(ManagedXcodeAppTempLinkPath);

        return $$"""
            canonical_path='{{canonicalAppPath}}'
            selected_app='{{selectedAppPath}}'
            migration_source='{{migrationSourcePath}}'
            migration_destination='{{migrationDestinationPath}}'
            temp_link='{{tempLinkPath}}'
            previous_symlink_target=""

            cleanup() {
                rm -f "$temp_link"
            }

            rollback() {
                rm -f "$temp_link"

                if [ -L "$canonical_path" ]; then
                    rm "$canonical_path"
                fi

                if [ -n "$previous_symlink_target" ]; then
                    ln -s "$previous_symlink_target" "$canonical_path"
                elif [ -n "$migration_source" ] && [ -n "$migration_destination" ] && [ -d "$migration_destination" ] && [ ! -e "$migration_source" ]; then
                    mv "$migration_destination" "$migration_source"
                fi
            }

            trap 'status=$?; cleanup; if [ $status -ne 0 ]; then rollback; fi; exit $status' EXIT

            if [ -L "$canonical_path" ]; then
                previous_symlink_target="$(readlink "$canonical_path")"
                rm "$canonical_path"
            fi

            if [ -n "$migration_source" ] && [ -n "$migration_destination" ] && [ -d "$migration_source" ] && [ ! -L "$migration_source" ]; then
                mv "$migration_source" "$migration_destination"
                if [ "$selected_app" = "$migration_source" ]; then
                    selected_app="$migration_destination"
                fi
            fi

            rm -f "$temp_link"
            ln -s "$selected_app" "$temp_link"
            mv "$temp_link" "$canonical_path"
            xcode-select -s "$canonical_path/Contents/Developer"

            trap - EXIT
            cleanup
            """;
    }

    private static async Task<(int exitCode, string output, string error)> RunElevatedShellScriptAsync(
        string scriptContents,
        CancellationToken ct)
    {
        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"maui-sherpa-xcode-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(
            tempScriptPath,
            "#!/bin/bash\nset -euo pipefail\n\n" + scriptContents + "\n",
            ct);

        try
        {
            var command = $"/bin/bash '{EscapeShellSingleQuotedString(tempScriptPath)}'";
            var appleScript = $"do shell script \"{EscapeAppleScriptDoubleQuotedString(command)}\" with administrator privileges";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "osascript",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(appleScript);

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) throw new InvalidOperationException("Failed to start osascript");

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            var error = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return (process.ExitCode, output, error);
        }
        finally
        {
            try
            {
                File.Delete(tempScriptPath);
            }
            catch
            {
                // Best effort cleanup for a temp script we created in this method.
            }
        }
    }

    private static string? TryResolveDirectoryLinkTarget(string path)
    {
        try
        {
            var directoryInfo = new DirectoryInfo(path);
            var linkTarget = directoryInfo.LinkTarget;
            if (string.IsNullOrWhiteSpace(linkTarget))
                return null;

            return Path.IsPathRooted(linkTarget)
                ? Path.GetFullPath(linkTarget)
                : Path.GetFullPath(Path.Combine(directoryInfo.Parent?.FullName ?? ApplicationsDirectory, linkTarget));
        }
        catch
        {
            return null;
        }
    }

    private static bool PathStartsWith(string path, string prefix)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedPrefix = NormalizePath(prefix);
        return PathsEqual(normalizedPath, normalizedPrefix) ||
               normalizedPath.StartsWith(normalizedPrefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    private static string EscapeShellSingleQuotedString(string value) =>
        value.Replace("'", "'\"'\"'");

    private static string EscapeAppleScriptDoubleQuotedString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static ReleaseInfo? ParseRelease(JsonElement r)
    {
        try
        {
            // Version
            string? version = null;
            bool isBeta = false;
            if (r.TryGetProperty("version", out var vObj))
            {
                var number = vObj.TryGetProperty("number", out var n) ? n.GetString() : null;
                if (number is null) return null;

                // release is an object: {"release": true} or {"beta": N} or {"rc": N}
                if (vObj.TryGetProperty("release", out var relObj) && relObj.ValueKind == JsonValueKind.Object)
                {
                    if (relObj.TryGetProperty("beta", out var betaNum))
                    {
                        isBeta = true;
                        version = $"{number} Beta {betaNum.GetInt32()}";
                    }
                    else if (relObj.TryGetProperty("rc", out var rcNum))
                    {
                        isBeta = true;
                        version = $"{number} RC {rcNum.GetInt32()}";
                    }
                    else
                    {
                        version = number;
                    }
                }
                else
                {
                    version = number;
                }
            }
            if (version is null) return null;

            // Build
            var build = r.TryGetProperty("version", out var v2) && v2.TryGetProperty("build", out var b)
                ? b.GetString() : null;

            // Date
            string? date = null;
            if (r.TryGetProperty("date", out var dObj))
            {
                var year = dObj.TryGetProperty("year", out var y) ? y.GetInt32() : 0;
                var month = dObj.TryGetProperty("month", out var m) ? m.GetInt32() : 0;
                var day = dObj.TryGetProperty("day", out var d) ? d.GetInt32() : 0;
                if (year > 0) date = $"{year}-{month:D2}-{day:D2}";
            }

            // Min macOS
            string? minMacOS = null;
            if (r.TryGetProperty("requires", out var req) && req.ValueKind == JsonValueKind.String)
                minMacOS = req.GetString();

            // Download URL
            string? downloadUrl = null;
            if (r.TryGetProperty("links", out var links) && links.TryGetProperty("download", out var dl))
                downloadUrl = dl.TryGetProperty("url", out var u) ? u.GetString() : null;

            // SDKs
            var sdks = new List<string>();
            if (r.TryGetProperty("sdks", out var sdksObj))
            {
                foreach (var platform in sdksObj.EnumerateObject())
                {
                    foreach (var sdk in platform.Value.EnumerateArray())
                    {
                        var sdkNum = sdk.TryGetProperty("number", out var sn) ? sn.GetString() : null;
                        if (sdkNum is not null)
                            sdks.Add($"{platform.Name} {sdkNum}");
                    }
                }
            }

            return new ReleaseInfo(version, build, date, isBeta, minMacOS, downloadUrl, sdks);
        }
        catch
        {
            return null;
        }
    }

    private record InstalledXcode(string Path, string? Version, string? Build, bool IsActive, bool IsDefault);
    private record ReleaseInfo(string Version, string? Build, string? Date, bool IsBeta, string? MinMacOS, string? DownloadUrl, List<string> Sdks);
    private record XcodeManagedDefaultState(
        string CanonicalAppPath,
        bool Exists,
        bool IsSymlink,
        string? LinkTargetPath,
        string? Version,
        string? BuildNumber)
    {
        public bool IsRealBundle => Exists && !IsSymlink;

        public string? PhysicalTargetPath => IsSymlink
            ? LinkTargetPath
            : Exists
                ? CanonicalAppPath
                : null;
    }

    private record XcodeSelectionPlan(
        string CanonicalAppPath,
        string SelectedAppPath,
        string? MigrationSourcePath,
        string? MigrationDestinationPath
    );
}
