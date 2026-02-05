using MauiSherpa.Core.Interfaces;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Secrets publisher for Azure DevOps Pipelines using official Microsoft SDK
/// </summary>
public class AzureDevOpsPublisher : ISecretsPublisher
{
    private readonly VssConnection _connection;
    private readonly string _organizationUrl;
    private readonly string? _projectFilter;
    private readonly ILoggingService _logger;

    public string ProviderId => "azuredevops";
    public string DisplayName => "Azure DevOps";
    public string IconClass => "fa-brands fa-microsoft";

    public AzureDevOpsPublisher(SecretsPublisherConfig config, ILoggingService logger)
    {
        _logger = logger;
        var token = config.Settings.GetValueOrDefault("PersonalAccessToken") 
            ?? throw new ArgumentException("PersonalAccessToken is required");
        _organizationUrl = config.Settings.GetValueOrDefault("OrganizationUrl") 
            ?? throw new ArgumentException("OrganizationUrl is required");
        _projectFilter = config.Settings.GetValueOrDefault("Project");

        var credentials = new VssBasicCredential(string.Empty, token);
        _connection = new VssConnection(new Uri(_organizationUrl), credentials);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _connection.ConnectAsync(cancellationToken);
            _logger.LogInformation($"Azure DevOps connection successful to {_organizationUrl}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Azure DevOps connection test failed: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<IReadOnlyList<PublisherRepository>> ListRepositoriesAsync(string? filter = null, CancellationToken cancellationToken = default)
    {
        var repos = new List<PublisherRepository>();

        try
        {
            var projectClient = await _connection.GetClientAsync<ProjectHttpClient>(cancellationToken);
            var projects = await projectClient.GetProjects();

            foreach (var project in projects)
            {
                // Filter by project name if specified
                if (!string.IsNullOrEmpty(_projectFilter) && 
                    !project.Name.Equals(_projectFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Apply search filter if provided
                if (!string.IsNullOrEmpty(filter) && 
                    !project.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !(project.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    continue;
                }

                var projectUrl = $"{_organizationUrl}/{project.Name}";
                repos.Add(new PublisherRepository(
                    project.Id.ToString(),
                    project.Name,
                    project.Name,
                    project.Description,
                    projectUrl
                ));
            }

            _logger.LogInformation($"Found {repos.Count} Azure DevOps projects");
            return repos;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to list Azure DevOps projects: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> ListSecretsAsync(string repositoryId, CancellationToken cancellationToken = default)
    {
        try
        {
            var projectId = Guid.Parse(repositoryId);
            var taskClient = await _connection.GetClientAsync<TaskAgentHttpClient>(cancellationToken);
            
            var variableGroups = await taskClient.GetVariableGroupsAsync(projectId.ToString());
            var secretNames = new List<string>();

            foreach (var group in variableGroups)
            {
                foreach (var variable in group.Variables)
                {
                    secretNames.Add($"{group.Name}/{variable.Key}");
                }
            }

            return secretNames;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to list secrets for project {repositoryId}: {ex.Message}", ex);
            throw;
        }
    }

    public async Task PublishSecretAsync(string repositoryId, string secretName, string secretValue, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation($"Publishing variable '{secretName}' to project {repositoryId}");

            var projectId = Guid.Parse(repositoryId);
            var taskClient = await _connection.GetClientAsync<TaskAgentHttpClient>(cancellationToken);

            // Parse group/variable format or use default group
            var parts = secretName.Split('/');
            var groupName = parts.Length > 1 ? parts[0] : "MauiSherpa-Secrets";
            var variableName = parts.Length > 1 ? parts[1] : secretName;

            // Find or create variable group
            var groups = await taskClient.GetVariableGroupsAsync(projectId.ToString(), groupName);
            VariableGroup? group = groups.FirstOrDefault();

            if (group == null)
            {
                // Create new variable group
                var newGroup = new VariableGroupParameters
                {
                    Name = groupName,
                    Description = "Secrets managed by MauiSherpa",
                    Type = "Vsts",
                    Variables = new Dictionary<string, VariableValue>
                    {
                        [variableName] = new VariableValue { Value = secretValue, IsSecret = true }
                    }
                };

                await taskClient.AddVariableGroupAsync(newGroup);
            }
            else
            {
                // Update existing group
                group.Variables[variableName] = new VariableValue { Value = secretValue, IsSecret = true };
                
                var updateParams = new VariableGroupParameters
                {
                    Name = group.Name,
                    Description = group.Description,
                    Type = group.Type,
                    Variables = group.Variables
                };

                await taskClient.UpdateVariableGroupAsync(group.Id, updateParams);
            }

            _logger.LogInformation($"Successfully published variable '{secretName}'");
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

        var projectId = Guid.Parse(repositoryId);
        var taskClient = await _connection.GetClientAsync<TaskAgentHttpClient>(cancellationToken);

        // Group secrets by variable group name
        var groupedSecrets = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (name, value) in secrets)
        {
            var parts = name.Split('/');
            var groupName = parts.Length > 1 ? parts[0] : "MauiSherpa-Secrets";
            var variableName = parts.Length > 1 ? parts[1] : name;

            if (!groupedSecrets.ContainsKey(groupName))
                groupedSecrets[groupName] = new Dictionary<string, string>();
            
            groupedSecrets[groupName][variableName] = value;
        }

        var count = 0;
        foreach (var (groupName, variables) in groupedSecrets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Publishing to group {groupName} ({++count}/{groupedSecrets.Count})...");

            var groups = await taskClient.GetVariableGroupsAsync(projectId.ToString(), groupName);
            VariableGroup? group = groups.FirstOrDefault();

            var variableValues = variables.ToDictionary(
                kvp => kvp.Key,
                kvp => new VariableValue { Value = kvp.Value, IsSecret = true }
            );

            if (group == null)
            {
                var newGroup = new VariableGroupParameters
                {
                    Name = groupName,
                    Description = "Secrets managed by MauiSherpa",
                    Type = "Vsts",
                    Variables = variableValues
                };

                await taskClient.AddVariableGroupAsync(newGroup);
            }
            else
            {
                foreach (var (key, val) in variableValues)
                {
                    group.Variables[key] = val;
                }

                var updateParams = new VariableGroupParameters
                {
                    Name = group.Name,
                    Description = group.Description,
                    Type = group.Type,
                    Variables = group.Variables
                };

                await taskClient.UpdateVariableGroupAsync(group.Id, updateParams);
            }
        }

        _logger.LogInformation($"Successfully published {secrets.Count} variables");
    }

    public async Task DeleteSecretAsync(string repositoryId, string secretName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation($"Deleting variable '{secretName}' from project {repositoryId}");
            
            var projectId = Guid.Parse(repositoryId);
            var taskClient = await _connection.GetClientAsync<TaskAgentHttpClient>(cancellationToken);

            var parts = secretName.Split('/');
            var groupName = parts.Length > 1 ? parts[0] : "MauiSherpa-Secrets";
            var variableName = parts.Length > 1 ? parts[1] : secretName;

            var groups = await taskClient.GetVariableGroupsAsync(projectId.ToString(), groupName);
            var group = groups.FirstOrDefault();

            if (group != null && group.Variables.ContainsKey(variableName))
            {
                group.Variables.Remove(variableName);

                if (group.Variables.Count == 0)
                {
                    // Delete empty group
                    await taskClient.DeleteVariableGroupAsync(group.Id, new[] { projectId.ToString() });
                }
                else
                {
                    var updateParams = new VariableGroupParameters
                    {
                        Name = group.Name,
                        Description = group.Description,
                        Type = group.Type,
                        Variables = group.Variables
                    };

                    await taskClient.UpdateVariableGroupAsync(group.Id, updateParams);
                }
            }
            
            _logger.LogInformation($"Successfully deleted variable '{secretName}'");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to delete variable '{secretName}': {ex.Message}", ex);
            throw;
        }
    }
}
