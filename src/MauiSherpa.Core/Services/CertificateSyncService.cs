using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Service for syncing certificate private keys between local keychain and cloud storage
/// </summary>
public class CertificateSyncService : ICertificateSyncService
{
    private readonly ICloudSecretsService _cloudSecretsService;
    private readonly ILocalCertificateService _localCertificateService;
    private readonly ILoggingService _logger;
    private readonly ISecretsProviderRegistry? _providerRegistry;
    
    private const string SecretPrefix = "CERT";

    public CertificateSyncService(
        ICloudSecretsService cloudSecretsService,
        ILocalCertificateService localCertificateService,
        ILoggingService logger,
        ISecretsProviderRegistry? providerRegistry = null)
    {
        _cloudSecretsService = cloudSecretsService;
        _localCertificateService = localCertificateService;
        _logger = logger;
        _providerRegistry = providerRegistry ?? cloudSecretsService as ISecretsProviderRegistry;
    }

    /// <summary>
    /// A secrets provider that may hold certificate copies.
    /// </summary>
    private sealed record ProviderSource(
        string? Id,
        string Name,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> ListAsync,
        Func<string, CancellationToken, Task<byte[]?>> GetAsync);

    private sealed record ProviderCertificate(byte[] P12, string? Password, string ProviderName, string Key);

    /// <summary>
    /// Every configured provider in read order. Without a registry only the active provider
    /// is available.
    /// </summary>
    private async Task<IReadOnlyList<ProviderSource>> GetProviderSourcesAsync(
        CancellationToken cancellationToken,
        bool preferActiveProvider = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activeSource = GetActiveProviderSource();
        var activeSources = activeSource is null ? Array.Empty<ProviderSource>() : new[] { activeSource };
        if (_providerRegistry is null)
            return activeSources;

        var activeId = activeSource?.Id;
        IReadOnlyList<CloudSecretsProviderConfig> providerConfigs;
        try
        {
            providerConfigs = await _providerRegistry.GetProvidersAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning($"Could not list certificate providers: {ex.Message}");
            return activeSources;
        }

        var configs = providerConfigs
            .OrderBy(config =>
                preferActiveProvider && config.Id == activeId ? 0 :
                config.Id == CloudSecretsService.DefaultLocalProviderId ? (preferActiveProvider ? 1 : 0) :
                config.Id == activeId ? 1 : 2)
            .ThenBy(config => config.Name, StringComparer.OrdinalIgnoreCase);

        var sources = new List<ProviderSource>();
        foreach (var config in configs)
        {
            var providerId = config.Id;
            var fallback = providerId == activeId ? activeSource : null;
            Task<ICloudSecretsProvider?>? providerTask = null;
            Task<ICloudSecretsProvider?> GetProviderAsync(CancellationToken ct) =>
                providerTask ??= ResolveProviderAsync(providerId, config.Name, fallback is not null, ct);
            sources.Add(new ProviderSource(
                providerId,
                config.Name,
                async (prefix, ct) =>
                {
                    var provider = await GetProviderAsync(ct);
                    return provider is null
                        ? await fallback!.ListAsync(prefix, ct)
                        : await provider.ListSecretsStrictAsync(prefix, ct);
                },
                async (key, ct) =>
                {
                    var provider = await GetProviderAsync(ct);
                    return provider is null
                        ? await fallback!.GetAsync(key, ct)
                        : await provider.GetSecretAsync(key, ct);
                }));
        }

        if (activeSource is not null && sources.All(source => source.Id != activeId))
        {
            _logger.LogWarning($"Active provider '{activeId}' was not returned by the certificate provider registry");
            var localCount = sources.Count(source => source.Id == CloudSecretsService.DefaultLocalProviderId);
            sources.Insert(preferActiveProvider ? 0 : localCount, activeSource);
        }

        return sources;
    }

    private async Task<ICloudSecretsProvider?> ResolveProviderAsync(
        string providerId,
        string providerName,
        bool allowActiveFallback,
        CancellationToken cancellationToken)
    {
        ICloudSecretsProvider? provider;
        try
        {
            provider = await _providerRegistry!.GetProviderAsync(providerId);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (allowActiveFallback && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning($"Could not resolve active certificate provider '{providerName}': {ex.Message}");
            return null;
        }

        if (provider is null)
        {
            if (!allowActiveFallback)
                throw new InvalidOperationException($"Provider '{providerName}' is unavailable.");
            _logger.LogWarning($"Active certificate provider '{providerName}' is unavailable in the registry");
        }

        return provider;
    }

    private ProviderSource? GetActiveProviderSource()
    {
        var active = _cloudSecretsService.ActiveProvider;
        return active is null
            ? null
            : new ProviderSource(
                active.Id,
                active.Name,
                (prefix, ct) => UseActiveProviderAsync(
                    active.Id, () => _cloudSecretsService.ListSecretsAsync(prefix, ct)),
                (key, ct) => UseActiveProviderAsync(
                    active.Id, () => _cloudSecretsService.GetSecretAsync(key, ct)));
    }

    private async Task<T> UseActiveProviderAsync<T>(string providerId, Func<Task<T>> operation)
    {
        EnsureActiveProviderUnchanged(providerId);
        var result = await operation();
        EnsureActiveProviderUnchanged(providerId);
        return result;
    }

    private void EnsureActiveProviderUnchanged(string providerId)
    {
        if (!string.Equals(_cloudSecretsService.ActiveProvider?.Id, providerId, StringComparison.Ordinal))
            throw new InvalidOperationException("The active secrets provider changed during the certificate operation.");
    }

    public string GetCertificateSecretKey(string serialNumber)
        => $"{SecretPrefix}_{SanitizeSerialNumber(serialNumber)}_P12";

    public string GetCertificatePasswordKey(string serialNumber)
        => $"{SecretPrefix}_{SanitizeSerialNumber(serialNumber)}_PWD";

    public string GetCertificateMetadataKey(string serialNumber)
        => $"{SecretPrefix}_{SanitizeSerialNumber(serialNumber)}_META";

    public async Task<IReadOnlyList<CertificateSecretInfo>> GetCertificateStatusesAsync(
        IEnumerable<AppleCertificate> certificates,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CertificateSecretInfo>();
        
        // Get all local signing identities
        // Note: security find-identity only returns identities with private keys,
        // so all returned identities have private keys by definition
        var localIdentities = _localCertificateService.IsSupported 
            ? await _localCertificateService.GetSigningIdentitiesAsync()
            : Array.Empty<LocalSigningIdentity>();
        cancellationToken.ThrowIfCancellationRequested();
        
        var localSerials = localIdentities
            .Where(i => i.IsValid) // Only consider valid certificates
            .Select(i => SanitizeSerialNumber(i.SerialNumber ?? "")) // Sanitize local serials too!
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet();

        // Check every configured provider; the first one holding a copy is reported.
        var cloudSecrets = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var source in await GetProviderSourcesAsync(cancellationToken))
        {
            try
            {
                var secrets = await source.ListAsync($"{SecretPrefix}_", cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var secret in secrets)
                {
                    if (CertificateSecretKeys.TryExtractSerial(secret, CertificateSecretKeys.P12Suffix, out var serial))
                        cloudSecrets.TryAdd(SanitizeSerialNumber(serial), source.Id);
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning($"Could not check certificate copies in '{source.Name}': {ex.Message}");
            }
        }

        foreach (var cert in certificates)
        {
            var serialUpper = cert.SerialNumber?.ToUpperInvariant() ?? "";
            var sanitizedSerial = SanitizeSerialNumber(serialUpper);
            
            var hasLocal = localSerials.Contains(sanitizedSerial);
            var hasCloud = cloudSecrets.TryGetValue(sanitizedSerial, out var cloudProviderId);

            var location = (hasLocal, hasCloud) switch
            {
                (true, true) => SecretLocation.Both,
                (true, false) => SecretLocation.LocalOnly,
                (false, true) => SecretLocation.CloudOnly,
                _ => SecretLocation.None
            };

            results.Add(new CertificateSecretInfo(
                cert.Id ?? "",
                cert.SerialNumber ?? "",
                location,
                hasCloud ? cloudProviderId : null,
                hasCloud ? GetCertificateSecretKey(cert.SerialNumber ?? "") : null,
                null // Would need to track last sync time separately
            ));
        }

        return results.AsReadOnly();
    }

    public async Task<bool> UploadToCloudAsync(
        AppleCertificate certificate,
        byte[] p12Data,
        string password,
        CertificateSecretMetadata? metadata = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activeProvider = _cloudSecretsService.ActiveProvider;
        if (activeProvider is null)
        {
            _logger.LogWarning("No active cloud secrets provider configured");
            return false;
        }
        if (_providerRegistry is null)
        {
            _logger.LogError("Cannot safely upload a certificate without a provider registry");
            return false;
        }

        var serialNumber = certificate.SerialNumber ?? "";
        
        try
        {
            var provider = await ResolveProviderAsync(activeProvider.Id, activeProvider.Name, false, cancellationToken)
                ?? throw new InvalidOperationException($"Provider '{activeProvider.Name}' is unavailable.");

            Task<byte[]?> ReadAsync(string key, CancellationToken token) =>
                provider.GetSecretAsync(key, token);

            Task<bool> WriteAsync(string key, byte[] value, CancellationToken token) =>
                provider.StoreSecretAsync(key, value, null, token);

            Task<bool> DeleteAsync(string key, CancellationToken token) =>
                provider.DeleteSecretAsync(key, token);

            async Task RestoreAsync(string key, byte[]? previous)
            {
                try
                {
                    var restored = previous is null
                        ? await DeleteAsync(key, CancellationToken.None)
                        : await WriteAsync(key, previous, CancellationToken.None);
                    if (!restored)
                        _logger.LogError($"Failed to restore certificate secret {key} after an upload error");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to restore certificate secret {key} after an upload error: {ex.Message}", ex);
                }
            }

            var p12Key = GetCertificateSecretKey(serialNumber);
            var pwdKey = GetCertificatePasswordKey(serialNumber);
            var previousP12 = await ReadAsync(p12Key, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var previousPassword = await ReadAsync(pwdKey, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            bool p12Stored;
            try
            {
                p12Stored = await WriteAsync(p12Key, p12Data, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to store P12 data for certificate {serialNumber}: {ex.Message}", ex);
                p12Stored = false;
            }

            if (!p12Stored)
            {
                _logger.LogError($"Failed to store P12 data for certificate {serialNumber}");
                await RestoreAsync(p12Key, previousP12);
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }

            // Finish or clean up the pair even if cancellation arrives after the P12 was stored.
            var pwdBytes = Encoding.UTF8.GetBytes(password);
            bool passwordStored;
            try
            {
                passwordStored = await WriteAsync(pwdKey, pwdBytes, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to store password for certificate {serialNumber}: {ex.Message}", ex);
                passwordStored = false;
            }

            if (!passwordStored)
            {
                _logger.LogError($"Failed to store password for certificate {serialNumber}");
                await RestoreAsync(pwdKey, previousPassword);
                await RestoreAsync(p12Key, previousP12);
                cancellationToken.ThrowIfCancellationRequested();
                return false;
            }
            cancellationToken.ThrowIfCancellationRequested();

            // Store metadata if provided
            if (metadata != null)
            {
                var metaKey = GetCertificateMetadataKey(serialNumber);
                var metaJson = JsonSerializer.Serialize(metadata);
                var metaBytes = Encoding.UTF8.GetBytes(metaJson);
                var metadataStored = await WriteAsync(metaKey, metaBytes, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!metadataStored)
                {
                    _logger.LogError($"Failed to store metadata for certificate {serialNumber}");
                    return false;
                }
            }

            _logger.LogInformation($"Uploaded certificate {serialNumber} to cloud storage");
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError($"Failed to upload certificate to cloud: {ex.Message}", ex);
            return false;
        }
    }

    public async Task<(byte[]? P12, string? Password)> GetCertificateSecretsAsync(
        string serialNumber,
        bool autoUploadFromKeychain = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            _logger.LogWarning("Cannot resolve a certificate without a serial number");
            return (null, null);
        }

        var storedCertificate = await FindProviderCertificateAsync(
            serialNumber, cancellationToken, preferActiveProvider: true);
        if (storedCertificate is not null)
        {
            _logger.LogInformation(
                $"Resolved certificate {serialNumber} from '{storedCertificate.ProviderName}' (key {storedCertificate.Key})");
            return (storedCertificate.P12, storedCertificate.Password);
        }

        // Local macOS keychain fallback — export the private key directly.
        if (_localCertificateService.IsSupported)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var identities = await _localCertificateService.GetSigningIdentitiesAsync();
                cancellationToken.ThrowIfCancellationRequested();
                var identity = identities.FirstOrDefault(i => SerialsFuzzyEqual(i.SerialNumber, serialNumber));
                if (identity is not null)
                {
                    var password = GenerateRandomPassword();
                    var p12 = await _localCertificateService.ExportP12Async(identity.Identity, password);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (CertificateBundleUtilities.ContainsPrivateKey(p12, password, serialNumber))
                    {
                        _logger.LogInformation($"Resolved certificate {serialNumber} from local keychain (identity '{identity.Identity}')");

                        if (autoUploadFromKeychain && _cloudSecretsService.ActiveProvider != null)
                        {
                            try
                            {
                                var cert = new AppleCertificate(
                                    Id: string.Empty,
                                    Name: identity.CommonName,
                                    CertificateType: string.Empty,
                                    Platform: string.Empty,
                                    ExpirationDate: identity.ExpirationDate ?? DateTime.MinValue,
                                    SerialNumber: serialNumber);
                                var metadata = new CertificateSecretMetadata(
                                    CertificateId: string.Empty,
                                    SerialNumber: serialNumber,
                                    CommonName: identity.CommonName,
                                    CertificateType: string.Empty,
                                    ExpirationDate: identity.ExpirationDate ?? DateTime.MinValue,
                                    CreatedByMachine: Environment.MachineName,
                                    CreatedAt: DateTime.UtcNow);
                                if (await UploadToCloudAsync(cert, p12, password, metadata, cancellationToken))
                                    _logger.LogInformation($"Auto-uploaded certificate {serialNumber} to cloud storage for future runs");
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                            {
                                _logger.LogWarning($"Auto-upload of certificate {serialNumber} to cloud failed: {ex.Message}");
                            }
                        }

                        return (p12, password);
                    }

                    _logger.LogWarning($"Keychain export for certificate {serialNumber} did not contain a usable private key");
                }
                else
                {
                    _logger.LogWarning($"No local keychain identity found for certificate serial {serialNumber}");
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning($"Local keychain fallback for certificate {serialNumber} failed: {ex.Message}");
            }
        }

        _logger.LogWarning($"Certificate {serialNumber} could not be resolved from cloud storage or local keychain");
        return (null, null);
    }

    /// <summary>
    /// Searches providers in priority order, checking exact keys and then legacy serial forms.
    /// </summary>
    private async Task<ProviderCertificate?> FindProviderCertificateAsync(
        string serialNumber,
        CancellationToken cancellationToken,
        bool preferActiveProvider = false)
    {
        var exactKey = GetCertificateSecretKey(serialNumber);
        foreach (var source in await GetProviderSourcesAsync(cancellationToken, preferActiveProvider))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var certificate = await ReadProviderCertificateAsync(source, exactKey, serialNumber, cancellationToken);
            if (certificate is not null)
                return certificate;

            IReadOnlyList<string> keys;
            try
            {
                keys = await source.ListAsync(CertificateSecretKeys.Prefix, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning($"Could not list certificate copies in '{source.Name}': {ex.Message}");
                continue;
            }

            foreach (var key in keys)
            {
                if (string.Equals(key, exactKey, StringComparison.Ordinal) ||
                    !CertificateSecretKeys.TryExtractSerial(key, CertificateSecretKeys.P12Suffix, out var storedSerial) ||
                    !SerialsFuzzyEqual(storedSerial, serialNumber))
                {
                    continue;
                }

                certificate = await ReadProviderCertificateAsync(source, key, serialNumber, cancellationToken);
                if (certificate is not null)
                    return certificate;
            }
        }

        return null;
    }

    private async Task<ProviderCertificate?> ReadProviderCertificateAsync(
        ProviderSource source,
        string p12Key,
        string serialNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            var p12 = await source.GetAsync(p12Key, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (p12 is not { Length: > 0 })
                return null;

            var passwordKey = p12Key[..^3] + "PWD";
            var passwordBytes = await source.GetAsync(passwordKey, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var password = passwordBytes is null ? null : Encoding.UTF8.GetString(passwordBytes);
            if (!CertificateBundleUtilities.ContainsPrivateKey(p12, password, serialNumber))
            {
                _logger.LogWarning(
                    $"Certificate copy '{p12Key}' in '{source.Name}' could not be opened or has no matching private key");
                return null;
            }

            return new ProviderCertificate(p12, password, source.Name, p12Key);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning($"Could not read certificate copy '{p12Key}' from '{source.Name}': {ex.Message}");
            return null;
        }
    }

    public async Task<bool> DownloadAndInstallAsync(string certificateId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(certificateId))
        {
            _logger.LogWarning("DownloadAndInstallAsync called with empty certificate ID");
            return false;
        }

        foreach (var source in await GetProviderSourcesAsync(cancellationToken))
        {
            IReadOnlyList<string> keys;
            try
            {
                keys = await source.ListAsync(CertificateSecretKeys.Prefix, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning($"Could not list certificate metadata in '{source.Name}': {ex.Message}");
                continue;
            }

            foreach (var key in keys)
            {
                if (!CertificateSecretKeys.TryExtractSerial(key, CertificateSecretKeys.MetadataSuffix, out var storedSerial))
                    continue;

                try
                {
                    var data = await source.GetAsync(key, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    var metadata = data is null ? null : JsonSerializer.Deserialize<CertificateSecretMetadata>(data);
                    if (metadata is null ||
                        string.IsNullOrWhiteSpace(metadata.SerialNumber) ||
                        !SerialsFuzzyEqual(storedSerial, metadata.SerialNumber) ||
                        !string.Equals(metadata.CertificateId, certificateId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _logger.LogInformation($"Resolved certificate ID {certificateId} to serial {metadata.SerialNumber}");
                    return await DownloadAndInstallBySerialAsync(metadata.SerialNumber, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning($"Could not read certificate metadata '{key}' from '{source.Name}': {ex.Message}");
                }
            }
        }

        _logger.LogWarning($"Could not resolve certificate ID {certificateId} to provider metadata");
        return false;
    }

    /// <summary>
    /// Downloads and installs a certificate from cloud using its serial number
    /// </summary>
    public async Task<bool> DownloadAndInstallBySerialAsync(
        string serialNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            _logger.LogWarning("Cannot install a certificate without a serial number");
            return false;
        }

        var certificate = await FindProviderCertificateAsync(serialNumber, cancellationToken);
        if (certificate is null)
        {
            _logger.LogError($"No provider holds certificate {serialNumber} with its private key");
            return false;
        }

        _logger.LogInformation($"Installing certificate {serialNumber} from '{certificate.ProviderName}'");
        try
        {
            var imported = await ImportP12LocallyAsync(certificate.P12, certificate.Password ?? string.Empty, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!imported)
            {
                _logger.LogError($"Importing certificate {serialNumber} from '{certificate.ProviderName}' failed");
                return false;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError($"Failed to install certificate {serialNumber}: {ex.Message}", ex);
            return false;
        }

        return await VerifySigningIdentityInstalledAsync(serialNumber, cancellationToken);
    }

    /// <summary>
    /// Confirms the Keychain now has a signing identity (certificate + private key) for the
    /// serial. Only checked where the local certificate service can enumerate identities.
    /// </summary>
    private async Task<bool> VerifySigningIdentityInstalledAsync(string serialNumber, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_localCertificateService.IsSupported)
            return true;

        _localCertificateService.InvalidateCache();
        var installed = await _localCertificateService.HasInstalledIdentityAsync(serialNumber);
        cancellationToken.ThrowIfCancellationRequested();
        if (!installed)
        {
            _logger.LogError($"Certificate {serialNumber} was imported but no signing identity with its private key appeared in the Keychain");
            return false;
        }

        var wanted = SanitizeSerialNumber(serialNumber).TrimStart('0');
        var identities = await _localCertificateService.GetSigningIdentitiesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var trusted = identities.Any(candidate =>
            SanitizeSerialNumber(candidate.SerialNumber ?? "").TrimStart('0') == wanted);
        if (!trusted)
        {
            // Usually a missing Apple WWDR intermediate; the key itself is installed.
            _logger.LogWarning($"Signing identity for {serialNumber} is installed but not trusted yet (check the Apple intermediate certificates)");
        }

        _logger.LogInformation($"Signing identity for {serialNumber} is installed");
        return true;
    }

    /// <summary>
    /// Gets certificate metadata from cloud storage
    /// </summary>
    public async Task<CertificateSecretMetadata?> GetCertificateMetadataAsync(
        string serialNumber,
        CancellationToken cancellationToken = default)
    {
        if (_cloudSecretsService.ActiveProvider == null)
            return null;

        try
        {
            var metaKey = GetCertificateMetadataKey(serialNumber);
            var metaData = await _cloudSecretsService.GetSecretAsync(metaKey, cancellationToken);
            if (metaData == null)
                return null;

            var metaJson = Encoding.UTF8.GetString(metaData);
            return JsonSerializer.Deserialize<CertificateSecretMetadata>(metaJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not get certificate metadata: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> DeleteFromCloudAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        if (_cloudSecretsService.ActiveProvider == null)
        {
            _logger.LogError("No cloud secrets provider configured");
            return false;
        }

        _logger.LogInformation($"Deleting certificate from cloud: {serialNumber}");

        var p12Key = GetCertificateSecretKey(serialNumber);
        var pwdKey = GetCertificatePasswordKey(serialNumber);
        var metaKey = GetCertificateMetadataKey(serialNumber);

        var success = true;

        // Delete P12 data
        try
        {
            if (!await _cloudSecretsService.DeleteSecretAsync(p12Key, cancellationToken))
            {
                _logger.LogWarning($"Failed to delete P12 secret: {p12Key}");
                success = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error deleting P12 secret: {ex.Message}");
        }

        // Delete password
        try
        {
            if (!await _cloudSecretsService.DeleteSecretAsync(pwdKey, cancellationToken))
            {
                _logger.LogWarning($"Failed to delete password secret: {pwdKey}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error deleting password secret: {ex.Message}");
        }

        // Delete metadata
        try
        {
            if (!await _cloudSecretsService.DeleteSecretAsync(metaKey, cancellationToken))
            {
                _logger.LogWarning($"Failed to delete metadata secret: {metaKey}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Error deleting metadata secret: {ex.Message}");
        }

        if (success)
        {
            _logger.LogInformation($"Successfully deleted certificate from cloud: {serialNumber}");
        }

        return success;
    }

    #region Private Helpers

    private static string SanitizeSerialNumber(string serialNumber) =>
        SecretItemAdapterHelper.SanitizeSerialNumber(serialNumber);

    /// <summary>
    /// Compares two certificate serial numbers tolerantly, ignoring case, non-alphanumeric
    /// characters and leading zeros. Matches the normalization used to build cloud secret keys.
    /// </summary>
    private static bool SerialsFuzzyEqual(string? a, string? b)
        => SanitizeSerialNumber(a ?? string.Empty).Equals(SanitizeSerialNumber(b ?? string.Empty), StringComparison.Ordinal);

    private static string GenerateRandomPassword()
        => Convert.ToBase64String(Guid.NewGuid().ToByteArray())[..16];

    private Task<bool> ImportP12LocallyAsync(
        byte[] p12Data,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (OperatingSystem.IsLinux())
        {
            return Task.FromResult(ImportP12ToX509Store(p12Data, password));
        }

        return _localCertificateService.ImportP12Async(p12Data, password, cancellationToken);
    }

    private bool ImportP12ToX509Store(byte[] p12Data, string password)
    {
        try
        {
            using var store = new System.Security.Cryptography.X509Certificates.X509Store(
                System.Security.Cryptography.X509Certificates.StoreName.My,
                System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser);
            store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);

            // Import the full P12 payload so the platform-backed user store gets the
            // certificate and private key material together instead of only a transient cert instance.
            var collection = new System.Security.Cryptography.X509Certificates.X509Certificate2Collection();
            collection.Import(p12Data, password,
                System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.PersistKeySet
                | System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.UserKeySet
                | System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.Exportable);

            foreach (var cert in collection)
            {
                store.Add(cert);
                _logger.LogInformation($"Imported certificate: {cert.Subject} (serial: {cert.SerialNumber}, hasKey: {cert.HasPrivateKey})");
                cert.Dispose();
            }

            _logger.LogInformation("Successfully imported certificate to X509 store");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to import P12 to X509 store: {ex.Message}", ex);
            return false;
        }
    }

    #endregion
}
