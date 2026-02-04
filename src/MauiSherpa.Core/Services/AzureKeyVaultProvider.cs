using System.Text;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Cloud secrets provider implementation for Azure Key Vault
/// Uses the official Azure.Security.KeyVault.Secrets SDK
/// </summary>
public class AzureKeyVaultProvider : ICloudSecretsProvider
{
    private readonly CloudSecretsProviderConfig _config;
    private readonly ILoggingService _logger;
    private SecretClient? _client;

    public AzureKeyVaultProvider(CloudSecretsProviderConfig config, ILoggingService logger)
    {
        _config = config;
        _logger = logger;
    }

    public CloudSecretsProviderType ProviderType => CloudSecretsProviderType.AzureKeyVault;
    public string DisplayName => "Azure Key Vault";

    #region Configuration Helpers

    private string VaultUrl => _config.Settings.GetValueOrDefault("VaultUrl", "").TrimEnd('/');
    private string TenantId => _config.Settings.GetValueOrDefault("TenantId", "");
    private string ClientId => _config.Settings.GetValueOrDefault("ClientId", "");
    private string ClientSecretValue => _config.Settings.GetValueOrDefault("ClientSecret", "");

    #endregion

    #region Client Initialization

    private SecretClient GetClient()
    {
        if (_client != null)
            return _client;

        var credential = new ClientSecretCredential(TenantId, ClientId, ClientSecretValue);
        _client = new SecretClient(new Uri(VaultUrl), credential);
        return _client;
    }

    #endregion

    #region ICloudSecretsProvider Implementation

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            
            // Try to list secrets (limited) to verify access
            await foreach (var _ in client.GetPropertiesOfSecretsAsync(cancellationToken).AsPages(pageSizeHint: 1))
            {
                break; // Just need to verify we can access
            }

            _logger.LogInformation($"Azure Key Vault connection test successful for {VaultUrl}");
            return true;
        }
        catch (AuthenticationFailedException ex)
        {
            _logger.LogError($"Azure Key Vault authentication failed: {ex.Message}", ex);
            return false;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError($"Azure Key Vault connection test failed: {ex.Status} - {ex.Message}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Azure Key Vault connection test error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var sanitizedKey = SanitizeKey(key);
            
            // Azure Key Vault stores strings, so we base64 encode the binary data
            var base64Value = Convert.ToBase64String(value);
            
            var secret = new KeyVaultSecret(sanitizedKey, base64Value);
            
            // Add metadata as tags
            if (metadata != null)
            {
                foreach (var kvp in metadata)
                {
                    secret.Properties.Tags[kvp.Key] = kvp.Value;
                }
            }

            await client.SetSecretAsync(secret, cancellationToken);
            
            _logger.LogInformation($"Stored secret: {key}");
            return true;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError($"Azure Key Vault store secret failed: {ex.Status} - {ex.Message}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Azure Key Vault store secret error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var sanitizedKey = SanitizeKey(key);
            
            var response = await client.GetSecretAsync(sanitizedKey, cancellationToken: cancellationToken);
            
            if (response?.Value?.Value == null)
                return null;

            // Decode from base64
            return Convert.FromBase64String(response.Value.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (FormatException ex)
        {
            _logger.LogError($"Azure Key Vault secret not base64 encoded: {key} - {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Azure Key Vault get secret error: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var sanitizedKey = SanitizeKey(key);
            
            var operation = await client.StartDeleteSecretAsync(sanitizedKey, cancellationToken);
            
            // Wait for deletion to complete
            await operation.WaitForCompletionAsync(cancellationToken);
            
            _logger.LogInformation($"Deleted secret: {key}");
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation($"Secret already deleted or not found: {key}");
            return true;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError($"Azure Key Vault delete secret failed: {ex.Status} - {ex.Message}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Azure Key Vault delete secret error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var sanitizedKey = SanitizeKey(key);
            
            await client.GetSecretAsync(sanitizedKey, cancellationToken: cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Azure Key Vault secret exists check error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListSecretsAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var allSecrets = new List<string>();

            await foreach (var secretProperties in client.GetPropertiesOfSecretsAsync(cancellationToken))
            {
                var secretName = secretProperties.Name;
                
                // Convert back from sanitized format (hyphens back to underscores for prefix matching)
                var originalKey = secretName.Replace("-", "_").ToUpperInvariant();
                
                if (string.IsNullOrEmpty(prefix) || originalKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    allSecrets.Add(originalKey);
                }
            }

            return allSecrets.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Azure Key Vault list secrets error: {ex.Message}", ex);
            return Array.Empty<string>();
        }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Sanitize key for Azure Key Vault (must be alphanumeric with hyphens, 1-127 chars)
    /// </summary>
    private static string SanitizeKey(string key)
    {
        var sanitized = new StringBuilder();
        foreach (var c in key)
        {
            if (char.IsLetterOrDigit(c))
                sanitized.Append(c);
            else
                sanitized.Append('-');
        }
        
        // Azure Key Vault secret names must start with a letter
        var result = sanitized.ToString();
        if (result.Length > 0 && !char.IsLetter(result[0]))
        {
            result = "S" + result;
        }
        
        // Limit to 127 characters
        if (result.Length > 127)
        {
            result = result[..127];
        }
        
        return result;
    }

    #endregion
}
