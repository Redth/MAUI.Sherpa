using System.Text;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.SecretManager.V1;
using Google.Protobuf;
using MauiSherpa.Core.Interfaces;
using Google.Apis.Auth.OAuth2;
using GcpSecret = Google.Cloud.SecretManager.V1.Secret;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Cloud secrets provider implementation for Google Cloud Secret Manager
/// Uses the official Google.Cloud.SecretManager.V1 SDK
/// </summary>
public class GoogleSecretManagerProvider : ICloudSecretsProvider
{
    private readonly CloudSecretsProviderConfig _config;
    private readonly ILoggingService _logger;
    private SecretManagerServiceClient? _client;

    public GoogleSecretManagerProvider(CloudSecretsProviderConfig config, ILoggingService logger)
    {
        _config = config;
        _logger = logger;
    }

    public CloudSecretsProviderType ProviderType => CloudSecretsProviderType.GoogleSecretManager;
    public string DisplayName => "Google Secret Manager";

    #region Configuration Helpers

    private string ProjectId => _config.Settings.GetValueOrDefault("ProjectId", "");
    private string CredentialsJson => _config.Settings.GetValueOrDefault("CredentialsJson", "");
    private string SecretPrefix => _config.Settings.GetValueOrDefault("SecretPrefix", "");

    #endregion

    #region Client Initialization

    private SecretManagerServiceClient GetClient()
    {
        if (_client != null)
            return _client;

#pragma warning disable CS0618 // Using deprecated method - will update when CredentialFactory is stable
        var credential = GoogleCredential.FromJson(CredentialsJson);
#pragma warning restore CS0618
        var builder = new SecretManagerServiceClientBuilder
        {
            Credential = credential
        };
        
        _client = builder.Build();
        return _client;
    }

    #endregion

    #region ICloudSecretsProvider Implementation

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var parent = $"projects/{ProjectId}";
            
            // Try to list secrets to verify access
            var request = new ListSecretsRequest { Parent = parent };
            var response = client.ListSecretsAsync(request);
            
            await foreach (var _ in response.AsRawResponses().WithCancellation(cancellationToken))
            {
                break; // Just need to verify we can access
            }

            _logger.LogInformation($"Google Secret Manager connection test successful for project {ProjectId}");
            return true;
        }
        catch (Grpc.Core.RpcException ex)
        {
            _logger.LogError($"Google Secret Manager connection test failed: {ex.StatusCode} - {ex.Message}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Google Secret Manager connection test error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var secretId = GetSecretId(key);
            var parent = $"projects/{ProjectId}";
            
            // Check if secret exists
            var secretExists = await SecretExistsInternalAsync(secretId, cancellationToken);
            
            string secretResourceName;
            if (!secretExists)
            {
                // Create the secret first
                var secret = new GcpSecret
                {
                    Replication = new Replication { Automatic = new Replication.Types.Automatic() }
                };
                
                // Add labels if metadata provided
                if (metadata != null)
                {
                    foreach (var kvp in metadata)
                    {
                        secret.Labels[SanitizeLabel(kvp.Key)] = SanitizeLabel(kvp.Value);
                    }
                }
                
                var createRequest = new CreateSecretRequest
                {
                    Parent = parent,
                    SecretId = secretId,
                    Secret = secret
                };
                
                var createdSecret = await client.CreateSecretAsync(createRequest, cancellationToken);
                secretResourceName = createdSecret.Name;
            }
            else
            {
                secretResourceName = $"projects/{ProjectId}/secrets/{secretId}";
            }
            
            // Add a new version with the secret data
            var addVersionRequest = new AddSecretVersionRequest
            {
                Parent = secretResourceName,
                Payload = new SecretPayload { Data = ByteString.CopyFrom(value) }
            };
            
            await client.AddSecretVersionAsync(addVersionRequest, cancellationToken);

            _logger.LogInformation($"Stored secret: {key}");
            return true;
        }
        catch (Grpc.Core.RpcException ex)
        {
            _logger.LogError($"Google Secret Manager store secret failed: {ex.StatusCode} - {ex.Message}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Google Secret Manager store secret error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var secretId = GetSecretId(key);
            var secretVersionName = $"projects/{ProjectId}/secrets/{secretId}/versions/latest";
            
            var request = new AccessSecretVersionRequest { Name = secretVersionName };
            var response = await client.AccessSecretVersionAsync(request, cancellationToken);

            return response.Payload?.Data?.ToByteArray();
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return null;
        }
        catch (Grpc.Core.RpcException ex)
        {
            _logger.LogError($"Google Secret Manager get secret failed: {ex.StatusCode} - {ex.Message}", ex);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Google Secret Manager get secret error: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var secretId = GetSecretId(key);
            var secretName = $"projects/{ProjectId}/secrets/{secretId}";
            
            var request = new DeleteSecretRequest { Name = secretName };
            await client.DeleteSecretAsync(request, cancellationToken);
            
            _logger.LogInformation($"Deleted secret: {key}");
            return true;
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            _logger.LogInformation($"Secret already deleted or not found: {key}");
            return true;
        }
        catch (Grpc.Core.RpcException ex)
        {
            _logger.LogError($"Google Secret Manager delete secret failed: {ex.StatusCode} - {ex.Message}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Google Secret Manager delete secret error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var secretId = GetSecretId(key);
            return await SecretExistsInternalAsync(secretId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Google Secret Manager secret exists check error: {ex.Message}", ex);
            return false;
        }
    }

    private async Task<bool> SecretExistsInternalAsync(string secretId, CancellationToken cancellationToken)
    {
        try
        {
            var client = GetClient();
            var secretName = $"projects/{ProjectId}/secrets/{secretId}";
            
            var request = new GetSecretRequest { Name = secretName };
            await client.GetSecretAsync(request, cancellationToken);
            return true;
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListSecretsAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var parent = $"projects/{ProjectId}";
            var allSecrets = new List<string>();
            var effectivePrefix = GetSecretId(prefix ?? "");

            var request = new ListSecretsRequest { Parent = parent };
            var response = client.ListSecretsAsync(request);

            await foreach (var secret in response.WithCancellation(cancellationToken))
            {
                // Extract secret ID from name (format: projects/{project}/secrets/{secretId})
                var name = secret.Name;
                var lastSlash = name.LastIndexOf('/');
                if (lastSlash < 0) continue;
                var secretId = name[(lastSlash + 1)..];

                // Filter by prefix if specified
                if (!string.IsNullOrEmpty(effectivePrefix) && !secretId.StartsWith(effectivePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Remove our prefix to get the original key
                var originalKey = RemoveSecretPrefix(secretId);
                allSecrets.Add(originalKey);
            }

            return allSecrets.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Google Secret Manager list secrets error: {ex.Message}", ex);
            return Array.Empty<string>();
        }
    }

    #endregion

    #region Private Helpers

    private string GetSecretId(string key)
    {
        if (string.IsNullOrEmpty(SecretPrefix))
            return SanitizeSecretId(key);
        
        return SanitizeSecretId($"{SecretPrefix}-{key}");
    }

    private string RemoveSecretPrefix(string secretId)
    {
        if (string.IsNullOrEmpty(SecretPrefix))
            return secretId;
        
        var prefix = SanitizeSecretId($"{SecretPrefix}-");
        if (secretId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return secretId[prefix.Length..];
        
        return secretId;
    }

    /// <summary>
    /// Sanitize secret ID for Google Secret Manager
    /// </summary>
    private static string SanitizeSecretId(string id)
    {
        var sanitized = new StringBuilder();
        foreach (var c in id)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                sanitized.Append(c);
            else
                sanitized.Append('-');
        }
        
        var result = sanitized.ToString();
        
        // Must start with a letter
        if (result.Length > 0 && !char.IsLetter(result[0]))
            result = "S" + result;
        
        // Limit to 255 characters
        if (result.Length > 255)
            result = result[..255];
        
        return result;
    }

    /// <summary>
    /// Sanitize label key/value for Google Cloud
    /// </summary>
    private static string SanitizeLabel(string value)
    {
        var sanitized = new StringBuilder();
        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                sanitized.Append(c);
            else
                sanitized.Append('-');
        }
        
        var result = sanitized.ToString();
        
        // Must start with a letter
        if (result.Length > 0 && !char.IsLetter(result[0]))
            result = "l" + result;
        
        // Limit to 63 characters
        if (result.Length > 63)
            result = result[..63];
        
        return result;
    }

    #endregion
}
