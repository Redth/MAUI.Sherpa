using System.Text;
using Amazon;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Cloud secrets provider implementation for AWS Secrets Manager
/// Uses the official AWSSDK.SecretsManager SDK
/// </summary>
public class AwsSecretsManagerProvider : ICloudSecretsProvider
{
    private readonly CloudSecretsProviderConfig _config;
    private readonly ILoggingService _logger;
    private AmazonSecretsManagerClient? _client;

    public AwsSecretsManagerProvider(CloudSecretsProviderConfig config, ILoggingService logger)
    {
        _config = config;
        _logger = logger;
    }

    public CloudSecretsProviderType ProviderType => CloudSecretsProviderType.AwsSecretsManager;
    public string DisplayName => "AWS Secrets Manager";

    #region Configuration Helpers

    private string Region => _config.Settings.GetValueOrDefault("Region", "us-east-1");
    private string AccessKeyId => _config.Settings.GetValueOrDefault("AccessKeyId", "");
    private string SecretAccessKey => _config.Settings.GetValueOrDefault("SecretAccessKey", "");
    private string SecretPrefix => _config.Settings.GetValueOrDefault("SecretPrefix", "");

    #endregion

    #region Client Initialization

    private AmazonSecretsManagerClient GetClient()
    {
        if (_client != null)
            return _client;

        var credentials = new BasicAWSCredentials(AccessKeyId, SecretAccessKey);
        var config = new AmazonSecretsManagerConfig
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(Region)
        };
        
        _client = new AmazonSecretsManagerClient(credentials, config);
        return _client;
    }

    #endregion

    #region ICloudSecretsProvider Implementation

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            
            // Try to list secrets to verify credentials
            await client.ListSecretsAsync(new ListSecretsRequest { MaxResults = 1 }, cancellationToken);
            
            _logger.LogInformation($"AWS Secrets Manager connection test successful for region {Region}");
            return true;
        }
        catch (AmazonSecretsManagerException ex)
        {
            _logger.LogError($"AWS Secrets Manager connection test failed: {ex.StatusCode} - {ex.Message}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"AWS Secrets Manager connection test error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var secretName = GetSecretName(key);
            
            // First check if the secret exists
            if (await SecretExistsInternalAsync(secretName, cancellationToken))
            {
                // Update existing secret
                var updateRequest = new PutSecretValueRequest
                {
                    SecretId = secretName,
                    SecretBinary = new MemoryStream(value)
                };
                
                await client.PutSecretValueAsync(updateRequest, cancellationToken);
            }
            else
            {
                // Create new secret
                var createRequest = new CreateSecretRequest
                {
                    Name = secretName,
                    SecretBinary = new MemoryStream(value)
                };
                
                // Add tags if metadata provided
                if (metadata != null && metadata.Count > 0)
                {
                    createRequest.Tags = metadata.Select(kv => new Tag { Key = kv.Key, Value = kv.Value }).ToList();
                }
                
                await client.CreateSecretAsync(createRequest, cancellationToken);
            }

            _logger.LogInformation($"Stored secret: {key}");
            return true;
        }
        catch (AmazonSecretsManagerException ex)
        {
            _logger.LogError($"AWS Secrets Manager store secret failed: {ex.StatusCode} - {ex.Message}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"AWS Secrets Manager store secret error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var secretName = GetSecretName(key);
            
            var request = new GetSecretValueRequest { SecretId = secretName };
            var response = await client.GetSecretValueAsync(request, cancellationToken);

            if (response.SecretBinary != null)
            {
                using var ms = new MemoryStream();
                await response.SecretBinary.CopyToAsync(ms, cancellationToken);
                return ms.ToArray();
            }
            
            // If stored as string, return as UTF8 bytes
            if (response.SecretString != null)
            {
                return Encoding.UTF8.GetBytes(response.SecretString);
            }

            return null;
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
        catch (AmazonSecretsManagerException ex)
        {
            _logger.LogError($"AWS Secrets Manager get secret failed: {ex.StatusCode} - {ex.Message}", ex);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError($"AWS Secrets Manager get secret error: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var secretName = GetSecretName(key);
            
            var request = new DeleteSecretRequest
            {
                SecretId = secretName,
                ForceDeleteWithoutRecovery = true
            };
            
            await client.DeleteSecretAsync(request, cancellationToken);
            
            _logger.LogInformation($"Deleted secret: {key}");
            return true;
        }
        catch (ResourceNotFoundException)
        {
            _logger.LogInformation($"Secret already deleted or not found: {key}");
            return true;
        }
        catch (AmazonSecretsManagerException ex)
        {
            _logger.LogError($"AWS Secrets Manager delete secret failed: {ex.StatusCode} - {ex.Message}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"AWS Secrets Manager delete secret error: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var secretName = GetSecretName(key);
            return await SecretExistsInternalAsync(secretName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError($"AWS Secrets Manager secret exists check error: {ex.Message}", ex);
            return false;
        }
    }

    private async Task<bool> SecretExistsInternalAsync(string secretName, CancellationToken cancellationToken)
    {
        try
        {
            var client = GetClient();
            await client.DescribeSecretAsync(new DescribeSecretRequest { SecretId = secretName }, cancellationToken);
            return true;
        }
        catch (ResourceNotFoundException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListSecretsAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetClient();
            var allSecrets = new List<string>();
            var effectivePrefix = GetSecretName(prefix ?? "");
            string? nextToken = null;

            do
            {
                var request = new ListSecretsRequest
                {
                    MaxResults = 100,
                    NextToken = nextToken
                };
                
                var response = await client.ListSecretsAsync(request, cancellationToken);

                foreach (var secret in response.SecretList)
                {
                    if (string.IsNullOrEmpty(secret.Name))
                        continue;

                    // Filter by prefix if specified
                    if (!string.IsNullOrEmpty(effectivePrefix) && !secret.Name.StartsWith(effectivePrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Remove our prefix to get the original key
                    var originalKey = RemoveSecretPrefix(secret.Name);
                    allSecrets.Add(originalKey);
                }

                nextToken = response.NextToken;
            } while (!string.IsNullOrEmpty(nextToken));

            return allSecrets.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError($"AWS Secrets Manager list secrets error: {ex.Message}", ex);
            return Array.Empty<string>();
        }
    }

    #endregion

    #region Private Helpers

    private string GetSecretName(string key)
    {
        if (string.IsNullOrEmpty(SecretPrefix))
            return SanitizeSecretName(key);
        
        return SanitizeSecretName($"{SecretPrefix}/{key}");
    }

    private string RemoveSecretPrefix(string secretName)
    {
        if (string.IsNullOrEmpty(SecretPrefix))
            return secretName;
        
        var prefix = SanitizeSecretName($"{SecretPrefix}/");
        if (secretName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return secretName[prefix.Length..];
        
        return secretName;
    }

    /// <summary>
    /// Sanitize secret name for AWS Secrets Manager
    /// </summary>
    private static string SanitizeSecretName(string name)
    {
        var sanitized = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '/' || c == '+' || c == '=' || c == '.' || c == '_' || c == '-')
                sanitized.Append(c);
            else
                sanitized.Append('-');
        }
        
        var result = sanitized.ToString();
        
        // Secret names must be 1-512 characters
        if (result.Length > 512)
            result = result[..512];
        
        return result;
    }

    #endregion
}
