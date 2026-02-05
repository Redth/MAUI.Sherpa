using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Factory for creating secrets publisher instances
/// </summary>
public class SecretsPublisherFactory : ISecretsPublisherFactory
{
    private readonly ILoggingService _logger;

    public SecretsPublisherFactory(ILoggingService logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<(string ProviderId, string DisplayName, string IconClass)> GetAvailableProviders()
    {
        return new List<(string, string, string)>
        {
            ("github", "GitHub Actions", "fa-brands fa-github"),
            ("gitea", "Gitea", "fa-solid fa-code-branch"),
            ("gitlab", "GitLab CI/CD", "fa-brands fa-gitlab"),
            ("azuredevops", "Azure DevOps", "fa-brands fa-microsoft")
        };
    }

    public ISecretsPublisher CreatePublisher(SecretsPublisherConfig config)
    {
        return config.ProviderId switch
        {
            "github" => new GitHubActionsPublisher(config, _logger),
            "gitea" => new GiteaPublisher(config, _logger),
            "gitlab" => new GitLabPublisher(config, _logger),
            "azuredevops" => new AzureDevOpsPublisher(config, _logger),
            _ => throw new NotSupportedException($"Provider '{config.ProviderId}' is not supported")
        };
    }

    public (bool IsValid, string? ErrorMessage) ValidateConfig(string providerId, Dictionary<string, string> settings)
    {
        return providerId switch
        {
            "github" => ValidateGitHubConfig(settings),
            "gitea" => ValidateGiteaConfig(settings),
            "gitlab" => ValidateGitLabConfig(settings),
            "azuredevops" => ValidateAzureDevOpsConfig(settings),
            _ => (false, $"Unknown provider: {providerId}")
        };
    }

    public IReadOnlyList<(string Key, string Label, string Type, bool Required, string? Placeholder)> GetRequiredSettings(string providerId)
    {
        return providerId switch
        {
            "github" => new List<(string, string, string, bool, string?)>
            {
                ("PersonalAccessToken", "Personal Access Token", "password", true, "ghp_xxxxxxxxxxxx"),
                ("Owner", "Owner Filter (optional)", "text", false, "username or organization")
            },
            "gitea" => new List<(string, string, string, bool, string?)>
            {
                ("ServerUrl", "Server URL", "url", true, "https://gitea.example.com"),
                ("AccessToken", "Access Token", "password", true, null)
            },
            "gitlab" => new List<(string, string, string, bool, string?)>
            {
                ("ServerUrl", "Server URL", "url", true, "https://gitlab.com"),
                ("PersonalAccessToken", "Personal Access Token", "password", true, "glpat-xxxxxxxxxxxx")
            },
            "azuredevops" => new List<(string, string, string, bool, string?)>
            {
                ("OrganizationUrl", "Organization URL", "url", true, "https://dev.azure.com/myorg"),
                ("PersonalAccessToken", "Personal Access Token", "password", true, null),
                ("Project", "Project (optional)", "text", false, "MyProject")
            },
            _ => new List<(string, string, string, bool, string?)>()
        };
    }

    private static (bool, string?) ValidateGitHubConfig(Dictionary<string, string> settings)
    {
        if (!settings.TryGetValue("PersonalAccessToken", out var token) || string.IsNullOrWhiteSpace(token))
            return (false, "Personal Access Token is required");
        
        return (true, null);
    }

    private static (bool, string?) ValidateGiteaConfig(Dictionary<string, string> settings)
    {
        if (!settings.TryGetValue("ServerUrl", out var url) || string.IsNullOrWhiteSpace(url))
            return (false, "Server URL is required");
        
        if (!settings.TryGetValue("AccessToken", out var token) || string.IsNullOrWhiteSpace(token))
            return (false, "Access Token is required");
        
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return (false, "Invalid Server URL");
        
        return (true, null);
    }

    private static (bool, string?) ValidateGitLabConfig(Dictionary<string, string> settings)
    {
        if (!settings.TryGetValue("ServerUrl", out var url) || string.IsNullOrWhiteSpace(url))
            return (false, "Server URL is required");
        
        if (!settings.TryGetValue("PersonalAccessToken", out var token) || string.IsNullOrWhiteSpace(token))
            return (false, "Personal Access Token is required");
        
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return (false, "Invalid Server URL");
        
        return (true, null);
    }

    private static (bool, string?) ValidateAzureDevOpsConfig(Dictionary<string, string> settings)
    {
        if (!settings.TryGetValue("OrganizationUrl", out var url) || string.IsNullOrWhiteSpace(url))
            return (false, "Organization URL is required");
        
        if (!settings.TryGetValue("PersonalAccessToken", out var token) || string.IsNullOrWhiteSpace(token))
            return (false, "Personal Access Token is required");
        
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return (false, "Invalid Organization URL");
        
        return (true, null);
    }
}

/// <summary>
/// Service for managing secrets publisher configurations
/// </summary>
public class SecretsPublisherService : ISecretsPublisherService
{
    private const string PublishersKey = "secrets_publishers";
    private readonly ISecureStorageService _secureStorage;
    private readonly ISecretsPublisherFactory _factory;
    private readonly ILoggingService _logger;
    private readonly Dictionary<string, ISecretsPublisher> _publisherInstances = new();
    private List<SecretsPublisherConfig>? _cachedPublishers;

    public event Action? OnPublishersChanged;

    public SecretsPublisherService(
        ISecureStorageService secureStorage,
        ISecretsPublisherFactory factory,
        ILoggingService logger)
    {
        _secureStorage = secureStorage;
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SecretsPublisherConfig>> GetPublishersAsync()
    {
        if (_cachedPublishers != null)
            return _cachedPublishers;

        var json = await _secureStorage.GetAsync(PublishersKey);
        if (string.IsNullOrEmpty(json))
        {
            _cachedPublishers = new List<SecretsPublisherConfig>();
            return _cachedPublishers;
        }

        try
        {
            _cachedPublishers = JsonSerializer.Deserialize<List<SecretsPublisherConfig>>(json) ?? new();
        }
        catch
        {
            _cachedPublishers = new List<SecretsPublisherConfig>();
        }

        return _cachedPublishers;
    }

    public async Task<SecretsPublisherConfig?> GetPublisherAsync(string id)
    {
        var publishers = await GetPublishersAsync();
        return publishers.FirstOrDefault(p => p.Id == id);
    }

    public async Task SavePublisherAsync(SecretsPublisherConfig config)
    {
        var publishers = (await GetPublishersAsync()).ToList();
        
        var existingIndex = publishers.FindIndex(p => p.Id == config.Id);
        if (existingIndex >= 0)
        {
            publishers[existingIndex] = config;
        }
        else
        {
            publishers.Add(config);
        }

        await SavePublishersListAsync(publishers);
        
        // Clear cached instance
        _publisherInstances.Remove(config.Id);
        
        _logger.LogInformation($"Saved publisher configuration: {config.Name}");
        OnPublishersChanged?.Invoke();
    }

    public async Task DeletePublisherAsync(string id)
    {
        var publishers = (await GetPublishersAsync()).ToList();
        var removed = publishers.RemoveAll(p => p.Id == id);
        
        if (removed > 0)
        {
            await SavePublishersListAsync(publishers);
            _publisherInstances.Remove(id);
            
            _logger.LogInformation($"Deleted publisher configuration: {id}");
            OnPublishersChanged?.Invoke();
        }
    }

    public async Task<bool> TestConnectionAsync(string publisherId, CancellationToken cancellationToken = default)
    {
        var publisher = GetPublisherInstance(publisherId);
        if (publisher == null)
            return false;

        return await publisher.TestConnectionAsync(cancellationToken);
    }

    public ISecretsPublisher? GetPublisherInstance(string publisherId)
    {
        if (_publisherInstances.TryGetValue(publisherId, out var cached))
            return cached;

        var config = _cachedPublishers?.FirstOrDefault(p => p.Id == publisherId);
        if (config == null)
            return null;

        try
        {
            var publisher = _factory.CreatePublisher(config);
            _publisherInstances[publisherId] = publisher;
            return publisher;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to create publisher instance: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<IReadOnlyList<PublisherRepository>> ListRepositoriesAsync(string publisherId, string? filter = null, CancellationToken cancellationToken = default)
    {
        var publisher = GetPublisherInstance(publisherId);
        if (publisher == null)
            return new List<PublisherRepository>();

        return await publisher.ListRepositoriesAsync(filter, cancellationToken);
    }

    public async Task PublishSecretsAsync(string publisherId, string repositoryId, IReadOnlyDictionary<string, string> secrets, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var publisher = GetPublisherInstance(publisherId);
        if (publisher == null)
            throw new InvalidOperationException($"Publisher not found: {publisherId}");

        await publisher.PublishSecretsAsync(repositoryId, secrets, progress, cancellationToken);
    }

    private async Task SavePublishersListAsync(List<SecretsPublisherConfig> publishers)
    {
        _cachedPublishers = publishers;
        var json = JsonSerializer.Serialize(publishers);
        await _secureStorage.SetAsync(PublishersKey, json);
    }
}
