using System.CommandLine;
using System.Net.Http.Json;
using System.Text.Json;
using MauiSherpa.Cli.Helpers;

namespace MauiSherpa.Cli.Commands.Apple;

public static class XcodeCommand
{
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
            ["Version", "Build", "Path", "Active"],
            installations.Select(x => new[]
            {
                x.Version ?? "unknown",
                x.Build ?? "",
                x.Path,
                x.IsActive ? "✓" : "",
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
        var cmd = new Command("select", "Switch the active Xcode via xcode-select (requires admin privileges).\n\nExamples:\n  maui-sherpa apple xcode select /Applications/Xcode-26.1.1.app\n  maui-sherpa apple xcode select 26.1.1");
        var targetArg = new Argument<string>("target") { Description = "Xcode.app path or version number (e.g. 26.1.1)" };
        cmd.Add(targetArg);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var json = parseResult.GetValue(CliOptions.Json);
            var target = parseResult.GetValue(targetArg)!;
            await SelectAsync(json, target);
        });
        return cmd;
    }

    private static async Task SelectAsync(bool json, string target)
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

        // Use osascript for admin privileges (same as GUI)
        var script = $"do shell script \"xcode-select -s '{developerDir}'\" with administrator privileges";
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        try
        {
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) throw new InvalidOperationException("Failed to start osascript");
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                if (json)
                    Output.WriteJson(new { success = true, path = appPath });
                else
                    Output.WriteSuccess($"Switched to {Path.GetFileName(appPath)} ({developerDir})");
            }
            else
            {
                var err = (await process.StandardError.ReadToEndAsync()).Trim();
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

        if (!Directory.Exists("/Applications")) return installations;

        var xcodeApps = Directory.GetDirectories("/Applications", "Xcode*.app")
            .Where(p => !Path.GetFileName(p).Equals("Xcodes.app", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p);

        foreach (var appPath in xcodeApps)
        {
            var devDir = Path.Combine(appPath, "Contents", "Developer");
            if (!Directory.Exists(devDir)) continue;

            string? version = null;
            string? build = null;

            // Try xcodebuild -version with DEVELOPER_DIR
            var result = await ProcessRunner.RunAsync("xcodebuild", "-version");
            // We need to set DEVELOPER_DIR per-app — use defaults read instead
            var infoResult = await ProcessRunner.RunAsync("defaults", $"read \"{Path.Combine(appPath, "Contents", "Info")}\" CFBundleShortVersionString");
            if (infoResult.ExitCode == 0)
                version = infoResult.Output.Trim();

            var buildResult = await ProcessRunner.RunAsync("defaults", $"read \"{Path.Combine(appPath, "Contents", "version")}\" ProductBuildVersion");
            if (buildResult.ExitCode == 0)
                build = buildResult.Output.Trim();

            var isActive = selectedDeveloperDir != null &&
                devDir.Equals(selectedDeveloperDir, StringComparison.OrdinalIgnoreCase);

            installations.Add(new InstalledXcode(appPath, version, build, isActive));
        }

        return installations;
    }

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

    private record InstalledXcode(string Path, string? Version, string? Build, bool IsActive);
    private record ReleaseInfo(string Version, string? Build, string? Date, bool IsBeta, string? MinMacOS, string? DownloadUrl, List<string> Sdks);
}
