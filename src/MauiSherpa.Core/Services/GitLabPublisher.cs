using MauiSherpa.Core.Interfaces;
using NGitLab;
using NGitLab.Models;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Secrets publisher for GitLab CI/CD using NGitLab
/// </summary>
public class GitLabPublisher : ISecretsPublisher
{
    private readonly IGitLabClient _client;
    private readonly ILoggingService _logger;

    public string ProviderId => "gitlab";
    public string DisplayName => "GitLab CI/CD";
    public string IconClass => "fa-brands fa-gitlab";

    public GitLabPublisher(SecretsPublisherConfig config, ILoggingService logger)
    {
        _logger = logger;
        var token = config.Settings.GetValueOrDefault("PersonalAccessToken") 
            ?? throw new ArgumentException("PersonalAccessToken is required");
        var serverUrl = config.Settings.GetValueOrDefault("ServerUrl") ?? "https://gitlab.com";

        _client = new GitLabClient(serverUrl, token);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await Task.Run(() => _client.Users.Current, cancellationToken);
            _logger.LogInformation($"GitLab connection successful, authenticated as: {user.Username}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"GitLab connection test failed: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<IReadOnlyList<PublisherRepository>> ListRepositoriesAsync(string? filter = null, CancellationToken cancellationToken = default)
    {
        var repos = new List<PublisherRepository>();

        try
        {
            var projects = await Task.Run(() => 
                _client.Projects.Accessible.Take(500).ToList(), cancellationToken);

            foreach (var project in projects)
            {
                // Apply filter if provided
                if (!string.IsNullOrEmpty(filter) && 
                    !project.PathWithNamespace.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !(project.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    continue;
                }

                repos.Add(new PublisherRepository(
                    project.Id.ToString(),
                    project.Name,
                    project.PathWithNamespace,
                    project.Description,
                    project.WebUrl
                ));
            }

            _logger.LogInformation($"Found {repos.Count} GitLab projects");
            return repos;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to list GitLab projects: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> ListSecretsAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = int.Parse(repositoryId);
            var variables = await Task.Run(() => 
                _client.GetProjectVariableClient(projectId).All.ToList(), cancellationToken);
            
            return variables.Select(v => v.Key).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to list variables for project {repositoryId}: {ex.Message}", ex);
            throw;
        }
    }

    public async Task PublishSecretAsync(string repositoryId, string secretName, string secretValue, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation($"Publishing variable '{secretName}' to project {repositoryId}");

            var projectId = int.Parse(repositoryId);
            var variableClient = _client.GetProjectVariableClient(projectId);

            // Try to update existing, create if not found
            try
            {
                var existing = await Task.Run(() => variableClient[secretName], cancellationToken);
                if (existing != null)
                {
                    await Task.Run(() => variableClient.Update(secretName, new VariableUpdate
                    {
                        Value = secretValue,
                        Protected = false,
                        Masked = true
                    }), cancellationToken);
                    _logger.LogInformation($"Successfully updated variable '{secretName}'");
                    return;
                }
            }
            catch
            {
                // Variable doesn't exist, will create it
            }

            // Create new variable
            await Task.Run(() => variableClient.Create(new VariableCreate
            {
                Key = secretName,
                Value = secretValue,
                Protected = false,
                Masked = true
            }), cancellationToken);

            _logger.LogInformation($"Successfully created variable '{secretName}'");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to publish variable '{secretName}': {ex.Message}", ex);
            throw;
        }
    }

    public async Task PublishSecretsAsync(string repositoryId, IReadOnlyDictionary<string, string> secrets, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Publishing {secrets.Count} variables to project {repositoryId}");

        var count = 0;
        foreach (var (name, value) in secrets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Publishing {name} ({++count}/{secrets.Count})...");
            await PublishSecretAsync(repositoryId, name, value, cancellationToken);
        }

        _logger.LogInformation($"Successfully published {secrets.Count} variables");
    }

    public async Task DeleteSecretAsync(string repositoryId, string secretName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation($"Deleting variable '{secretName}' from project {repositoryId}");
            
            var projectId = int.Parse(repositoryId);
            await Task.Run(() => 
                _client.GetProjectVariableClient(projectId).Delete(secretName), cancellationToken);
            
            _logger.LogInformation($"Successfully deleted variable '{secretName}'");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to delete variable '{secretName}': {ex.Message}", ex);
            throw;
        }
    }
}
