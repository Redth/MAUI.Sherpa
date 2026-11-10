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
        _providerRegistry = providerRegistry;
    }

    /// <summary>
    /// A secrets provider that may hold certificate copies.
    /// </summary>
    private sealed record ProviderSource(
        string? Id,
        string Name,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> ListAsync,
        Func<string, CancellationToken, Task<byte[]?>> GetAsync);

    /// <summary>
    /// Every configured provider in read order: Local Vault first, then the active provider,
    /// then the rest. Without a registry only the active provider is available.
    /// </summary>
    private async Task<IReadOnlyList<ProviderSource>> GetProviderSourcesAsync()
    {
        if (_providerRegistry is null)
        {
            var active = _cloudSecretsService.ActiveProvider;
            return active is null
                ? Array.Empty<ProviderSource>()
                : new[]
                {
                    new ProviderSource(
                        active.Id,
                        active.Name,
                        (prefix, ct) => _cloudSecretsService.ListSecretsAsync(prefix, ct),
                        (key, ct) => _cloudSecretsService.GetSecretAsync(key, ct))
                };
        }

        var activeId = _cloudSecretsService.ActiveProvider?.Id;
        var configs = (await _providerRegistry.GetProvidersAsync())
            .OrderBy(config =>
                config.Id == CloudSecretsService.DefaultLocalProviderId ? 0 :
                config.Id == activeId ? 1 : 2)
            .ThenBy(config => config.Name, StringComparer.OrdinalIgnoreCase);

        var sources = new List<ProviderSource>();
        foreach (var config in configs)
        {
            var providerId = config.Id;
            sources.Add(new ProviderSource(
                providerId,
                config.Name,
                async (prefix, ct) =>
                {
                    var provider = await _providerRegistry.GetProviderAsync(providerId);
                    return provider is null
                        ? Array.Empty<string>()
                        : await provider.ListSecretsAsync(prefix, ct);
                },
                async (key, ct) =>
                {
                    var provider = await _providerRegistry.GetProviderAsync(providerId);
                    return provider is null ? null : await provider.GetSecretAsync(key, ct);
                }));
        }

        return sources;
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
        
        var localSerials = localIdentities
            .Where(i => i.IsValid) // Only consider valid certificates
            .Select(i => SanitizeSerialNumber(i.SerialNumber ?? "")) // Sanitize local serials too!
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet();

        // Check every configured provider; the first one holding a copy is reported.
        var cloudSecrets = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var source in await GetProviderSourcesAsync())
        {
            try
            {
                var secrets = await source.ListAsync($"{SecretPrefix}_", cancellationToken);
                foreach (var secret in secrets)
                {
                    // Extract serial number from key like "CERT_XXXX_P12"
                    var parts = secret.Split('_');
                    if (parts.Length >= 2)
                    {
                        cloudSecrets.TryAdd(parts[1].ToUpperInvariant(), source.Id);
                    }
                }
            }
            catch (Exception ex)
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
        if (_cloudSecretsService.ActiveProvider == null)
        {
            _logger.LogWarning("No active cloud secrets provider configured");
            return false;
        }

        var serialNumber = certificate.SerialNumber ?? "";
        
        try
        {
            // Store the P12 data
            var p12Key = GetCertificateSecretKey(serialNumber);
            if (!await _cloudSecretsService.StoreSecretAsync(p12Key, p12Data, null, cancellationToken))
            {
                _logger.LogError($"Failed to store P12 data for certificate {serialNumber}");
                return false;
            }

            // Store the password
            var pwdKey = GetCertificatePasswordKey(serialNumber);
            var pwdBytes = Encoding.UTF8.GetBytes(password);
            if (!await _cloudSecretsService.StoreSecretAsync(pwdKey, pwdBytes, null, cancellationToken))
            {
                _logger.LogError($"Failed to store password for certificate {serialNumber}");
                // Clean up the P12 we just stored
                await _cloudSecretsService.DeleteSecretAsync(p12Key, cancellationToken);
                return false;
            }

            // Store metadata if provided
            if (metadata != null)
            {
                var metaKey = GetCertificateMetadataKey(serialNumber);
                var metaJson = JsonSerializer.Serialize(metadata);
                var metaBytes = Encoding.UTF8.GetBytes(metaJson);
                await _cloudSecretsService.StoreSecretAsync(metaKey, metaBytes, null, cancellationToken);
            }

            _logger.LogInformation($"Uploaded certificate {serialNumber} to cloud storage");
            return true;
        }
        catch (Exception ex)
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
            return (null, null);

        // 1) Cloud lookup — exact key, then fuzzy serial match against stored CERT_ secrets.
        if (_cloudSecretsService.ActiveProvider != null)
        {
            try
            {
                var p12Key = GetCertificateSecretKey(serialNumber);
                var p12 = await _cloudSecretsService.GetSecretAsync(p12Key, cancellationToken);
                if (p12 is { Length: > 0 })
                {
                    var pwd = await _cloudSecretsService.GetSecretAsync(GetCertificatePasswordKey(serialNumber), cancellationToken);
                    _logger.LogInformation($"Resolved certificate {serialNumber} from cloud (exact key {p12Key})");
                    return (p12, pwd is not null ? Encoding.UTF8.GetString(pwd) : null);
                }

                // Exact key missed — the secret may have been stored under a differently
                // normalized serial. Scan CERT_*_P12 keys and fuzzy-match the serial.
                var storedSerial = await FindStoredCertSerialAsync(serialNumber, cancellationToken);
                if (storedSerial is not null)
                {
                    var fuzzyP12Key = $"{SecretPrefix}_{storedSerial}_P12";
                    var fuzzyP12 = await _cloudSecretsService.GetSecretAsync(fuzzyP12Key, cancellationToken);
                    if (fuzzyP12 is { Length: > 0 })
                    {
                        var fuzzyPwd = await _cloudSecretsService.GetSecretAsync($"{SecretPrefix}_{storedSerial}_PWD", cancellationToken);
                        _logger.LogInformation($"Resolved certificate {serialNumber} from cloud via fuzzy serial match (key {fuzzyP12Key})");
                        return (fuzzyP12, fuzzyPwd is not null ? Encoding.UTF8.GetString(fuzzyPwd) : null);
                    }
                }

                _logger.LogWarning($"Certificate {serialNumber} not found in cloud storage (tried key {p12Key} and fuzzy serial match)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Cloud lookup for certificate {serialNumber} failed: {ex.Message}");
            }
        }
        else
        {
            _logger.LogWarning($"No active cloud secrets provider while resolving certificate {serialNumber}");
        }

        // 1b) The other configured providers, Local Vault first, without changing the active one.
        var fromOtherProvider = await FindInOtherProvidersAsync(serialNumber, cancellationToken);
        if (fromOtherProvider.P12 is not null)
            return fromOtherProvider;

        // 2) Local macOS keychain fallback — export the private key directly.
        if (_localCertificateService.IsSupported)
        {
            try
            {
                var identities = await _localCertificateService.GetSigningIdentitiesAsync();
                var identity = identities.FirstOrDefault(i => SerialsFuzzyEqual(i.SerialNumber, serialNumber));
                if (identity is not null)
                {
                    var password = GenerateRandomPassword();
                    var p12 = await _localCertificateService.ExportP12Async(identity.Identity, password);
                    if (p12 is { Length: > 0 })
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
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning($"Auto-upload of certificate {serialNumber} to cloud failed: {ex.Message}");
                            }
                        }

                        return (p12, password);
                    }
                }
                else
                {
                    _logger.LogWarning($"No local keychain identity found for certificate serial {serialNumber}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Local keychain fallback for certificate {serialNumber} failed: {ex.Message}");
            }
        }

        _logger.LogWarning($"Certificate {serialNumber} could not be resolved from cloud storage or local keychain");
        return (null, null);
    }

    /// <summary>
    /// Finds the raw serial segment of a stored CERT_&lt;serial&gt;_P12 secret whose serial
    /// fuzzy-matches the requested one (tolerant of leading-zero / non-alphanumeric differences).
    /// </summary>
    private async Task<(byte[]? P12, string? Password)> FindInOtherProvidersAsync(
        string serialNumber,
        CancellationToken cancellationToken)
    {
        if (_providerRegistry is null)
            return (null, null);

        var activeId = _cloudSecretsService.ActiveProvider?.Id;
        foreach (var source in await GetProviderSourcesAsync())
        {
            if (source.Id == activeId)
                continue;

            try
            {
                var storedSerial = SanitizeSerialNumber(serialNumber);
                var p12 = await source.GetAsync(GetCertificateSecretKey(serialNumber), cancellationToken);
                if (p12 is not { Length: > 0 })
                {
                    var keys = await source.ListAsync($"{SecretPrefix}_", cancellationToken);
                    storedSerial = FindFuzzySerial(keys, serialNumber);
                    if (storedSerial is null)
                        continue;
                    p12 = await source.GetAsync($"{SecretPrefix}_{storedSerial}_P12", cancellationToken);
                    if (p12 is not { Length: > 0 })
                        continue;
                }

                var pwd = await source.GetAsync($"{SecretPrefix}_{storedSerial}_PWD", cancellationToken);
                _logger.LogInformation($"Resolved certificate {serialNumber} from provider '{source.Name}'");
                return (p12, pwd is not null ? Encoding.UTF8.GetString(pwd) : null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Lookup for certificate {serialNumber} in '{source.Name}' failed: {ex.Message}");
            }
        }

        return (null, null);
    }

    private async Task<string?> FindStoredCertSerialAsync(string serialNumber, CancellationToken cancellationToken)
    {
        var keys = await _cloudSecretsService.ListSecretsAsync($"{SecretPrefix}_", cancellationToken);
        return FindFuzzySerial(keys, serialNumber);
    }

    private static string? FindFuzzySerial(IEnumerable<string> keys, string serialNumber)
    {
        foreach (var key in keys)
        {
            if (!key.EndsWith("_P12", StringComparison.OrdinalIgnoreCase))
                continue;

            // Key format: CERT_<serial>_P12 — the serial may itself be empty but never contains '_'
            // because SanitizeSerialNumber strips non-alphanumerics.
            var parts = key.Split('_');
            if (parts.Length < 3)
                continue;
            var rawSerial = string.Join('_', parts[1..^1]);
            if (SerialsFuzzyEqual(rawSerial, serialNumber))
                return rawSerial;
        }
        return null;
    }

    public async Task<bool> DownloadAndInstallAsync(string certificateId, CancellationToken cancellationToken = default)
    {
        if (_cloudSecretsService.ActiveProvider == null)
        {
            _logger.LogWarning("No active cloud secrets provider configured");
            return false;
        }

        if (string.IsNullOrWhiteSpace(certificateId))
        {
            _logger.LogWarning("DownloadAndInstallAsync called with empty certificate ID");
            return false;
        }

        try
        {
            // Resolve certificate ID to serial number via metadata records.
            var keys = await _cloudSecretsService.ListSecretsAsync($"{SecretPrefix}_", cancellationToken);
            var metadataKeys = keys
                .Where(k => k.EndsWith("_META", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var metadataKey in metadataKeys)
            {
                var serial = TryExtractSerialFromMetadataKey(metadataKey);
                if (string.IsNullOrEmpty(serial))
                    continue;

                var metadata = await GetCertificateMetadataAsync(serial, cancellationToken);
                if (metadata == null)
                    continue;

                if (!string.Equals(metadata.CertificateId, certificateId, StringComparison.OrdinalIgnoreCase))
                    continue;

                _logger.LogInformation($"Resolved certificate ID {certificateId} to serial {metadata.SerialNumber}");
                return await DownloadAndInstallBySerialAsync(metadata.SerialNumber, cancellationToken);
            }

            _logger.LogWarning($"Could not resolve certificate ID {certificateId} to cloud metadata");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to resolve certificate ID {certificateId}: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// Downloads and installs a certificate from cloud using its serial number
    /// </summary>
    public async Task<bool> DownloadAndInstallBySerialAsync(
        string serialNumber,
        CancellationToken cancellationToken = default)
    {
        var p12Key = GetCertificateSecretKey(serialNumber);
        var pwdKey = GetCertificatePasswordKey(serialNumber);
        var sources = await GetProviderSourcesAsync();
        if (sources.Count == 0)
        {
            _logger.LogWarning("No secrets provider configured");
            return false;
        }

        // Use the first provider copy that actually carries the private key, so an install
        // never leaves a certificate in the Keychain that can't sign.
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? p12Data;
            byte[]? pwdData;
            try
            {
                p12Data = await source.GetAsync(p12Key, cancellationToken);
                if (p12Data is null)
                    continue;
                pwdData = await source.GetAsync(pwdKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not read certificate {serialNumber} from '{source.Name}': {ex.Message}");
                continue;
            }

            if (pwdData is null)
            {
                _logger.LogWarning($"Certificate {serialNumber} in '{source.Name}' has no stored password");
                continue;
            }

            var password = Encoding.UTF8.GetString(pwdData);
            if (!CertificateBundleUtilities.ContainsPrivateKey(p12Data, password))
            {
                _logger.LogWarning($"Certificate {serialNumber} in '{source.Name}' has no private key; not installing that copy");
                continue;
            }

            _logger.LogInformation($"Installing certificate {serialNumber} from '{source.Name}'");
            try
            {
                if (!await ImportP12LocallyAsync(p12Data, password, cancellationToken))
                {
                    _logger.LogError($"Importing certificate {serialNumber} from '{source.Name}' failed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to install certificate {serialNumber}: {ex.Message}", ex);
                return false;
            }

            return await VerifySigningIdentityInstalledAsync(serialNumber);
        }

        _logger.LogError($"No provider holds certificate {serialNumber} with its private key");
        return false;
    }

    /// <summary>
    /// Confirms the Keychain now has a signing identity (certificate + private key) for the
    /// serial. Only checked where the local certificate service can enumerate identities.
    /// </summary>
    private async Task<bool> VerifySigningIdentityInstalledAsync(string serialNumber)
    {
        if (!_localCertificateService.IsSupported)
            return true;

        _localCertificateService.InvalidateCache();
        if (!await _localCertificateService.HasInstalledIdentityAsync(serialNumber))
        {
            _logger.LogError($"Certificate {serialNumber} was imported but no signing identity with its private key appeared in the Keychain");
            return false;
        }

        var wanted = SanitizeSerialNumber(serialNumber).TrimStart('0');
        var trusted = (await _localCertificateService.GetSigningIdentitiesAsync())
            .Any(candidate => SanitizeSerialNumber(candidate.SerialNumber ?? "").TrimStart('0') == wanted);
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

    private static string SanitizeSerialNumber(string serialNumber)
    {
        // Remove any non-alphanumeric characters and convert to uppercase
        var sb = new StringBuilder();
        foreach (var c in serialNumber)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToUpperInvariant(c));
        }
        // Strip leading zeros — local keychain may include them but API may not
        return sb.ToString().TrimStart('0');
    }

    /// <summary>
    /// Compares two certificate serial numbers tolerantly, ignoring case, non-alphanumeric
    /// characters and leading zeros. Matches the normalization used to build cloud secret keys.
    /// </summary>
    private static bool SerialsFuzzyEqual(string? a, string? b)
        => SanitizeSerialNumber(a ?? string.Empty).Equals(SanitizeSerialNumber(b ?? string.Empty), StringComparison.Ordinal);

    private static string GenerateRandomPassword()
        => Convert.ToBase64String(Guid.NewGuid().ToByteArray())[..16];

    private static string? TryExtractSerialFromMetadataKey(string key)
    {
        // Expected format: CERT_<SERIAL>_META
        var parts = key.Split('_');
        if (parts.Length != 3)
            return null;

        if (!parts[0].Equals(SecretPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!parts[2].Equals("META", StringComparison.OrdinalIgnoreCase))
            return null;

        return parts[1];
    }

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
