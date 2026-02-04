using System.Text;
using Infisical.Sdk;
using Infisical.Sdk.Model;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Cloud secrets provider implementation for Infisical
/// Uses the official Infisical.Sdk SDK
/// </summary>
public class InfisicalProvider : ICloudSecretsProvider
{
    private readonly CloudSecretsProviderConfig _config;
    private readonly ILoggingService _logger;
    private InfisicalClient? _client;
    private bool _isAuthenticated;

    public InfisicalProvider(CloudSecretsProviderConfig config, ILoggingService logger)
    {
        _config = config;
        _logger = logger;
    }

    public CloudSecretsProviderType ProviderType => CloudSecretsProviderType.Infisical;
    public string DisplayName => "Infisical";

    #region Configuration Helpers

    private string SiteUrl => _config.Settings.GetValueOrDefault("SiteUrl", "https://app.infisical.com").TrimEnd('/');
    private string ClientId => _config.Settings.GetValueOrDefault("ClientId", "");
    private string ClientSecretValue => _config.Settings.GetValueOrDefault("ClientSecret", "");
    private string ProjectId => _config.Settings.GetValueOrDefault("ProjectId", "");
    private string Environment => _config.Settings.GetValueOrDefault("Environment", "prod");
    private string SecretPath => _config.Settings.GetValueOrDefault("SecretPath", "/maui-sherpa");

    #endregion

    #region Client Initialization

    private async Task<InfisicalClient?> GetClientAsync(CancellationToken cancellationToken = default)
    {
        if (_client != null && _isAuthenticated)
            return _client;

        try
        {
            var settings = new InfisicalSdkSettingsBuilder()
                .WithHostUri(SiteUrl)
                .Build();

            _client = new InfisicalClient(settings);
            
            await _client.Auth().UniversalAuth().LoginAsync(ClientId, ClientSecretValue);
            _isAuthenticated = true;
            
            return _client;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Infisical authentication failed: {ex.Message}", ex);
            _isAuthenticated = false;
            return null;
        }
    }

    #endregion

    #region ICloudSecretsProvider Implementation

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetClientAsync(cancellationToken);
            if (client == null)
                return false;
            
            // Try to list secrets to verify access
            var options = new ListSecretsOptions
            {
                EnvironmentSlug = Environment,
                SecretPath = SecretPath,
                ProjectId = ProjectId,
            };
            
            await client.Secrets().ListAsync(options);
            
            _logger.LogInformation($"Infisical connection test successful for project {ProjectId}");
            return true;
        }
        catch (InfisicalException ex)
        {
            _logger.LogError($"Infisical connection test failed: {ex.Message}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Infisical connection test error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetClientAsync(cancellationToken);
            if (client == null)
                return false;

            var secretName = SanitizeSecretName(key);
            // Store binary data as base64 encoded string
            var base64Value = Convert.ToBase64String(value);
            
            // Check if secret exists to decide create vs update
            var exists = await SecretExistsInternalAsync(client, secretName, cancellationToken);
            
            if (exists)
            {
                var updateOptions = new UpdateSecretOptions
                {
                    SecretName = secretName,
                    EnvironmentSlug = Environment,
                    SecretPath = SecretPath,
                    ProjectId = ProjectId,
                    NewSecretValue = base64Value
                };
                
                await client.Secrets().UpdateAsync(updateOptions);
            }
            else
            {
                var createOptions = new CreateSecretOptions
                {
                    SecretName = secretName,
                    SecretValue = base64Value,
                    EnvironmentSlug = Environment,
                    SecretPath = SecretPath,
                    ProjectId = ProjectId,
                };

                await client.Secrets().CreateAsync(createOptions);
            }

            _logger.LogInformation($"Stored secret: {key}");
            return true;
        }
        catch (InfisicalException ex)
        {
            _logger.LogError($"Infisical store secret failed: {ex.Message}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Infisical store secret error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetClientAsync(cancellationToken);
            if (client == null)
                return null;

            var secretName = SanitizeSecretName(key);
            
            var options = new GetSecretOptions
            {
                SecretName = secretName,
                EnvironmentSlug = Environment,
                SecretPath = SecretPath,
                ProjectId = ProjectId,
            };
            
            var secret = await client.Secrets().GetAsync(options);
            
            if (secret?.SecretValue == null)
                return null;

            // Decode from base64
            return Convert.FromBase64String(secret.SecretValue);
        }
        catch (InfisicalException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        catch (FormatException ex)
        {
            _logger.LogError($"Infisical secret not base64 encoded: {key} - {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Infisical get secret error: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetClientAsync(cancellationToken);
            if (client == null)
                return false;

            var secretName = SanitizeSecretName(key);
            
            var options = new DeleteSecretOptions
            {
                SecretName = secretName,
                EnvironmentSlug = Environment,
                SecretPath = SecretPath,
                ProjectId = ProjectId,
            };
            
            await client.Secrets().DeleteAsync(options);
            
            _logger.LogInformation($"Deleted secret: {key}");
            return true;
        }
        catch (InfisicalException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation($"Secret already deleted or not found: {key}");
            return true;
        }
        catch (InfisicalException ex)
        {
            _logger.LogError($"Infisical delete secret failed: {ex.Message}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Infisical delete secret error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetClientAsync(cancellationToken);
            if (client == null)
                return false;

            var secretName = SanitizeSecretName(key);
            return await SecretExistsInternalAsync(client, secretName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Infisical secret exists check error: {ex.Message}", ex);
            return false;
        }
    }

    private async Task<bool> SecretExistsInternalAsync(InfisicalClient client, string secretName, CancellationToken cancellationToken)
    {
        try
        {
            var options = new GetSecretOptions
            {
                SecretName = secretName,
                EnvironmentSlug = Environment,
                SecretPath = SecretPath,
                ProjectId = ProjectId,
            };
            
            await client.Secrets().GetAsync(options);
            return true;
        }
        catch (InfisicalException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListSecretsAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetClientAsync(cancellationToken);
            if (client == null)
                return Array.Empty<string>();

            var options = new ListSecretsOptions
            {
                EnvironmentSlug = Environment,
                SecretPath = SecretPath,
                ProjectId = ProjectId,
            };
            
            var secrets = await client.Secrets().ListAsync(options);
            
            if (secrets == null)
                return Array.Empty<string>();

            var result = new List<string>();
            foreach (var secret in secrets)
            {
                var secretKey = secret.SecretKey;
                
                // Filter by prefix if specified
                if (!string.IsNullOrEmpty(prefix) && !secretKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                
                result.Add(secretKey);
            }

            return result.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Infisical list secrets error: {ex.Message}", ex);
            return Array.Empty<string>();
        }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Sanitize secret name for Infisical
    /// Secret names must be uppercase and can only contain letters, numbers, and underscores
    /// </summary>
    private static string SanitizeSecretName(string name)
    {
        var sanitized = new StringBuilder();
        foreach (var c in name.ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sanitized.Append(c);
            else
                sanitized.Append('_');
        }
        
        var result = sanitized.ToString();
        
        // Must start with a letter
        if (result.Length > 0 && !char.IsLetter(result[0]))
            result = "S" + result;
        
        return result;
    }

    #endregion
}
