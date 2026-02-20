using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILoggingService _logger;
    private readonly string _currentVersion;
    private readonly string _gitHubApiUrl;
    private DateTimeOffset _lastCheckTime = DateTimeOffset.MinValue;
    private UpdateCheckResult? _cachedResult;
    private string? _dismissedVersion;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public UpdateCheckResult? CachedResult => _cachedResult;
    public string? DismissedVersion => _dismissedVersion;

    public void DismissVersion(string version) => _dismissedVersion = version;

    public UpdateService(HttpClient httpClient, ILoggingService logger, string currentVersion, string repoOwner = "Redth", string repoName = "MAUI.Sherpa")
    {
        _httpClient = httpClient;
        _logger = logger;
        _currentVersion = currentVersion;
        _gitHubApiUrl = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases";
    }

    public string GetCurrentVersion() => _currentVersion;

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Return cached result if still fresh
            if (_cachedResult != null && DateTimeOffset.UtcNow - _lastCheckTime < CacheDuration)
            {
                return _cachedResult;
            }

            var currentVersion = GetCurrentVersion();
            _logger.LogInformation("Checking for updates...");

            var releases = await GetAllReleasesAsync(cancellationToken);

            var latestRelease = releases
                .Where(r => !r.IsPrerelease && !r.IsDraft)
                .OrderByDescending(r => r.PublishedAt)
                .FirstOrDefault();

            if (latestRelease == null)
            {
                _logger.LogWarning("No releases found");
                var noRelease = new UpdateCheckResult(false, currentVersion, null);
                _cachedResult = noRelease;
                _lastCheckTime = DateTimeOffset.UtcNow;
                return noRelease;
            }

            var updateAvailable = IsNewerVersion(latestRelease.TagName, currentVersion);

            if (updateAvailable)
                _logger.LogInformation($"Update available: {latestRelease.TagName}");
            else
                _logger.LogInformation($"Already on latest version: {currentVersion}");

            var result = new UpdateCheckResult(updateAvailable, currentVersion, latestRelease);
            _cachedResult = result;
            _lastCheckTime = DateTimeOffset.UtcNow;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to check for updates", ex);
            return new UpdateCheckResult(false, GetCurrentVersion(), null);
        }
    }

    public async Task<IReadOnlyList<GitHubRelease>> GetAllReleasesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(_gitHubApiUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };

            var releases = await response.Content.ReadFromJsonAsync<List<GitHubReleaseDto>>(jsonOptions, cancellationToken);

            if (releases == null)
                return Array.Empty<GitHubRelease>();

            return releases.Select(r => new GitHubRelease(
                TagName: r.TagName ?? "",
                Name: r.Name ?? r.TagName ?? "Unnamed Release",
                Body: r.Body ?? "",
                IsPrerelease: r.Prerelease,
                IsDraft: r.Draft,
                PublishedAt: r.PublishedAt ?? DateTime.MinValue,
                HtmlUrl: r.HtmlUrl ?? "",
                Assets: (r.Assets ?? []).Select(a => new GitHubReleaseAsset(
                    Name: a.Name ?? "",
                    DownloadUrl: a.BrowserDownloadUrl ?? "",
                    Size: a.Size
                )).ToList()
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to fetch releases from GitHub", ex);
            return Array.Empty<GitHubRelease>();
        }
    }

    internal static bool IsNewerVersion(string remoteVersion, string currentVersion)
    {
        var remote = remoteVersion.TrimStart('v');
        var current = currentVersion.TrimStart('v');

        try
        {
            // Strip pre-release (-beta.1) and build metadata (+abc123) suffixes
            var remoteParts = remote.Split(['-', '+'])[0].Split('.');
            var currentParts = current.Split(['-', '+'])[0].Split('.');

            var remoteNumbers = new List<int>();
            var currentNumbers = new List<int>();

            foreach (var part in remoteParts)
            {
                if (int.TryParse(part, out var num))
                    remoteNumbers.Add(num);
                else
                    break;
            }

            foreach (var part in currentParts)
            {
                if (int.TryParse(part, out var num))
                    currentNumbers.Add(num);
                else
                    break;
            }

            var maxLength = Math.Max(remoteNumbers.Count, currentNumbers.Count);
            for (int i = 0; i < maxLength; i++)
            {
                var remotePart = i < remoteNumbers.Count ? remoteNumbers[i] : 0;
                var currentPart = i < currentNumbers.Count ? currentNumbers[i] : 0;

                if (remotePart > currentPart)
                    return true;
                if (remotePart < currentPart)
                    return false;
            }

            return false;
        }
        catch
        {
            return string.Compare(remote, current, StringComparison.OrdinalIgnoreCase) > 0;
        }
    }

    public async Task DownloadAndApplyUpdateAsync(GitHubRelease release, IProgress<(double Percent, string Message)>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsMacCatalyst() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Auto-update is only supported on macOS.");

        // Find the mac zip asset
        var asset = release.Assets.FirstOrDefault(a => a.Name.Contains("mac", StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (asset == null)
            throw new InvalidOperationException("No macOS asset found in the release.");

        _logger.LogInformation($"Auto-update: downloading {asset.Name} ({asset.Size / 1024 / 1024}MB)");

        // Create temp directory for the update
        var updateDir = Path.Combine(Path.GetTempPath(), $"maui-sherpa-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(updateDir);
        var zipPath = Path.Combine(updateDir, asset.Name);

        try
        {
            // Download with progress
            progress?.Report((0, "Downloading update..."));
            using var response = await _httpClient.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
            long bytesRead = 0;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = File.Create(zipPath);
            var buffer = new byte[81920];
            int read;
            while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesRead += read;
                if (totalBytes > 0)
                {
                    var pct = (double)bytesRead / totalBytes * 100;
                    progress?.Report((pct, $"Downloading... {bytesRead / 1024 / 1024}MB / {totalBytes / 1024 / 1024}MB"));
                }
            }

            _logger.LogInformation($"Auto-update: download complete ({bytesRead} bytes)");

            // Extract
            progress?.Report((100, "Extracting update..."));
            var extractDir = Path.Combine(updateDir, "extracted");
            Directory.CreateDirectory(extractDir);

            var unzipPsi = new ProcessStartInfo
            {
                FileName = "unzip",
                Arguments = $"-q \"{zipPath}\" -d \"{extractDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            using var unzipProcess = Process.Start(unzipPsi);
            if (unzipProcess == null) throw new InvalidOperationException("Failed to start unzip.");
            await unzipProcess.WaitForExitAsync(cancellationToken);
            if (unzipProcess.ExitCode != 0)
            {
                var err = await unzipProcess.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException($"Failed to extract update: {err}");
            }

            // Find the .app bundle in the extracted directory
            var extractedApp = Directory.GetDirectories(extractDir, "*.app", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (extractedApp == null)
                throw new InvalidOperationException("No .app bundle found in the extracted update.");

            // Resolve current .app path: AppContext.BaseDirectory = .../MAUI Sherpa.app/Contents/MonoBundle/
            var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var currentAppPath = Path.GetFullPath(Path.Combine(baseDir, "..", ".."));

            if (!currentAppPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Could not determine current .app path. Resolved: {currentAppPath}");

            _logger.LogInformation($"Auto-update: replacing {currentAppPath}");
            progress?.Report((100, "Restarting..."));

            // Write the update script
            var pid = Environment.ProcessId;
            var scriptPath = Path.Combine(updateDir, "update.sh");
            var script = $"""
                #!/bin/bash
                # Wait for the old process to exit
                while kill -0 {pid} 2>/dev/null; do sleep 0.5; done
                sleep 1
                # Replace the app bundle
                rm -rf "{currentAppPath}"
                mv "{extractedApp}" "{currentAppPath}"
                # Relaunch
                open "{currentAppPath}"
                # Clean up
                rm -rf "{updateDir}"
                """;
            await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

            // Make executable and launch detached
            var chmodPsi = new ProcessStartInfo { FileName = "chmod", Arguments = $"+x \"{scriptPath}\"", UseShellExecute = false, CreateNoWindow = true };
            Process.Start(chmodPsi)?.WaitForExit();

            var scriptPsi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = scriptPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            Process.Start(scriptPsi);

            _logger.LogInformation("Auto-update: update script launched, exiting app");

            // Give the script a moment to start, then exit
            await Task.Delay(500, CancellationToken.None);
            Environment.Exit(0);
        }
        catch (OperationCanceledException)
        {
            // Clean up on cancel
            try { Directory.Delete(updateDir, true); } catch { }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Auto-update failed: {ex.Message}", ex);
            try { Directory.Delete(updateDir, true); } catch { }
            throw;
        }
    }
}

internal class GitHubReleaseDto
{
    public string? TagName { get; set; }
    public string? Name { get; set; }
    public string? Body { get; set; }
    public bool Prerelease { get; set; }
    public bool Draft { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? HtmlUrl { get; set; }
    public List<GitHubReleaseAssetDto>? Assets { get; set; }
}

internal class GitHubReleaseAssetDto
{
    public string? Name { get; set; }
    public string? BrowserDownloadUrl { get; set; }
    public long Size { get; set; }
}
