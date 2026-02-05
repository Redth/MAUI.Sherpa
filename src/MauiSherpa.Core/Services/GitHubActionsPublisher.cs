using System.Text;
using MauiSherpa.Core.Interfaces;
using Octokit;
using Sodium;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Secrets publisher for GitHub Actions using Octokit
/// </summary>
public class GitHubActionsPublisher : ISecretsPublisher
{
    private readonly GitHubClient _client;
    private readonly string? _ownerFilter;
    private readonly ILoggingService _logger;

    public string ProviderId => "github";
    public string DisplayName => "GitHub Actions";
    public string IconClass => "fa-brands fa-github";

    public GitHubActionsPublisher(SecretsPublisherConfig config, ILoggingService logger)
    {
        _logger = logger;
        var token = config.Settings.GetValueOrDefault("PersonalAccessToken") 
            ?? throw new ArgumentException("PersonalAccessToken is required");
        _ownerFilter = config.Settings.GetValueOrDefault("Owner");

        _client = new GitHubClient(new ProductHeaderValue("MauiSherpa", "1.0"))
        {
            Credentials = new Credentials(token)
        };
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await _client.User.Current();
            _logger.LogInformation($"GitHub connection successful, authenticated as: {user.Login}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"GitHub connection test failed: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<IReadOnlyList<PublisherRepository>> ListRepositoriesAsync(string? filter = null, CancellationToken cancellationToken = default)
    {
        var repos = new List<PublisherRepository>();

        try
        {
            IReadOnlyList<Repository> ghRepos;
            
            if (string.IsNullOrEmpty(_ownerFilter))
            {
                // Get repos for authenticated user
                ghRepos = await _client.Repository.GetAllForCurrent(new RepositoryRequest
                {
                    Sort = RepositorySort.Updated,
                    Direction = SortDirection.Descending
                });
            }
            else
            {
                // Get repos for specific owner/org
                ghRepos = await _client.Repository.GetAllForUser(_ownerFilter);
            }

            foreach (var repo in ghRepos)
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
                
                // Limit to first 500 repos
                if (repos.Count >= 500)
                    break;
            }

            _logger.LogInformation($"Found {repos.Count} GitHub repositories");
            return repos;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to list GitHub repositories: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> ListSecretsAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        try
        {
            var (owner, repo) = ParseRepositoryId(repositoryId);
            var secrets = await _client.Repository.Actions.Secrets.GetAll(owner, repo);
            return secrets.Secrets.Select(s => s.Name).ToList();
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

            var (owner, repo) = ParseRepositoryId(repositoryId);

            // Get repository public key
            var publicKey = await _client.Repository.Actions.Secrets.GetPublicKey(owner, repo);

            // Encrypt the secret value
            var encryptedValue = EncryptSecret(secretValue, publicKey.Key);

            // Create or update the secret
            var upsertSecret = new UpsertRepositorySecret
            {
                EncryptedValue = encryptedValue,
                KeyId = publicKey.KeyId
            };

            await _client.Repository.Actions.Secrets.CreateOrUpdate(owner, repo, secretName, upsertSecret);
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

        var (owner, repo) = ParseRepositoryId(repositoryId);

        // Get public key once for all secrets
        var publicKey = await _client.Repository.Actions.Secrets.GetPublicKey(owner, repo);

        var count = 0;
        foreach (var (name, value) in secrets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            progress?.Report($"Publishing {name} ({++count}/{secrets.Count})...");

            var encryptedValue = EncryptSecret(value, publicKey.Key);
            var upsertSecret = new UpsertRepositorySecret
            {
                EncryptedValue = encryptedValue,
                KeyId = publicKey.KeyId
            };

            await _client.Repository.Actions.Secrets.CreateOrUpdate(owner, repo, name, upsertSecret);
        }

        _logger.LogInformation($"Successfully published {secrets.Count} secrets");
    }

    public async Task DeleteSecretAsync(string repositoryId, string secretName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation($"Deleting secret '{secretName}' from {repositoryId}");
            
            var (owner, repo) = ParseRepositoryId(repositoryId);
            await _client.Repository.Actions.Secrets.Delete(owner, repo, secretName);
            
            _logger.LogInformation($"Successfully deleted secret '{secretName}'");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to delete secret '{secretName}': {ex.Message}", ex);
            throw;
        }
    }

    private static (string Owner, string Repo) ParseRepositoryId(string repositoryId)
    {
        // repositoryId is "owner/repo" format
        var parts = repositoryId.Split('/');
        if (parts.Length != 2)
            throw new ArgumentException($"Invalid repository ID format: {repositoryId}. Expected 'owner/repo'");
        return (parts[0], parts[1]);
    }

    private static string EncryptSecret(string secretValue, string publicKeyBase64)
    {
        var publicKey = Convert.FromBase64String(publicKeyBase64);
        var secretBytes = Encoding.UTF8.GetBytes(secretValue);
        
        // Use libsodium sealed box encryption
        var encrypted = SealedPublicKeyBox.Create(secretBytes, publicKey);
        
        return Convert.ToBase64String(encrypted);
    }
}

