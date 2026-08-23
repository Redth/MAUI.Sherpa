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
    private readonly Func<string, IInfisicalSdkClient> _clientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _authenticationLock = new(1, 1);
    private IInfisicalSdkClient? _client;
    private DateTimeOffset _reauthenticateAt = DateTimeOffset.MinValue;

    public InfisicalProvider(CloudSecretsProviderConfig config, ILoggingService logger)
        : this(config, logger, siteUrl => new InfisicalSdkClient(siteUrl), TimeProvider.System)
    {
    }

    internal InfisicalProvider(
        CloudSecretsProviderConfig config,
        ILoggingService logger,
        Func<string, IInfisicalSdkClient> clientFactory,
        TimeProvider timeProvider)
    {
        _config = config;
        _logger = logger;
        _clientFactory = clientFactory;
        _timeProvider = timeProvider;
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

    private async Task<IInfisicalSdkClient?> GetClientAsync(CancellationToken cancellationToken = default)
    {
        if (_client != null && _timeProvider.GetUtcNow() < _reauthenticateAt)
            return _client;

        await _authenticationLock.WaitAsync(cancellationToken);
        try
        {
            if (_client != null && _timeProvider.GetUtcNow() < _reauthenticateAt)
                return _client;

            _client ??= _clientFactory(SiteUrl);
            var credential = await _client.LoginAsync(ClientId, ClientSecretValue, cancellationToken);
            var lifetime = TimeSpan.FromSeconds(decimal.ToDouble(credential.ExpiresIn));
            var refreshSkew = TimeSpan.FromSeconds(Math.Min(30, lifetime.TotalSeconds * 0.1));
            _reauthenticateAt = _timeProvider.GetUtcNow() + lifetime - refreshSkew;
            return _client;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Infisical authentication failed: {ex.Message}", ex);
            _reauthenticateAt = DateTimeOffset.MinValue;
            return null;
        }
        finally
        {
            _authenticationLock.Release();
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
            
            await client.ListSecretsAsync(options, cancellationToken);
            
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
            var base64Value = Convert.ToBase64String(value);
            await UpsertSecretAsync(
                client,
                secretName,
                base64Value,
                ToInfisicalMetadata(metadata),
                cancellationToken);

            if (metadata is { Count: 0 })
                await DeleteLegacyMetadataAsync(client, secretName, cancellationToken);

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
                throw new InvalidOperationException("Infisical client is unavailable.");

            var secretName = SanitizeSecretName(key);
            
            var secret = await GetSecretRecordAsync(client, secretName, cancellationToken);
            
            if (secret?.SecretValue == null)
                return null;

            // Decode from base64
            return Convert.FromBase64String(secret.SecretValue);
        }
        catch (InfisicalException ex) when (IsNotFound(ex))
        {
            return null;
        }
        catch (FormatException ex)
        {
            _logger.LogError($"Infisical secret not base64 encoded: {key} - {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Infisical get secret error: {ex.Message}", ex);
            throw;
        }
    }

    public async Task<Dictionary<string, string>?> GetSecretMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = await GetClientAsync(cancellationToken);
            if (client == null)
                return null;

            var secretName = SanitizeSecretName(key);
            var secret = await TryGetSecretRecordAsync(client, secretName, cancellationToken);
            if (secret is null)
                return null;

            if (secret.Metadata is { Length: > 0 })
                return FromInfisicalMetadata(secret.Metadata);

            var metadataSecret = await TryGetSecretRecordAsync(
                client,
                GetLegacyMetadataSecretName(secretName),
                cancellationToken);
            var metadataBytes = metadataSecret is null
                ? null
                : Convert.FromBase64String(metadataSecret.SecretValue);
            return metadataBytes is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : CloudSecretMetadata.Deserialize(metadataBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Infisical get secret metadata error: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<bool> SetSecretMetadataAsync(
        string key,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var secretName = SanitizeSecretName(key);
            var client = await GetClientAsync(cancellationToken);
            if (client == null)
                return false;

            var options = new UpdateSecretOptions
            {
                SecretName = secretName,
                EnvironmentSlug = Environment,
                SecretPath = SecretPath,
                ProjectId = ProjectId,
                NewMetadata = ToInfisicalMetadata(metadata),
            };

            try
            {
                await client.UpdateSecretAsync(options, cancellationToken);
            }
            catch (InfisicalException ex) when (IsNotFound(ex))
            {
                return false;
            }

            if (metadata.Count == 0)
                await DeleteLegacyMetadataAsync(client, secretName, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Infisical set secret metadata error: {ex.Message}", ex);
            return false;
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
            
            await client.DeleteSecretAsync(options, cancellationToken);
            if (!CloudSecretMetadata.IsMetadataKey(key))
                await DeleteSecretAsync(CloudSecretMetadata.GetMetadataKey(secretName), cancellationToken);
            
            _logger.LogInformation($"Deleted secret: {key}");
            return true;
        }
        catch (InfisicalException ex) when (IsNotFound(ex))
        {
            if (!CloudSecretMetadata.IsMetadataKey(key))
                await DeleteSecretAsync(CloudSecretMetadata.GetMetadataKey(SanitizeSecretName(key)), cancellationToken);
            _logger.LogInformation($"Secret already deleted or not found: {key}");
            return true;
        }
        catch (InfisicalException ex)
        {
            var innerMsg = ex.InnerException?.Message;
            _logger.LogError($"Infisical delete secret failed for '{key}': {ex.Message}{(innerMsg != null ? $" → {innerMsg}" : "")}", ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Infisical delete secret error for '{key}': {ex.Message}", ex);
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
            throw;
        }
    }

    private async Task<bool> SecretExistsInternalAsync(
        IInfisicalSdkClient client,
        string secretName,
        CancellationToken cancellationToken)
    {
        try
        {
            await GetSecretRecordAsync(client, secretName, cancellationToken);
            return true;
        }
        catch (InfisicalException ex) when (IsNotFound(ex))
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
            
            var secrets = await client.ListSecretsAsync(options, cancellationToken);
            
            if (secrets == null)
                return Array.Empty<string>();

            _logger.LogDebug($"Infisical ListSecrets returned {secrets.Length} secrets (path={SecretPath}, env={Environment})");
            var sanitizedPrefix = !string.IsNullOrEmpty(prefix) ? SanitizeSecretName(prefix) : null;
            var result = new List<string>();
            foreach (var secret in secrets)
            {
                _logger.LogDebug($"  Secret: {secret.SecretKey} path={secret.SecretPath} env={secret.Environment}");

                // Skip imported secrets from other paths — they can't be deleted from our path
                if (!string.IsNullOrEmpty(secret.SecretPath) &&
                    !string.Equals(secret.SecretPath, SecretPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug($"  → Skipped (path mismatch: '{secret.SecretPath}' != '{SecretPath}')");
                    continue;
                }

                var secretKey = secret.SecretKey;
                
                // Filter by sanitized prefix if specified
                if (sanitizedPrefix != null && !secretKey.StartsWith(sanitizedPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (CloudSecretMetadata.IsMetadataKey(secretKey))
                    continue;
                
                result.Add(secretKey);
            }

            return result.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Infisical list secrets error: {ex.Message}", ex);
            throw;
        }
    }

    #endregion

    #region Private Helpers

    private async Task UpsertSecretAsync(
        IInfisicalSdkClient client,
        string secretName,
        string secretValue,
        SecretMetadata[]? metadata,
        CancellationToken cancellationToken)
    {
        var updateOptions = new UpdateSecretOptions
        {
            SecretName = secretName,
            EnvironmentSlug = Environment,
            SecretPath = SecretPath,
            ProjectId = ProjectId,
            NewSecretValue = secretValue,
            NewMetadata = metadata,
        };

        try
        {
            await client.UpdateSecretAsync(updateOptions, cancellationToken);
            return;
        }
        catch (InfisicalException ex) when (IsNotFound(ex))
        {
            var createOptions = new CreateSecretOptions
            {
                SecretName = secretName,
                SecretValue = secretValue,
                EnvironmentSlug = Environment,
                SecretPath = SecretPath,
                ProjectId = ProjectId,
                Metadata = metadata,
            };

            try
            {
                await client.CreateSecretAsync(createOptions, cancellationToken);
                return;
            }
            catch (InfisicalException createException) when (IsAlreadyExists(createException))
            {
                await client.UpdateSecretAsync(updateOptions, cancellationToken);
            }
        }
    }

    private async Task<Secret> GetSecretRecordAsync(
        IInfisicalSdkClient client,
        string secretName,
        CancellationToken cancellationToken)
    {
        var options = new GetSecretOptions
        {
            SecretName = secretName,
            EnvironmentSlug = Environment,
            SecretPath = SecretPath,
            ProjectId = ProjectId,
        };

        return await client.GetSecretAsync(options, cancellationToken);
    }

    private async Task<Secret?> TryGetSecretRecordAsync(
        IInfisicalSdkClient client,
        string secretName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetSecretRecordAsync(client, secretName, cancellationToken);
        }
        catch (InfisicalException ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    private async Task DeleteLegacyMetadataAsync(
        IInfisicalSdkClient client,
        string secretName,
        CancellationToken cancellationToken)
    {
        var options = new DeleteSecretOptions
        {
            SecretName = GetLegacyMetadataSecretName(secretName),
            EnvironmentSlug = Environment,
            SecretPath = SecretPath,
            ProjectId = ProjectId,
        };

        try
        {
            await client.DeleteSecretAsync(options, cancellationToken);
        }
        catch (InfisicalException ex) when (IsNotFound(ex))
        {
        }
    }

    private static SecretMetadata[]? ToInfisicalMetadata(Dictionary<string, string>? metadata) =>
        metadata?
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new SecretMetadata { Key = x.Key, Value = x.Value })
            .ToArray();

    private static string GetLegacyMetadataSecretName(string secretName) =>
        SanitizeSecretName(CloudSecretMetadata.GetMetadataKey(secretName));

    private static Dictionary<string, string> FromInfisicalMetadata(IEnumerable<SecretMetadata> metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in metadata)
            result[item.Key] = item.Value;
        return result;
    }

    internal static bool IsNotFound(Exception exception) =>
        ExceptionChainContains(exception, "not found") ||
        ExceptionChainContains(exception, "notfound") ||
        ExceptionChainContains(exception, "404");

    private static bool IsAlreadyExists(Exception exception) =>
        ExceptionChainContains(exception, "already exists") ||
        ExceptionChainContains(exception, "conflict") ||
        ExceptionChainContains(exception, "409");

    private static bool ExceptionChainContains(Exception exception, string value)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

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

internal interface IInfisicalSdkClient
{
    Task<MachineIdentityCredential> LoginAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken);
    Task<Secret[]> ListSecretsAsync(ListSecretsOptions options, CancellationToken cancellationToken);
    Task<Secret> GetSecretAsync(GetSecretOptions options, CancellationToken cancellationToken);
    Task<Secret> CreateSecretAsync(CreateSecretOptions options, CancellationToken cancellationToken);
    Task<Secret> UpdateSecretAsync(UpdateSecretOptions options, CancellationToken cancellationToken);
    Task<Secret> DeleteSecretAsync(DeleteSecretOptions options, CancellationToken cancellationToken);
}

internal sealed class InfisicalSdkClient : IInfisicalSdkClient
{
    private readonly InfisicalClient _client;

    public InfisicalSdkClient(string siteUrl)
    {
        var settings = new InfisicalSdkSettingsBuilder()
            .WithHostUri(siteUrl)
            .Build();
        _client = new InfisicalClient(settings);
    }

    public Task<MachineIdentityCredential> LoginAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _client.Auth().UniversalAuth().LoginAsync(clientId, clientSecret);
    }

    public Task<Secret[]> ListSecretsAsync(
        ListSecretsOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _client.Secrets().ListAsync(options);
    }

    public Task<Secret> GetSecretAsync(
        GetSecretOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _client.Secrets().GetAsync(options);
    }

    public Task<Secret> CreateSecretAsync(
        CreateSecretOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _client.Secrets().CreateAsync(options);
    }

    public Task<Secret> UpdateSecretAsync(
        UpdateSecretOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _client.Secrets().UpdateAsync(options);
    }

    public Task<Secret> DeleteSecretAsync(
        DeleteSecretOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _client.Secrets().DeleteAsync(options);
    }
}
