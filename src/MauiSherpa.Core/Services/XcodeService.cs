using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Discovers installed Xcode versions, fetches available releases from xcodereleases.com,
/// and manages active Xcode selection via xcode-select.
/// </summary>
public class XcodeService : IXcodeService
{
    private readonly ILoggingService _logger;
    private readonly IPlatformService _platform;
    private readonly HttpClient _httpClient;

    private const string XcodeReleasesUrl = "https://xcodereleases.com/data.json";

    public bool IsSupported => _platform.IsMacCatalyst || _platform.IsMacOS;

    public XcodeService(ILoggingService logger, IPlatformService platform, HttpClient httpClient)
    {
        _logger = logger;
        _platform = platform;
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<XcodeInstallation>> GetInstalledXcodesAsync()
    {
        if (!IsSupported) return [];

        var installations = new List<XcodeInstallation>();
        var selectedPath = await GetSelectedXcodePathAsync();

        try
        {
            var applicationsDir = "/Applications";
            if (!Directory.Exists(applicationsDir)) return [];

            var xcodeApps = Directory.GetDirectories(applicationsDir, "Xcode*.app")
                .Where(p => !Path.GetFileName(p).Equals("Xcodes.app", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p)
                .ToList();

            foreach (var appPath in xcodeApps)
            {
                try
                {
                    var (version, build) = await GetXcodeVersionAsync(appPath);
                    if (version == null) continue;

                    var isSelected = selectedPath != null &&
                        selectedPath.StartsWith(appPath, StringComparison.OrdinalIgnoreCase);

                    installations.Add(new XcodeInstallation(
                        Path: appPath,
                        Version: version,
                        BuildNumber: build ?? "unknown",
                        IsSelected: isSelected
                    ));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to inspect Xcode at {appPath}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to discover Xcode installations: {ex.Message}", ex);
        }

        return installations.OrderByDescending(x => x.Version).ToList();
    }

    public async Task<string?> GetSelectedXcodePathAsync()
    {
        if (!IsSupported) return null;

        try
        {
            var result = await RunProcessAsync("xcode-select", "-p");
            return result.exitCode == 0 ? result.output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<XcodeRelease>> GetAvailableReleasesAsync()
    {
        try
        {
            _logger.LogInformation("Fetching available Xcode releases from xcodereleases.com...");
            var json = await _httpClient.GetStringAsync(XcodeReleasesUrl);
            var releases = ParseXcodeReleases(json);
            _logger.LogInformation($"Fetched {releases.Count} Xcode releases");
            return releases;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fetch Xcode releases: {ex.Message}", ex);
            return [];
        }
    }

    public async Task<bool> SelectXcodeAsync(string xcodeAppPath)
    {
        if (!IsSupported) return false;

        var developerDir = Path.Combine(xcodeAppPath, "Contents", "Developer");
        if (!Directory.Exists(developerDir))
        {
            _logger.LogError($"Developer directory not found: {developerDir}");
            return false;
        }

        try
        {
            // xcode-select -s requires sudo — use osascript to prompt for admin
            var script = $"do shell script \"xcode-select -s '{developerDir}'\" with administrator privileges";
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start osascript");
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation($"Switched active Xcode to: {xcodeAppPath}");
                return true;
            }

            _logger.LogError($"Failed to switch Xcode: {error}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to select Xcode: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> AcceptLicenseAsync()
    {
        if (!IsSupported) return false;

        try
        {
            var script = "do shell script \"xcodebuild -license accept\" with administrator privileges";
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start osascript");
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to accept license: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> InstallCommandLineToolsAsync()
    {
        if (!IsSupported) return false;

        try
        {
            var result = await RunProcessAsync("xcode-select", "--install");
            // This opens a system dialog — exit code 1 means "already installed"
            return result.exitCode == 0 || result.error.Contains("already installed", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to install command line tools: {ex.Message}", ex);
            return false;
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private async Task<(string? version, string? build)> GetXcodeVersionAsync(string xcodeAppPath)
    {
        var xcodebuild = Path.Combine(xcodeAppPath, "Contents", "Developer", "usr", "bin", "xcodebuild");
        if (!File.Exists(xcodebuild))
        {
            // Fallback: try parsing Info.plist
            return await GetVersionFromInfoPlistAsync(xcodeAppPath);
        }

        var result = await RunProcessAsync(xcodebuild, "-version");
        if (result.exitCode != 0) return (null, null);

        var versionMatch = Regex.Match(result.output, @"Xcode (\d+\.\d+(?:\.\d+)?)");
        var buildMatch = Regex.Match(result.output, @"Build version (\w+)");

        return (
            versionMatch.Success ? versionMatch.Groups[1].Value : null,
            buildMatch.Success ? buildMatch.Groups[1].Value : null
        );
    }

    private async Task<(string? version, string? build)> GetVersionFromInfoPlistAsync(string xcodeAppPath)
    {
        try
        {
            // Use defaults read to parse Info.plist
            var plistPath = Path.Combine(xcodeAppPath, "Contents", "Info.plist");
            if (!File.Exists(plistPath)) return (null, null);

            var versionResult = await RunProcessAsync("defaults", $"read \"{plistPath}\" CFBundleShortVersionString");
            var buildResult = await RunProcessAsync("defaults", $"read \"{plistPath}\" DTXcodeBuild");

            return (
                versionResult.exitCode == 0 ? versionResult.output.Trim() : null,
                buildResult.exitCode == 0 ? buildResult.output.Trim() : null
            );
        }
        catch
        {
            return (null, null);
        }
    }

    private static IReadOnlyList<XcodeRelease> ParseXcodeReleases(string json)
    {
        var releases = new List<XcodeRelease>();

        using var doc = JsonDocument.Parse(json);
        var seen = new HashSet<string>(); // deduplicate by version+build

        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            try
            {
                var version = entry.GetProperty("version");
                var number = version.GetProperty("number").GetString() ?? "";
                var build = version.GetProperty("build").GetString() ?? "";

                var key = $"{number}-{build}";
                if (!seen.Add(key)) continue;

                // Determine if beta
                var releaseInfo = version.GetProperty("release");
                var isBeta = !releaseInfo.TryGetProperty("release", out var isRelease) || !isRelease.GetBoolean();
                if (releaseInfo.TryGetProperty("beta", out _)) isBeta = true;
                if (releaseInfo.TryGetProperty("rc", out _)) isBeta = true;

                // Date
                var dateObj = entry.GetProperty("date");
                var year = dateObj.GetProperty("year").GetInt32();
                var month = dateObj.GetProperty("month").GetInt32();
                var day = dateObj.GetProperty("day").GetInt32();
                var releaseDate = new DateTime(year, month, day);

                // Download URL
                string? downloadUrl = null;
                if (entry.TryGetProperty("links", out var links) &&
                    links.TryGetProperty("download", out var download) &&
                    download.TryGetProperty("url", out var urlProp))
                {
                    downloadUrl = urlProp.GetString();
                }

                // Release notes URL
                string? notesUrl = null;
                if (entry.TryGetProperty("links", out var links2) &&
                    links2.TryGetProperty("notes", out var notes) &&
                    notes.TryGetProperty("url", out var notesProp))
                {
                    notesUrl = notesProp.GetString();
                }

                // Minimum macOS
                string? minMacOS = null;
                if (entry.TryGetProperty("requires", out var requires))
                {
                    minMacOS = requires.GetString();
                }

                // SDKs
                var sdks = new List<XcodeReleaseSdk>();
                if (entry.TryGetProperty("sdks", out var sdksObj))
                {
                    foreach (var sdkPlatform in sdksObj.EnumerateObject())
                    {
                        foreach (var sdk in sdkPlatform.Value.EnumerateArray())
                        {
                            var sdkNum = sdk.GetProperty("number").GetString() ?? "";
                            sdks.Add(new XcodeReleaseSdk(sdkPlatform.Name, sdkNum));
                        }
                    }
                }

                // Compilers
                var compilers = new List<XcodeReleaseCompiler>();
                if (entry.TryGetProperty("compilers", out var compilersObj))
                {
                    foreach (var compilerType in compilersObj.EnumerateObject())
                    {
                        foreach (var compiler in compilerType.Value.EnumerateArray())
                        {
                            var compilerNum = compiler.GetProperty("number").GetString() ?? "";
                            compilers.Add(new XcodeReleaseCompiler(compilerType.Name, compilerNum));
                        }
                    }
                }

                releases.Add(new XcodeRelease(
                    Version: number,
                    BuildNumber: build,
                    ReleaseDate: releaseDate,
                    IsBeta: isBeta,
                    MinimumMacOSVersion: minMacOS,
                    DownloadUrl: downloadUrl,
                    ReleaseNotesUrl: notesUrl,
                    FileSizeBytes: null,
                    Sdks: sdks,
                    Compilers: compilers
                ));
            }
            catch
            {
                // Skip malformed entries
            }
        }

        return releases;
    }

    private static async Task<(int exitCode, string output, string error)> RunProcessAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, output, error);
    }
}
