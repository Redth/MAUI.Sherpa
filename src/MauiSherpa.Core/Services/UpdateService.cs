using System.Net.Http.Json;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILoggingService _logger;
    private const string GitHubApiUrl = "https://api.github.com/repos/redth/MAUI.Sherpa/releases";

    public UpdateService(HttpClient httpClient, ILoggingService logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // GitHub API requires User-Agent header
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MauiSherpa");
        }
    }

    public string GetCurrentVersion() => AppInfo.VersionString;

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var currentVersion = GetCurrentVersion();
            _logger.LogInformation("Checking for updates...");
            
            var releases = await GetAllReleasesAsync(cancellationToken);
            
            // Find the latest non-prerelease, non-draft release
            var latestRelease = releases
                .Where(r => !r.IsPrerelease && !r.IsDraft)
                .OrderByDescending(r => r.PublishedAt)
                .FirstOrDefault();
            
            if (latestRelease == null)
            {
                _logger.LogWarning("No releases found");
                return new UpdateCheckResult(false, currentVersion, null);
            }
            
            var updateAvailable = IsNewerVersion(latestRelease.TagName, currentVersion);
            
            if (updateAvailable)
            {
                _logger.LogInformation($"Update available: {latestRelease.TagName}");
            }
            else
            {
                _logger.LogInformation($"Already on latest version: {currentVersion}");
            }
            
            return new UpdateCheckResult(updateAvailable, currentVersion, latestRelease);
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
            var response = await _httpClient.GetAsync(GitHubApiUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };
            
            var releases = await response.Content.ReadFromJsonAsync<List<GitHubReleaseDto>>(jsonOptions, cancellationToken);
            
            if (releases == null)
            {
                return Array.Empty<GitHubRelease>();
            }
            
            return releases.Select(r => new GitHubRelease(
                TagName: r.TagName ?? "",
                Name: r.Name ?? r.TagName ?? "Unnamed Release",
                Body: r.Body ?? "",
                IsPrerelease: r.Prerelease,
                IsDraft: r.Draft,
                PublishedAt: r.PublishedAt ?? DateTime.MinValue,
                HtmlUrl: r.HtmlUrl ?? ""
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to fetch releases from GitHub", ex);
            return Array.Empty<GitHubRelease>();
        }
    }

    private static bool IsNewerVersion(string remoteVersion, string currentVersion)
    {
        // Remove 'v' prefix if present
        var remote = remoteVersion.TrimStart('v');
        var current = currentVersion.TrimStart('v');
        
        try
        {
            var remoteParts = remote.Split('.').Select(int.Parse).ToArray();
            var currentParts = current.Split('.').Select(int.Parse).ToArray();
            
            // Pad with zeros if lengths differ
            var maxLength = Math.Max(remoteParts.Length, currentParts.Length);
            Array.Resize(ref remoteParts, maxLength);
            Array.Resize(ref currentParts, maxLength);
            
            for (int i = 0; i < maxLength; i++)
            {
                if (remoteParts[i] > currentParts[i])
                    return true;
                if (remoteParts[i] < currentParts[i])
                    return false;
            }
            
            return false; // Versions are equal
        }
        catch
        {
            // If version parsing fails, do string comparison as fallback
            return string.Compare(remote, current, StringComparison.OrdinalIgnoreCase) > 0;
        }
    }
}

// DTO for deserializing GitHub API response
internal class GitHubReleaseDto
{
    public string? TagName { get; set; }
    public string? Name { get; set; }
    public string? Body { get; set; }
    public bool Prerelease { get; set; }
    public bool Draft { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? HtmlUrl { get; set; }
}
