using MauiSherpa.Core.Interfaces;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Cloud secrets provider implementation for Azure DevOps Variable Groups.
/// Stores secrets as variables within a dedicated variable group in an Azure DevOps project.
/// Binary data is base64-encoded for storage.
/// </summary>
public class AzureDevOpsProvider : ICloudSecretsProvider
{
    private readonly CloudSecretsProviderConfig _config;
    private readonly ILoggingService _logger;
    private VssConnection? _connection;

    public AzureDevOpsProvider(CloudSecretsProviderConfig config, ILoggingService logger)
    {
        _config = config;
        _logger = logger;
    }

    public CloudSecretsProviderType ProviderType => CloudSecretsProviderType.AzureDevOps;
    public string DisplayName => "Azure DevOps";

    #region Configuration Helpers

    private string OrganizationUrl => _config.Settings.GetValueOrDefault("OrganizationUrl", "").TrimEnd('/');
    private string PersonalAccessToken => _config.Settings.GetValueOrDefault("PersonalAccessToken", "");
    private string Project => _config.Settings.GetValueOrDefault("Project", "");
    private string VariableGroupName => _config.Settings.GetValueOrDefault("VariableGroupName", "MauiSherpa-Secrets");

    #endregion

    #region Connection

    private VssConnection GetConnection()
    {
        if (_connection != null)
            return _connection;

        var credentials = new VssBasicCredential(string.Empty, PersonalAccessToken);
        _connection = new VssConnection(new Uri(OrganizationUrl), credentials);
        return _connection;
    }

    private async Task<TaskAgentHttpClient> GetTaskClientAsync(CancellationToken cancellationToken = default)
    {
        var connection = GetConnection();
        return await connection.GetClientAsync<TaskAgentHttpClient>(cancellationToken);
    }

    #endregion

    #region Variable Group Helpers

    private async Task<VariableGroup?> GetVariableGroupAsync(TaskAgentHttpClient client, CancellationToken cancellationToken)
    {
        var groups = await client.GetVariableGroupsAsync(Project, VariableGroupName, cancellationToken: cancellationToken);
        return groups.FirstOrDefault();
    }

    private async Task<VariableGroup> GetOrCreateVariableGroupAsync(TaskAgentHttpClient client, CancellationToken cancellationToken)
    {
        var group = await GetVariableGroupAsync(client, cancellationToken);
        if (group != null)
            return group;

        var newGroup = new VariableGroupParameters
        {
            Name = VariableGroupName,
            Description = "Secrets managed by MAUI Sherpa",
            Type = "Vsts",
            Variables = new Dictionary<string, VariableValue>()
        };

        return await client.AddVariableGroupAsync(newGroup, cancellationToken: cancellationToken);
    }

    private async Task UpdateVariableGroupAsync(TaskAgentHttpClient client, VariableGroup group, CancellationToken cancellationToken)
    {
        var updateParams = new VariableGroupParameters
        {
            Name = group.Name,
            Description = group.Description,
            Type = group.Type,
            Variables = group.Variables
        };

        await client.UpdateVariableGroupAsync(group.Id, updateParams, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sanitize key for use as an Azure DevOps variable name.
    /// Variable names allow alphanumeric, dots, and underscores.
    /// </summary>
    private static string SanitizeKey(string key)
    {
        var chars = key.Select(c => char.IsLetterOrDigit(c) || c == '.' ? c : '_').ToArray();
        var result = new string(chars);

        // Must start with a letter or underscore
        if (result.Length > 0 && !char.IsLetter(result[0]) && result[0] != '_')
            result = "_" + result;

        return result;
    }

    #endregion

    #region ICloudSecretsProvider Implementation

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = GetConnection();
            await connection.ConnectAsync(cancellationToken);

            // Verify we can access the project's variable groups
            var client = await GetTaskClientAsync(cancellationToken);
            await client.GetVariableGroupsAsync(Project, cancellationToken: cancellationToken);

            _logger.LogInformation($"Azure DevOps connection test successful for {OrganizationUrl}/{Project}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Azure DevOps connection test failed: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetTaskClientAsync(cancellationToken);
            var group = await GetOrCreateVariableGroupAsync(client, cancellationToken);
            var sanitizedKey = SanitizeKey(key);

            // Base64 encode binary data for storage
            var base64Value = Convert.ToBase64String(value);

            group.Variables[sanitizedKey] = new VariableValue { Value = base64Value, IsSecret = true };

            // Store metadata as companion variables if provided
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    var metaKey = $"{sanitizedKey}__meta__{SanitizeKey(kvp.Key)}";
                    group.Variables[metaKey] = new VariableValue { Value = kvp.Value, IsSecret = false };
                }
            }

            await UpdateVariableGroupAsync(client, group, cancellationToken);

            _logger.LogInformation($"Stored secret in Azure DevOps: {key}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Azure DevOps store secret failed: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetTaskClientAsync(cancellationToken);
            var group = await GetVariableGroupAsync(client, cancellationToken);

            if (group == null)
                return null;

            var sanitizedKey = SanitizeKey(key);

            if (!group.Variables.TryGetValue(sanitizedKey, out var variable) || variable.Value == null)
                return null;

            return Convert.FromBase64String(variable.Value);
        }
        catch (FormatException ex)
        {
            _logger.LogError($"Azure DevOps secret not base64 encoded: {key} - {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Azure DevOps get secret error: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetTaskClientAsync(cancellationToken);
            var group = await GetVariableGroupAsync(client, cancellationToken);

            if (group == null)
            {
                _logger.LogInformation($"Variable group not found, nothing to delete: {key}");
                return true;
            }

            var sanitizedKey = SanitizeKey(key);
            var removed = group.Variables.Remove(sanitizedKey);

            // Also remove any companion metadata variables
            var metaPrefix = $"{sanitizedKey}__meta__";
            var metaKeys = group.Variables.Keys.Where(k => k.StartsWith(metaPrefix)).ToList();
            foreach (var metaKey in metaKeys)
            {
                group.Variables.Remove(metaKey);
            }

            if (removed || metaKeys.Count > 0)
            {
                await UpdateVariableGroupAsync(client, group, cancellationToken);
            }

            _logger.LogInformation($"Deleted secret from Azure DevOps: {key}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Azure DevOps delete secret failed: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetTaskClientAsync(cancellationToken);
            var group = await GetVariableGroupAsync(client, cancellationToken);

            if (group == null)
                return false;

            var sanitizedKey = SanitizeKey(key);
            return group.Variables.ContainsKey(sanitizedKey);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Azure DevOps secret exists check error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListSecretsAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetTaskClientAsync(cancellationToken);
            var group = await GetVariableGroupAsync(client, cancellationToken);

            if (group == null)
                return Array.Empty<string>();

            var sanitizedPrefix = string.IsNullOrEmpty(prefix) ? null : SanitizeKey(prefix);

            var secrets = group.Variables.Keys
                .Where(k => !k.Contains("__meta__")) // Exclude metadata companion variables
                .Where(k => sanitizedPrefix == null || k.StartsWith(sanitizedPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .AsReadOnly();

            return secrets;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Azure DevOps list secrets error: {ex.Message}", ex);
            return Array.Empty<string>();
        }
    }

    #endregion
}
