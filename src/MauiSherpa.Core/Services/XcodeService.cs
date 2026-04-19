using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
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

    internal const string ApplicationsDirectory = "/Applications";
    internal const string ManagedXcodeAppPath = "/Applications/Xcode.app";
    private const string XcodesAppName = "Xcodes.app";
    private const string ManagedXcodeAppTempLinkPath = "/Applications/.Xcode.app.maui-sherpa-tmp";
    private const string XcodeReleasesUrl = "https://xcodereleases.com/data.json";
    private static readonly string[] SystemUnxipExecutableCandidates =
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
        var managedDefaultState = await GetManagedDefaultStateAsync();
        var managedDefaultTargetPath = managedDefaultState.PhysicalTargetPath;

        try
        {
            if (!Directory.Exists(ApplicationsDirectory)) return [];

            var xcodeApps = Directory.GetDirectories(ApplicationsDirectory, "Xcode*.app")
                .Where(p => !Path.GetFileName(p).Equals(XcodesAppName, StringComparison.OrdinalIgnoreCase))
                .Where(p => !(managedDefaultState.IsSymlink && PathsEqual(p, ManagedXcodeAppPath)))
                .OrderBy(p => p)
                .ToList();

            foreach (var appPath in xcodeApps)
            {
                try
                {
                    var (version, build) = await GetXcodeVersionAsync(appPath);
                    if (version == null) continue;

                    var isDefault = managedDefaultTargetPath != null &&
                        PathsEqual(managedDefaultTargetPath, appPath);
                    var isSelected = IsSelectedXcodePath(selectedPath, appPath, isDefault);

                    installations.Add(new XcodeInstallation(
                        Path: appPath,
                        Version: version,
                        BuildNumber: build ?? "unknown",
                        IsSelected: isSelected,
                        IsDefault: isDefault
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

        if (!Directory.Exists(xcodeAppPath))
        {
            _logger.LogError($"Xcode path not found: {xcodeAppPath}");
            return false;
        }

        try
        {
            var managedDefaultState = await GetManagedDefaultStateAsync();
            var selectedDeveloperDir = Path.Combine(xcodeAppPath, "Contents", "Developer");
            if (!Directory.Exists(selectedDeveloperDir))
            {
                _logger.LogError($"Developer directory not found: {selectedDeveloperDir}");
                return false;
            }

            var existingPaths = Directory.GetDirectories(ApplicationsDirectory, "Xcode*.app")
                .Where(p => !Path.GetFileName(p).Equals(XcodesAppName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var selectionPlan = CreateSelectionPlan(xcodeAppPath, managedDefaultState, existingPaths, await GetBundleSeparatorAsync());
            var script = CreateSelectionScript(selectionPlan);
            var result = await RunElevatedShellScriptAsync(script);

            if (result.exitCode == 0)
            {
                _logger.LogInformation($"Switched active Xcode to: {selectionPlan.SelectedAppPath}");
                return true;
            }

            _logger.LogError($"Failed to switch Xcode: {result.error}");
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
                _logger.LogError("No Xcode*.app found after expanding archive");
                progress?.Report("Error: No Xcode app bundle was found in the archive");
                return false;
            }

            var (version, buildNumber) = await GetXcodeVersionAsync(xcodeApp);
            if (version == null)
            {
                _logger.LogError("Failed to determine Xcode version/build from extracted app");
                progress?.Report("Error: Failed to determine Xcode version/build from extracted app");
                return false;
            }

            var existingPaths = Directory.GetDirectories(targetDirectory, "Xcode*.app")
                .Where(path => !PathsEqual(path, xcodeApp))
                .Where(path => !Path.GetFileName(path).Equals(XcodesAppName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var separator = await GetBundleSeparatorAsync();
            var destinationPath = ResolveManagedXcodeBundlePath(targetDirectory, version, buildNumber ?? "unknown", existingPaths, separator);

            // Step 3: Move to target directory with admin privileges
            progress?.Report($"Moving Xcode to {targetDirectory}...");
            var moveResult = await RunElevatedShellScriptAsync($"""
                source_path='{EscapeShellSingleQuotedString(xcodeApp)}'
                destination_path='{EscapeShellSingleQuotedString(destinationPath)}'

                if [ ! -d "$source_path" ]; then
                    echo "Expanded Xcode app not found: $source_path" >&2
                    exit 1
                fi

                mv "$source_path" "$destination_path"
                """, ct);

            if (moveResult.exitCode != 0)
            {
                _logger.LogError($"Failed to move Xcode: {moveResult.error}");
                progress?.Report($"Failed to move Xcode: {moveResult.error}");
                return false;
            }

            // Step 4: Run first-launch setup
            progress?.Report("Running first-launch setup...");
            var xcodebuild = Path.Combine(destinationPath, "Contents", "Developer", "usr", "bin", "xcodebuild");
            if (File.Exists(xcodebuild))
            {
                var firstLaunchResult = await RunElevatedShellScriptAsync($"""
                    xcodebuild_path='{EscapeShellSingleQuotedString(xcodebuild)}'
                    "$xcodebuild_path" -runFirstLaunch
                    """, ct);
                if (firstLaunchResult.exitCode != 0)
                {
                    _logger.LogWarning($"First-launch setup failed for {destinationPath}: {firstLaunchResult.error}");
                    progress?.Report($"First-launch setup reported an error: {firstLaunchResult.error}");
                }
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

    // ── Bundle name normalization ───────────────────────────────────────

    public async Task<XcodeNormalizationPlan> GetNormalizationPlanAsync()
    {
        var separator = await GetBundleSeparatorAsync();

        if (!IsSupported || !Directory.Exists(ApplicationsDirectory))
            return new XcodeNormalizationPlan(separator, [], null);

        var candidateBundles = new List<(string Path, string Version, string BuildNumber)>();

        var xcodeApps = Directory.GetDirectories(ApplicationsDirectory, "Xcode*.app")
            .Where(p =>
            {
                var name = Path.GetFileName(p);
                if (name.Equals(XcodesAppName, StringComparison.OrdinalIgnoreCase)) return false;
                // Exclude the canonical /Applications/Xcode.app slot — managed by the selection flow.
                if (PathsEqual(p, ManagedXcodeAppPath)) return false;
                // Exclude symlinks (resolved separately).
                if (TryResolveDirectoryLinkTarget(p) != null) return false;
                return true;
            })
            .ToList();

        foreach (var appPath in xcodeApps)
        {
            var (version, buildNumber) = await GetXcodeVersionAsync(appPath);
            if (string.IsNullOrWhiteSpace(version)) continue;
            candidateBundles.Add((appPath, version!, buildNumber ?? "unknown"));
        }

        return ComputeNormalizationPlan(
            candidateBundles,
            separator,
            currentSymlinkTarget: TryResolveDirectoryLinkTarget(ManagedXcodeAppPath));
    }

    /// <summary>
    /// Pure planning helper: given the set of discovered Xcode bundles and the
    /// desired separator, return the renames required to bring them into line.
    /// Visible for unit tests.
    /// </summary>
    internal static XcodeNormalizationPlan ComputeNormalizationPlan(
        IReadOnlyList<(string Path, string Version, string BuildNumber)> bundles,
        string separator,
        string? currentSymlinkTarget)
    {
        separator = NormalizeBundleSeparator(separator);
        var renames = new List<XcodeBundleRename>();

        // Snapshot of paths we're reserving — starts as every existing bundle path.
        // As renames resolve, source paths are released and destination paths claimed.
        var reservedPaths = new HashSet<string>(
            bundles.Select(b => NormalizePath(b.Path)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var bundle in bundles.OrderBy(b => b.Path, StringComparer.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(bundle.Path) ?? ApplicationsDirectory;
            var currentName = Path.GetFileName(bundle.Path);

            // Don't touch bundles that don't look managed. Our heuristic: starts with
            // "Xcode_" or "Xcode-". A plain "Xcode.app" is already excluded above but
            // skipped here too as a safety net.
            if (!LooksLikeManagedBundleName(currentName)) continue;

            // Release this bundle's path from the reserved set while we plan its move.
            reservedPaths.Remove(NormalizePath(bundle.Path));

            var existingForResolve = reservedPaths.ToList();
            var desiredPath = ResolveManagedXcodeBundlePath(
                directory, bundle.Version, bundle.BuildNumber, existingForResolve, separator);

            if (PathsEqual(desiredPath, bundle.Path))
            {
                // Already in the right shape — just re-reserve and continue.
                reservedPaths.Add(NormalizePath(bundle.Path));
                continue;
            }

            renames.Add(new XcodeBundleRename(
                FromPath: bundle.Path,
                ToPath: desiredPath,
                Version: bundle.Version,
                BuildNumber: bundle.BuildNumber));

            reservedPaths.Add(NormalizePath(desiredPath));
        }

        string? symlinkRetargetPath = null;
        if (!string.IsNullOrWhiteSpace(currentSymlinkTarget))
        {
            var rename = renames.FirstOrDefault(r => PathsEqual(r.FromPath, currentSymlinkTarget!));
            if (rename is not null)
                symlinkRetargetPath = rename.ToPath;
        }

        return new XcodeNormalizationPlan(separator, renames, symlinkRetargetPath);
    }

    public async Task<bool> NormalizeBundleNamesAsync(
        XcodeNormalizationPlan plan,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!IsSupported) return false;
        if (plan is null || !plan.HasWork)
        {
            progress?.Report("No bundles to normalize.");
            return true;
        }

        progress?.Report($"Normalizing {plan.Renames.Count} Xcode bundle name(s)...");

        var sb = new System.Text.StringBuilder();
        foreach (var rename in plan.Renames)
        {
            var from = EscapeShellSingleQuotedString(rename.FromPath);
            var to = EscapeShellSingleQuotedString(rename.ToPath);
            sb.AppendLine("if [ -d '" + from + "' ] && [ ! -e '" + to + "' ]; then");
            sb.AppendLine("  mv '" + from + "' '" + to + "'");
            sb.AppendLine("fi");
        }

        if (plan.SymlinkRetargetPath is not null)
        {
            var canonical = EscapeShellSingleQuotedString(ManagedXcodeAppPath);
            var newTarget = EscapeShellSingleQuotedString(plan.SymlinkRetargetPath);
            sb.AppendLine("if [ -L '" + canonical + "' ]; then");
            sb.AppendLine("  rm '" + canonical + "'");
            sb.AppendLine("  ln -s '" + newTarget + "' '" + canonical + "'");
            // Re-run xcode-select so the active developer dir points at the new bundle path.
            sb.AppendLine("  xcode-select -s '" + newTarget + "/Contents/Developer' || true");
            sb.AppendLine("fi");
        }

        try
        {
            var result = await RunElevatedShellScriptAsync(sb.ToString(), ct);
            if (result.exitCode != 0)
            {
                _logger.LogError($"Failed to normalize Xcode bundle names: {result.error}");
                progress?.Report($"Failed to normalize bundle names: {result.error}");
                return false;
            }

            progress?.Report("Xcode bundle names normalized.");
            _logger.LogInformation($"Normalized {plan.Renames.Count} Xcode bundle name(s) with separator '{plan.Separator}'.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception normalizing Xcode bundle names: {ex.Message}", ex);
            progress?.Report($"Normalize failed: {ex.Message}");
            return false;
        }
    }

    private static bool LooksLikeManagedBundleName(string bundleName) =>
        (bundleName.StartsWith("Xcode_", StringComparison.OrdinalIgnoreCase) ||
         bundleName.StartsWith("Xcode-", StringComparison.OrdinalIgnoreCase)) &&
        bundleName.EndsWith(".app", StringComparison.OrdinalIgnoreCase);

    // ── Private helpers ─────────────────────────────────────────────────

    private async Task<XcodeManagedDefaultState> GetManagedDefaultStateAsync()
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

        var (version, buildNumber) = await GetXcodeVersionAsync(ManagedXcodeAppPath);
        return new XcodeManagedDefaultState(
            CanonicalAppPath: ManagedXcodeAppPath,
            Exists: true,
            IsSymlink: false,
            LinkTargetPath: null,
            Version: version,
            BuildNumber: buildNumber ?? "unknown");
    }

    private static bool IsSelectedXcodePath(string? selectedDeveloperDir, string appPath, bool isDefault)
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

    internal static string GetManagedXcodeBundleName(string version, string separator) =>
        $"Xcode{separator}{SanitizeXcodeBundleSegment(version, separator)}.app";

    // Retained for tests/compat: produces the collision-disambiguated name with a
    // build-number suffix appended. Not used for the default install path.
    internal static string GetManagedXcodeBundleNameWithBuild(string version, string buildNumber, string separator) =>
        $"Xcode{separator}{SanitizeXcodeBundleSegment(version, separator)}{separator}{SanitizeXcodeBundleSegment(buildNumber, separator)}.app";

    internal static string ResolveManagedXcodeBundlePath(
        string targetDirectory,
        string version,
        string buildNumber,
        IEnumerable<string> existingPaths,
        string separator)
    {
        var normalizedExistingPaths = new HashSet<string>(
            existingPaths.Select(NormalizePath),
            StringComparer.OrdinalIgnoreCase);

        // 1. Prefer the plain `Xcode<sep><version>.app` form.
        var preferredPath = Path.Combine(targetDirectory, GetManagedXcodeBundleName(version, separator));
        if (!normalizedExistingPaths.Contains(NormalizePath(preferredPath)))
            return preferredPath;

        // 2. Disambiguate with the build number: `Xcode<sep><version><sep><build>.app`.
        var withBuildPath = Path.Combine(targetDirectory, GetManagedXcodeBundleNameWithBuild(version, buildNumber, separator));
        if (!normalizedExistingPaths.Contains(NormalizePath(withBuildPath)))
            return withBuildPath;

        // 3. Final fallback: numeric suffix.
        var baseName = Path.GetFileNameWithoutExtension(withBuildPath);
        for (var suffix = 2; ; suffix++)
        {
            var candidatePath = Path.Combine(targetDirectory, $"{baseName}{separator}{suffix}.app");
            if (!normalizedExistingPaths.Contains(NormalizePath(candidatePath)))
                return candidatePath;
        }
    }

    internal static XcodeSelectionPlan CreateSelectionPlan(
        string selectedAppPath,
        XcodeManagedDefaultState managedDefaultState,
        IEnumerable<string> existingPaths,
        string separator = XcodeBundleSeparatorOptions.Underscore)
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
            existingPaths.Where(path => !PathsEqual(path, managedDefaultState.CanonicalAppPath)),
            separator);

        if (PathsEqual(normalizedSelectedAppPath, managedDefaultState.CanonicalAppPath))
            normalizedSelectedAppPath = migrationDestinationPath;

        return new XcodeSelectionPlan(
            CanonicalAppPath: managedDefaultState.CanonicalAppPath,
            SelectedAppPath: normalizedSelectedAppPath,
            MigrationSourcePath: managedDefaultState.CanonicalAppPath,
            MigrationDestinationPath: migrationDestinationPath);
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
        CancellationToken ct = default)
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
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(appleScript);

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start osascript");
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

    private static string SanitizeXcodeBundleSegment(string value, string separator)
    {
        var sanitized = Regex.Replace(value.Trim(), @"[^A-Za-z0-9.\-]+", separator);
        // Collapse runs of the separator.
        sanitized = Regex.Replace(sanitized, Regex.Escape(separator) + "+", separator);
        sanitized = sanitized.Trim(separator[0]);
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private async Task<string> GetBundleSeparatorAsync()
    {
        try
        {
            var settings = await _settingsService.GetSettingsAsync();
            return NormalizeBundleSeparator(settings.Preferences.XcodeBundleSeparator);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to read XcodeBundleSeparator preference; defaulting to '_': {ex.Message}");
            return XcodeBundleSeparatorOptions.Underscore;
        }
    }

    internal static string NormalizeBundleSeparator(string? value) =>
        string.Equals(value, XcodeBundleSeparatorOptions.Hyphen, StringComparison.Ordinal)
            ? XcodeBundleSeparatorOptions.Hyphen
            : XcodeBundleSeparatorOptions.Underscore;

    private static string EscapeShellSingleQuotedString(string value) =>
        value.Replace("'", "'\"'\"'");

    private static string EscapeAppleScriptDoubleQuotedString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

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

    internal static string? FindBundledUnxipExecutable(string baseDirectory, string runtimeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory) || string.IsNullOrWhiteSpace(runtimeIdentifier))
            return null;

        var bundledPath = Path.Combine(baseDirectory, "runtimes", runtimeIdentifier, "native", "unxip");
        if (File.Exists(bundledPath))
            return bundledPath;

        if (!runtimeIdentifier.StartsWith("maccatalyst-", StringComparison.OrdinalIgnoreCase))
            return null;

        var osxRid = runtimeIdentifier.Replace("maccatalyst-", "osx-", StringComparison.OrdinalIgnoreCase);
        var osxPath = Path.Combine(baseDirectory, "runtimes", osxRid, "native", "unxip");
        return File.Exists(osxPath) ? osxPath : null;
    }

    private static async Task<string?> FindUnxipExecutableAsync()
    {
        var bundledPath = FindBundledUnxipExecutable(AppContext.BaseDirectory, RuntimeInformation.RuntimeIdentifier);
        if (!string.IsNullOrWhiteSpace(bundledPath))
            return bundledPath;

        foreach (var candidate in SystemUnxipExecutableCandidates)
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

internal readonly record struct XcodeManagedDefaultState(
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

internal readonly record struct XcodeSelectionPlan(
    string CanonicalAppPath,
    string SelectedAppPath,
    string? MigrationSourcePath,
    string? MigrationDestinationPath
);
