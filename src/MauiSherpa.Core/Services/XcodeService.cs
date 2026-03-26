using System.Diagnostics;
using System.Net.Http;
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
    private readonly IAppleDownloadAuthService _authService;
    private readonly IEncryptedSettingsService _settingsService;

    private const string XcodeReleasesUrl = "https://xcodereleases.com/data.json";
    private static readonly string[] UnxipExecutableCandidates =
    [
        "/opt/homebrew/bin/unxip",
        "/usr/local/bin/unxip",
        "/opt/local/bin/unxip"
    ];

    public bool IsSupported => _platform.IsMacCatalyst || _platform.IsMacOS;

    public XcodeService(
        ILoggingService logger,
        IPlatformService platform,
        HttpClient httpClient,
        IAppleDownloadAuthService authService,
        IEncryptedSettingsService settingsService)
    {
        _logger = logger;
        _platform = platform;
        _httpClient = httpClient;
        _authService = authService;
        _settingsService = settingsService;
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

    public async Task<bool> UninstallXcodeAsync(string xcodeAppPath)
    {
        if (!IsSupported) return false;

        // Safety: only allow uninstalling from /Applications and must be an Xcode*.app
        var fileName = Path.GetFileName(xcodeAppPath);
        if (!xcodeAppPath.StartsWith("/Applications/", StringComparison.Ordinal) ||
            !fileName.StartsWith("Xcode", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError($"Refusing to uninstall: path does not appear to be a valid Xcode installation: {xcodeAppPath}");
            return false;
        }

        if (!Directory.Exists(xcodeAppPath))
        {
            _logger.LogError($"Xcode installation not found: {xcodeAppPath}");
            return false;
        }

        try
        {
            // Move to Trash via Finder (preserves "put back" capability)
            var escapedPath = xcodeAppPath.Replace("\"", "\\\"");
            var script = $"tell application \"Finder\" to delete POSIX file \"{escapedPath}\"";
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
                _logger.LogInformation($"Uninstalled Xcode: {xcodeAppPath} (moved to Trash)");
                return true;
            }

            _logger.LogError($"Failed to uninstall Xcode: {error}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to uninstall Xcode: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> DownloadXcodeAsync(
        XcodeRelease release,
        string destinationPath,
        IProgress<XcodeDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(release.DownloadUrl))
        {
            _logger.LogError($"No download URL available for Xcode {release.Version}");
            return false;
        }

        try
        {
            _logger.LogInformation($"Downloading Xcode {release.Version} from {release.DownloadUrl}...");

            // Use the auth service's shared cookie jar — cookies from SRP auth + Olympus session
            // are already there, and listDownloads.action will add ADCDownloadAuth
            using var downloadClient = _authService.CreateAuthenticatedHttpClient();

            // Step 1: POST to listDownloads.action to establish ADCDownloadAuth cookie
            var listRequest = new HttpRequestMessage(HttpMethod.Post,
                "https://developer.apple.com/services-account/QH65B2/downloadws/listDownloads.action");
            listRequest.Headers.Add("Accept", "application/json");
            try
            {
                var listResponse = await downloadClient.SendAsync(listRequest, ct);
                _logger.LogInformation($"listDownloads response: {(int)listResponse.StatusCode}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"listDownloads failed (continuing): {ex.Message}");
            }

            // Step 2: Download the file. We must handle redirects manually because
            // .NET doesn't forward Cookie headers on redirects, and Apple's CDN
            // requires the cookies on the initial request to authorize the redirect.
            var allCookies = _authService.GetAllCookies();
            var cookieHeader = string.Join("; ", allCookies.Select(c => $"{c.Name}={c.Value}"));
            _logger.LogInformation($"Sending {allCookies.Count} cookies for download");

            using var noRedirectHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            };
            using var redirectClient = new HttpClient(noRedirectHandler);
            redirectClient.DefaultRequestHeaders.Add("User-Agent", "MauiSherpa");
            redirectClient.Timeout = TimeSpan.FromHours(4);

            // Support resume if partial file exists
            long existingBytes = 0;
            if (File.Exists(destinationPath))
                existingBytes = new FileInfo(destinationPath).Length;

            // Follow redirects manually, forwarding cookies each hop
            var currentUrl = release.DownloadUrl;
            HttpResponseMessage response;
            for (var redirectCount = 0; ; redirectCount++)
            {
                if (redirectCount > 10)
                    throw new Exception("Too many redirects");

                var req = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                req.Headers.Add("Cookie", cookieHeader);
                if (existingBytes > 0)
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);

                response = await redirectClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                _logger.LogInformation($"Request {currentUrl} → {(int)response.StatusCode}");

                // 416 = Range Not Satisfiable — file may already be fully downloaded
                if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable && existingBytes > 0)
                {
                    _logger.LogInformation($"File already fully downloaded ({existingBytes:N0} bytes), skipping download");
                    response.Dispose();
                    return true;
                }

                if ((int)response.StatusCode is >= 300 and < 400
                    && response.Headers.Location is { } location)
                {
                    var redirectUri = location.IsAbsoluteUri ? location : new Uri(new Uri(currentUrl), location);
                    currentUrl = redirectUri.AbsoluteUri;
                    response.Dispose();
                    continue;
                }
                break;
            }

            _logger.LogInformation($"Final download URL: {currentUrl}, status: {(int)response.StatusCode}");

            // Detect unauthorized redirect
            if (currentUrl.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
                || currentUrl.Contains("appleid.apple.com")
                || currentUrl.Contains("/account/"))
            {
                _logger.LogError($"Download unauthorized — redirected to {currentUrl}");
                response.Dispose();
                return false;
            }

            // Verify we're getting binary data, not HTML
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError($"Download returned HTML instead of binary (content-type: {contentType}, url: {currentUrl})");
                response.Dispose();
                return false;
            }

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            if (response.StatusCode == System.Net.HttpStatusCode.PartialContent && totalBytes.HasValue)
                totalBytes += existingBytes;
            else if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                existingBytes = 0; // Server didn't honor range, start over

            var fileMode = existingBytes > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent
                ? FileMode.Append
                : FileMode.Create;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(destinationPath, fileMode, FileAccess.Write, FileShare.None, 81920, true);

            _logger.LogInformation($"Starting file write, total expected: {totalBytes?.ToString("N0") ?? "unknown"} bytes");

            var buffer = new byte[81920];
            long totalReceived = existingBytes;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalReceived += bytesRead;

                var elapsed = stopwatch.Elapsed.TotalSeconds;
                var speed = elapsed > 0 ? (totalReceived - existingBytes) / elapsed : 0;
                var remaining = totalBytes.HasValue && speed > 0
                    ? TimeSpan.FromSeconds((totalBytes.Value - totalReceived) / speed)
                    : (TimeSpan?)null;

                progress?.Report(new XcodeDownloadProgress(totalReceived, totalBytes, speed, remaining));
            }

            _logger.LogInformation($"Downloaded Xcode {release.Version} ({totalReceived:N0} bytes) to {destinationPath}");
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation($"Xcode {release.Version} download cancelled");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to download Xcode {release.Version}: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> InstallXcodeAsync(
        string xipPath,
        string? targetDirectory = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!IsSupported) return false;

        targetDirectory ??= "/Applications";

        if (!File.Exists(xipPath))
        {
            _logger.LogError($"Xcode archive not found: {xipPath}");
            return false;
        }

        try
        {
            var expandDir = Path.GetDirectoryName(xipPath) ?? Path.GetTempPath();
            var extractor = await ResolveArchiveExtractionCommandAsync(xipPath);

            if (extractor.FellBackToSystemXip)
            {
                _logger.LogWarning("unxip is selected for Xcode extraction but was not found. Falling back to system xip.");
                progress?.Report("unxip is selected but was not found. Falling back to system xip...");
            }

            progress?.Report(extractor.Preference == XcodeArchiveExtractorOptions.Unxip
                ? "Unarchiving Xcode with unxip (faster, no signature verification)..."
                : "Unarchiving Xcode with system xip (this can take a while)...");

            var expandResult = await RunProcessAsync(extractor.FileName, extractor.Arguments, expandDir);

            // xip expands into the current directory, so we need to look for Xcode*.app
            if (expandResult.exitCode != 0)
            {
                _logger.LogError($"Failed to expand archive: {expandResult.error}");
                progress?.Report($"Failed to unarchive: {expandResult.error}");
                return false;
            }

            // Step 2: Find the expanded .app
            var expandedApps = Directory.GetDirectories(expandDir, "Xcode*.app");
            var xcodeApp = expandedApps.FirstOrDefault();
            if (xcodeApp == null)
            {
                _logger.LogError("No Xcode.app found after expanding archive");
                progress?.Report("Error: No Xcode.app found in archive");
                return false;
            }

            // Step 3: Move to target directory with admin privileges
            var appName = Path.GetFileName(xcodeApp);
            var destinationPath = Path.Combine(targetDirectory, appName);

            if (Directory.Exists(destinationPath))
            {
                progress?.Report($"Removing existing {appName}...");
                var removeScript = $"do shell script \"rm -rf '{destinationPath}'\" with administrator privileges";
                var removePsi = new ProcessStartInfo { FileName = "osascript", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                removePsi.ArgumentList.Add("-e");
                removePsi.ArgumentList.Add(removeScript);
                using var removeProcess = Process.Start(removePsi);
                if (removeProcess != null) await removeProcess.WaitForExitAsync(ct);
            }

            progress?.Report($"Moving Xcode to {targetDirectory}...");
            var moveScript = $"do shell script \"mv '{xcodeApp}' '{destinationPath}'\" with administrator privileges";
            var movePsi = new ProcessStartInfo { FileName = "osascript", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            movePsi.ArgumentList.Add("-e");
            movePsi.ArgumentList.Add(moveScript);

            using var moveProcess = Process.Start(movePsi) ?? throw new InvalidOperationException("Failed to start osascript");
            var moveError = await moveProcess.StandardError.ReadToEndAsync();
            await moveProcess.WaitForExitAsync(ct);

            if (moveProcess.ExitCode != 0)
            {
                _logger.LogError($"Failed to move Xcode: {moveError}");
                progress?.Report($"Failed to move Xcode: {moveError}");
                return false;
            }

            // Step 4: Run first-launch setup
            progress?.Report("Running first-launch setup...");
            var xcodebuild = Path.Combine(destinationPath, "Contents", "Developer", "usr", "bin", "xcodebuild");
            if (File.Exists(xcodebuild))
            {
                var firstLaunchScript = $"do shell script \"\'{xcodebuild}\' -runFirstLaunch\" with administrator privileges";
                var flPsi = new ProcessStartInfo { FileName = "osascript", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                flPsi.ArgumentList.Add("-e");
                flPsi.ArgumentList.Add(firstLaunchScript);
                using var flProcess = Process.Start(flPsi);
                if (flProcess != null) await flProcess.WaitForExitAsync(ct);
            }

            // Step 5: Cleanup archive
            progress?.Report("Cleaning up...");
            try { File.Delete(xipPath); } catch { /* best effort */ }

            progress?.Report($"Xcode installed to {destinationPath}");
            _logger.LogInformation($"Installed Xcode to {destinationPath}");
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Xcode installation cancelled");
            progress?.Report("Installation cancelled");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to install Xcode: {ex.Message}", ex);
            progress?.Report($"Installation failed: {ex.Message}");
            return false;
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────

    internal static XcodeArchiveExtractionCommand CreateArchiveExtractionCommand(string xipPath, string? preference, string? unxipPath)
    {
        var normalizedPreference = NormalizeArchiveExtractorPreference(preference);
        if (normalizedPreference == XcodeArchiveExtractorOptions.Unxip &&
            !string.IsNullOrWhiteSpace(unxipPath))
        {
            return new XcodeArchiveExtractionCommand(
                FileName: unxipPath,
                Arguments: $"\"{xipPath}\"",
                Preference: normalizedPreference,
                FellBackToSystemXip: false);
        }

        return new XcodeArchiveExtractionCommand(
            FileName: "xip",
            Arguments: $"--expand \"{xipPath}\"",
            Preference: XcodeArchiveExtractorOptions.SystemXip,
            FellBackToSystemXip: normalizedPreference == XcodeArchiveExtractorOptions.Unxip);
    }

    internal static string NormalizeArchiveExtractorPreference(string? preference) =>
        string.Equals(preference, XcodeArchiveExtractorOptions.Unxip, StringComparison.OrdinalIgnoreCase)
            ? XcodeArchiveExtractorOptions.Unxip
            : XcodeArchiveExtractorOptions.SystemXip;

    private async Task<XcodeArchiveExtractionCommand> ResolveArchiveExtractionCommandAsync(string xipPath)
    {
        var preference = XcodeArchiveExtractorOptions.SystemXip;

        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            preference = settings.Preferences.XcodeArchiveExtractor;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load Xcode extractor preference, using system xip: {ex.Message}");
        }

        var unxipPath = NormalizeArchiveExtractorPreference(preference) == XcodeArchiveExtractorOptions.Unxip
            ? await FindUnxipExecutableAsync()
            : null;

        return CreateArchiveExtractionCommand(xipPath, preference, unxipPath);
    }

    private static async Task<string?> FindUnxipExecutableAsync()
    {
        foreach (var candidate in UnxipExecutableCandidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var result = await RunProcessAsync("/usr/bin/which", "unxip");
        if (result.exitCode != 0)
            return null;

        var resolved = result.output.Trim();
        return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
    }

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

    private static async Task<(int exitCode, string output, string error)> RunProcessAsync(
        string fileName, string arguments, string? workingDirectory = null)
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
        if (workingDirectory != null)
            psi.WorkingDirectory = workingDirectory;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, output, error);
    }
}

internal readonly record struct XcodeArchiveExtractionCommand(
    string FileName,
    string Arguments,
    string Preference,
    bool FellBackToSystemXip
);
