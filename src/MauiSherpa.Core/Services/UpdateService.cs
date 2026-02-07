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
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

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
                HtmlUrl: r.HtmlUrl ?? ""
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
            var remoteParts = remote.Split('-')[0].Split('.');
            var currentParts = current.Split('-')[0].Split('.');

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
}
