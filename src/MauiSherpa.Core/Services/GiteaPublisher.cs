using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Secrets publisher for Gitea Actions
/// Note: Gitea doesn't have an official .NET SDK, so we use HttpClient directly
/// </summary>
public class GiteaPublisher : ISecretsPublisher
{
    private readonly HttpClient _httpClient;
    private readonly string _serverUrl;
    private readonly ILoggingService _logger;

    public string ProviderId => "gitea";
    public string DisplayName => "Gitea";
    public string IconClass => "fa-solid fa-code-branch";

    public GiteaPublisher(SecretsPublisherConfig config, ILoggingService logger)
    {
        _logger = logger;
        var token = config.Settings.GetValueOrDefault("AccessToken") 
            ?? throw new ArgumentException("AccessToken is required");
        _serverUrl = config.Settings.GetValueOrDefault("ServerUrl") 
            ?? throw new ArgumentException("ServerUrl is required");

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_serverUrl.TrimEnd('/') + "/api/v1/")
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MauiSherpa", "1.0"));
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("user", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var user = JsonSerializer.Deserialize<GiteaUser>(json);
                _logger.LogInformation($"Gitea connection successful, authenticated as: {user?.Login}");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Gitea connection test failed: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<IReadOnlyList<PublisherRepository>> ListRepositoriesAsync(string? filter = null, CancellationToken cancellationToken = default)
    {
        var repos = new List<PublisherRepository>();
        var page = 1;
        const int limit = 50;

        try
        {
            while (true)
            {
                var response = await _httpClient.GetAsync($"user/repos?page={page}&limit={limit}", cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var pageRepos = JsonSerializer.Deserialize<List<GiteaRepo>>(json);
                if (pageRepos == null || pageRepos.Count == 0)
                    break;

                foreach (var repo in pageRepos)
                {
                    // Apply filter if provided
                    if (!string.IsNullOrEmpty(filter) && 
                        !repo.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                        !(repo.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                    {
                        continue;
                    }

                    repos.Add(new PublisherRepository(
                        repo.Id.ToString(),
                        repo.Name,
                        repo.FullName,
                        repo.Description,
                        repo.HtmlUrl
                    ));
                }

                if (pageRepos.Count < limit || repos.Count >= 500)
                    break;

                page++;
            }

            _logger.LogInformation($"Found {repos.Count} Gitea repositories");
            return repos;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to list Gitea repositories: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> ListSecretsAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        try
        {
            // repositoryId is "owner/repo" format
            var response = await _httpClient.GetAsync($"repos/{repositoryId}/actions/secrets", cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<GiteaSecretsResponse>(json);
            return result?.Secrets?.Select(s => s.Name).ToList() ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to list secrets for {repositoryId}: {ex.Message}", ex);
            throw;
        }
    }

    public async Task PublishSecretAsync(string repositoryId, string secretName, string secretValue, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation($"Publishing secret '{secretName}' to {repositoryId}");

            var request = new GiteaCreateSecretRequest { Data = secretValue };
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            // Gitea uses PUT for create/update
            var response = await _httpClient.PutAsync(
                $"repos/{repositoryId}/actions/secrets/{secretName}",
                content,
                cancellationToken);

            response.EnsureSuccessStatusCode();
            _logger.LogInformation($"Successfully published secret '{secretName}'");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to publish secret '{secretName}': {ex.Message}", ex);
            throw;
        }
    }

    public async Task PublishSecretsAsync(string repositoryId, IReadOnlyDictionary<string, string> secrets, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Publishing {secrets.Count} secrets to {repositoryId}");

        var count = 0;
        foreach (var (name, value) in secrets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Publishing {name} ({++count}/{secrets.Count})...");
            await PublishSecretAsync(repositoryId, name, value, cancellationToken);
        }

        _logger.LogInformation($"Successfully published {secrets.Count} secrets");
    }

    public async Task DeleteSecretAsync(string repositoryId, string secretName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation($"Deleting secret '{secretName}' from {repositoryId}");
            
            var response = await _httpClient.DeleteAsync(
                $"repos/{repositoryId}/actions/secrets/{secretName}",
                cancellationToken);

            response.EnsureSuccessStatusCode();
            _logger.LogInformation($"Successfully deleted secret '{secretName}'");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to delete secret '{secretName}': {ex.Message}", ex);
            throw;
        }
    }

    // JSON models
    private class GiteaUser
    {
        [JsonPropertyName("login")]
        public string Login { get; set; } = "";

        [JsonPropertyName("id")]
        public long Id { get; set; }
    }

    private class GiteaRepo
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";
    }

    private class GiteaSecretsResponse
    {
        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }

        [JsonPropertyName("secrets")]
        public List<GiteaSecret>? Secrets { get; set; }
    }

    private class GiteaSecret
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    private class GiteaCreateSecretRequest
    {
        [JsonPropertyName("data")]
        public string Data { get; set; } = "";
    }
}
