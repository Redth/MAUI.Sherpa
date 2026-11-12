using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Backup service for password-protected export/import of settings
/// Uses PBKDF2 for key derivation and AES-256-GCM for encryption
/// </summary>
public class BackupService : IBackupService
{
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private const int BackupPayloadVersion = 2;
    private static readonly byte[] MagicHeader = "MSSBAK01"u8.ToArray();
    
    private readonly IEncryptedSettingsService _settingsService;
    private readonly IAppleIdentityService? _appleIdentityService;
    private readonly ICloudSecretsService? _cloudSecretsService;
    private readonly ISecretsPublisherService? _secretsPublisherService;
    private readonly IGoogleIdentityService? _googleIdentityService;
    private readonly IPushProjectService? _pushProjectService;
    private readonly IPublishProfileService? _publishProfileService;
    private readonly ISecretsProviderRegistry? _providerRegistry;
    private readonly IReadOnlyList<ISecretItemAdapter> _secretItemAdapters;
    private readonly ILocalCertificateService? _localCertificateService;
    private readonly IKeystoreService? _keystoreService;
    private readonly ILoggingService? _logger;

    private static readonly JsonSerializerOptions PublishProfileJsonOptions = new(JsonSerializerDefaults.Web);

    private record BackupPayload(
        int Version,
        MauiSherpaSettings Settings,
        BackupExportSelection Selection,
        List<SecretItemPayload>? Certificates = null,
        List<KeystoreBackupItem>? Keystores = null
    );

    public BackupService(
        IEncryptedSettingsService settingsService,
        IAppleIdentityService? appleIdentityService = null,
        ICloudSecretsService? cloudSecretsService = null,
        ISecretsPublisherService? secretsPublisherService = null,
        IGoogleIdentityService? googleIdentityService = null,
        IPushProjectService? pushProjectService = null,
        IPublishProfileService? publishProfileService = null,
        ISecretsProviderRegistry? providerRegistry = null,
        IEnumerable<ISecretItemAdapter>? secretItemAdapters = null,
        ILocalCertificateService? localCertificateService = null,
        ILoggingService? logger = null,
        IKeystoreService? keystoreService = null)
    {
        _settingsService = settingsService;
        _appleIdentityService = appleIdentityService;
        _cloudSecretsService = cloudSecretsService;
        _secretsPublisherService = secretsPublisherService;
        _googleIdentityService = googleIdentityService;
        _pushProjectService = pushProjectService;
        _publishProfileService = publishProfileService;
        _providerRegistry = providerRegistry;
        _secretItemAdapters = secretItemAdapters?.ToList() ?? new List<ISecretItemAdapter>();
        _localCertificateService = localCertificateService;
        _keystoreService = keystoreService;
        _logger = logger;
    }

    public async Task<byte[]> ExportSettingsAsync(string password, BackupExportSelection? selection = null)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password is required", nameof(password));

        var settings = await BuildCurrentSettingsSnapshotAsync();
        var discovery = selection is null || selection.CertificateIds is { Count: > 0 }
            ? await GetBackupCertificatesAsync()
            : new BackupCertificateDiscoveryResult(
                Array.Empty<BackupCertificateInfo>(),
                Array.Empty<string>());
        var availableCertificates = discovery.Certificates;
        if (selection is null && discovery.Errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Could not list all signing certificates. " + string.Join(" ", discovery.Errors));
        }

        if (selection?.CertificateIds is { Count: > 0 } selectedCertificateIds)
        {
            var availableIds = availableCertificates.Select(c => c.Item.Id).ToHashSet(StringComparer.Ordinal);
            var missingIds = selectedCertificateIds
                .Where(id => !availableIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (missingIds.Count > 0)
            {
                var details = discovery.Errors.Count > 0 ? " " + string.Join(" ", discovery.Errors) : string.Empty;
                throw new InvalidOperationException(
                    "The selected signing certificates are no longer available: " +
                    string.Join(", ", missingIds) + "." + details +
                    " Refresh the certificate list or deselect them.");
            }
        }

        var availableKeystores = _keystoreService is null
            ? Array.Empty<AndroidKeystore>()
            : await _keystoreService.ListKeystoresAsync();
        var resolvedSelection = ResolveSelection(
            selection,
            settings,
            availableCertificates.Select(c => c.Item.Id).ToList(),
            availableKeystores.Select(k => k.Id).ToList());
        if (!resolvedSelection.IncludePreferences &&
            resolvedSelection.AppleIdentityIds.Count == 0 &&
            resolvedSelection.CloudProviderIds.Count == 0 &&
            resolvedSelection.SecretsPublisherIds.Count == 0 &&
            resolvedSelection.GoogleIdentityIds.Count == 0 &&
            resolvedSelection.PushProjectIds.Count == 0 &&
            resolvedSelection.PublishProfileIds.Count == 0 &&
            resolvedSelection.CertificateIds.Count == 0 &&
            resolvedSelection.KeystoreIds.Count == 0)
        {
            throw new InvalidOperationException("At least one setting item must be selected for export.");
        }

        var selectedSettings = ApplySelection(settings, resolvedSelection);
        var certificates = await ReadCertificatesWithPrivateKeysAsync(
            availableCertificates.Where(c => resolvedSelection.CertificateIds.Contains(c.Item.Id)).ToList());
        var keystores = await ReadKeystoresAsync(
            availableKeystores.Where(k => resolvedSelection.KeystoreIds.Contains(k.Id)).ToList());
        var payload = new BackupPayload(
            Version: BackupPayloadVersion,
            Settings: selectedSettings,
            Selection: resolvedSelection,
            Certificates: certificates,
            Keystores: keystores);

        var json = JsonSerializer.Serialize(payload);
        var plaintext = Encoding.UTF8.GetBytes(json);

        // Generate salt and derive key
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(password, salt);
        
        // Encrypt with AES-GCM
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Format: [magic 8][salt 32][nonce 12][tag 16][ciphertext]
        var result = new byte[MagicHeader.Length + SaltSize + NonceSize + TagSize + ciphertext.Length];
        var offset = 0;
        
        Buffer.BlockCopy(MagicHeader, 0, result, offset, MagicHeader.Length);
        offset += MagicHeader.Length;
        
        Buffer.BlockCopy(salt, 0, result, offset, SaltSize);
        offset += SaltSize;
        
        Buffer.BlockCopy(nonce, 0, result, offset, NonceSize);
        offset += NonceSize;
        
        Buffer.BlockCopy(tag, 0, result, offset, TagSize);
        offset += TagSize;
        
        Buffer.BlockCopy(ciphertext, 0, result, offset, ciphertext.Length);

        return result;
    }

    public async Task<BackupImportResult> ImportBackupAsync(byte[] encryptedData, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password is required", nameof(password));

        if (!await ValidateBackupAsync(encryptedData))
            throw new InvalidOperationException("Invalid backup file format");

        var minLength = MagicHeader.Length + SaltSize + NonceSize + TagSize;
        if (encryptedData.Length < minLength)
            throw new InvalidOperationException("Backup file is too small");

        var offset = MagicHeader.Length;
        
        var salt = new byte[SaltSize];
        Buffer.BlockCopy(encryptedData, offset, salt, 0, SaltSize);
        offset += SaltSize;
        
        var nonce = new byte[NonceSize];
        Buffer.BlockCopy(encryptedData, offset, nonce, 0, NonceSize);
        offset += NonceSize;
        
        var tag = new byte[TagSize];
        Buffer.BlockCopy(encryptedData, offset, tag, 0, TagSize);
        offset += TagSize;
        
        var ciphertext = new byte[encryptedData.Length - offset];
        Buffer.BlockCopy(encryptedData, offset, ciphertext, 0, ciphertext.Length);

        // Derive key and decrypt
        var key = DeriveKey(password, salt);
        var plaintext = new byte[ciphertext.Length];
        
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        var json = Encoding.UTF8.GetString(plaintext);
        var payload = JsonSerializer.Deserialize<BackupPayload>(json);
        if (payload?.Settings != null && payload.Selection != null)
        {
            var certificates = payload.Certificates ?? new List<SecretItemPayload>();
            var keystores = payload.Keystores ?? new List<KeystoreBackupItem>();
            var resolvedSelection = ResolveSelection(
                payload.Selection,
                payload.Settings,
                certificates.Select(c => c.Item.Id).ToList(),
                keystores.Select(k => k.Id).ToList());
            var selectedSettings = ApplySelection(payload.Settings, resolvedSelection);
            var selectedCertificates = certificates
                .Where(c => resolvedSelection.CertificateIds.Contains(c.Item.Id))
                .ToList();
            var selectedKeystores = keystores
                .Where(k => resolvedSelection.KeystoreIds.Contains(k.Id))
                .ToList();
            return new BackupImportResult(selectedSettings, resolvedSelection, selectedCertificates, selectedKeystores);
        }

        var settings = JsonSerializer.Deserialize<MauiSherpaSettings>(json) 
            ?? throw new InvalidOperationException("Failed to deserialize settings");
        var legacySelection = new BackupExportSelection
        {
            IncludePreferences = true,
            AppleIdentityIds = settings.AppleIdentities.Select(i => i.Id).ToList(),
            CloudProviderIds = settings.CloudProviders.Select(p => p.Id).ToList(),
            SecretsPublisherIds = settings.SecretsPublishers.Select(p => p.Id).ToList(),
            GoogleIdentityIds = settings.GoogleIdentities.Select(g => g.Id).ToList(),
            PushProjectIds = settings.PushProjects.Select(p => p.Id).ToList()
        };

        return new BackupImportResult(settings, legacySelection);
    }

    public async Task<MauiSherpaSettings> ImportSettingsAsync(byte[] encryptedData, string password)
    {
        var importResult = await ImportBackupAsync(encryptedData, password);
        return importResult.Settings;
    }

    public Task<bool> ValidateBackupAsync(byte[] data)
    {
        if (data == null || data.Length < MagicHeader.Length)
            return Task.FromResult(false);

        for (int i = 0; i < MagicHeader.Length; i++)
        {
            if (data[i] != MagicHeader[i])
                return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public async Task<BackupCertificateDiscoveryResult> GetBackupCertificatesAsync(CancellationToken cancellationToken = default)
    {
        var vaultItems = new List<SecretItemRef>();
        var errors = new List<string>();
        var adapter = GetAdapter(SecretItemKind.Certificate);
        if (adapter is not null)
        {
            try
            {
                vaultItems.AddRange(await adapter.ListProviderItemsAsync(
                    CloudSecretsService.DefaultLocalProviderId,
                    cancellationToken));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                var message = $"Could not list signing certificates in Local Vault: {ex.Message}";
                errors.Add(message);
                _logger?.LogWarning(message);
            }
        }

        var keychainItems = new List<SecretItemRef>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localDiscovery = await GetKeychainIdentitiesAsync();
            errors.AddRange(localDiscovery.Errors.Select(error => $"Could not list all local signing certificates: {error}"));
            foreach (var identity in localDiscovery.Identities)
            {
                keychainItems.Add(new SecretItemRef(
                    SecretItemKind.Certificate,
                    SecretItemAdapterHelper.SanitizeSerialNumber(identity.SerialNumber!),
                    identity.CommonName));
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            var message = $"Could not list signing certificates in Keychain: {ex.Message}";
            errors.Add(message);
            _logger?.LogWarning(message);
        }

        var vaultIds = vaultItems.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var keychainById = keychainItems
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var certificates = vaultItems
            .Select(item => keychainById.TryGetValue(item.Id, out var keychainItem)
                // Keychain names are the friendly common name; vault names may be bare serials.
                ? new BackupCertificateInfo(item with { DisplayName = keychainItem.DisplayName }, true, true)
                : new BackupCertificateInfo(item, true, false))
            .Concat(keychainById.Values
                .Where(item => !vaultIds.Contains(item.Id))
                .Select(item => new BackupCertificateInfo(item, false, true)))
            .OrderBy(info => info.Item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new BackupCertificateDiscoveryResult(certificates, errors);
    }

    public async Task<BackupRestoreResult> RestoreStoredItemsAsync(
        BackupImportResult importResult,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        var profiles = importResult.Settings.PublishProfiles;
        var certificates = importResult.Certificates ?? Array.Empty<SecretItemPayload>();
        var keystores = importResult.Keystores ?? Array.Empty<KeystoreBackupItem>();
        if (profiles.Count == 0 && certificates.Count == 0 && keystores.Count == 0)
            return new BackupRestoreResult(0, 0, failures);

        // Restored items always land in the built-in Local Vault so they are available even
        // when the remote providers they originally came from are unreachable.
        if (_cloudSecretsService is not null && (profiles.Count > 0 || certificates.Count > 0))
        {
            try
            {
                await _cloudSecretsService.EnableDefaultLocalProviderAsync(setActiveProvider: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.AddRange(profiles.Select(p => $"Publish profile '{p.Name}' (Local Vault unavailable: {ex.Message})"));
                failures.AddRange(certificates.Select(c => $"Certificate '{c.Item.DisplayName}' (Local Vault unavailable: {ex.Message})"));
                profiles = new List<PublishProfileData>();
                certificates = Array.Empty<SecretItemPayload>();
            }
        }

        var profilesRestored = 0;
        var profileAdapter = GetAdapter(SecretItemKind.PublishProfile);
        foreach (var profile in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (profileAdapter is not null &&
                await TryWriteLocalAsync(profileAdapter, CreatePublishProfilePayload(profile), cancellationToken))
            {
                profilesRestored++;
            }
            else
            {
                failures.Add($"Publish profile '{profile.Name}'");
            }
        }

        var certificatesRestored = 0;
        var certificateAdapter = GetAdapter(SecretItemKind.Certificate);
        foreach (var certificate in certificates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasPrivateKey(certificate))
            {
                failures.Add($"Certificate '{certificate.Item.DisplayName}' (backup has no matching private key)");
                continue;
            }

            if (certificateAdapter is not null &&
                await TryWriteLocalAsync(certificateAdapter, certificate, cancellationToken))
            {
                certificatesRestored++;
            }
            else
            {
                failures.Add($"Certificate '{certificate.Item.DisplayName}'");
            }
        }

        var keystoresRestored = 0;
        foreach (var keystore in keystores)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_keystoreService is null)
            {
                failures.Add($"Keystore '{keystore.Alias}' (keystore service unavailable)");
                continue;
            }

            try
            {
                await RestoreKeystoreAsync(_keystoreService, keystore, cancellationToken);
                keystoresRestored++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning($"Could not restore keystore '{keystore.Alias}': {ex.Message}");
                failures.Add($"Keystore '{keystore.Alias}' ({ex.Message})");
            }
        }

        return new BackupRestoreResult(profilesRestored, certificatesRestored, failures, keystoresRestored);
    }

    /// <summary>
    /// Reads each selected keystore from the Local Vault with its saved passwords. Export fails
    /// rather than writing a backup that is missing a keystore that was asked for.
    /// </summary>
    private async Task<List<KeystoreBackupItem>> ReadKeystoresAsync(IReadOnlyList<AndroidKeystore> keystores)
    {
        var items = new List<KeystoreBackupItem>();
        if (_keystoreService is null || keystores.Count == 0)
            return items;

        var failures = new List<string>();
        foreach (var keystore in keystores)
        {
            byte[]? content;
            try
            {
                content = await _keystoreService.GetKeystoreContentAsync(keystore.Id);
            }
            catch (Exception ex)
            {
                failures.Add($"{keystore.Alias} ({ex.Message})");
                continue;
            }

            if (content is null)
            {
                failures.Add($"{keystore.Alias} (the keystore file has not been located)");
                continue;
            }

            items.Add(new KeystoreBackupItem(
                Id: keystore.Id,
                Alias: keystore.Alias,
                KeystoreType: keystore.KeystoreType,
                CreatedDate: keystore.CreatedDate,
                Notes: keystore.Notes,
                FileName: Path.GetFileName(keystore.FilePath),
                Content: content,
                KeystorePassword: await _keystoreService.GetKeystorePasswordAsync(keystore.Id),
                KeyPassword: await _keystoreService.GetKeyPasswordAsync(keystore.Id)));
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "The keystore could not be read for: " + string.Join(", ", failures) +
                ". Locate the keystore files or deselect them.");
        }

        return items;
    }

    /// <summary>
    /// Stores a backed-up keystore in the Local Vault under its original ID, so publish profiles
    /// that point at it keep working. A keystore already in the vault keeps its file and only
    /// gains the passwords it is missing.
    /// </summary>
    private static async Task RestoreKeystoreAsync(
        IKeystoreService keystoreService,
        KeystoreBackupItem item,
        CancellationToken cancellationToken)
    {
        var existing = (await keystoreService.ListKeystoresAsync())
            .FirstOrDefault(k => string.Equals(k.Id, item.Id, StringComparison.Ordinal));
        cancellationToken.ThrowIfCancellationRequested();

        if (existing is { StoredInVault: true })
        {
            if (item.KeystorePassword is not null && await keystoreService.GetKeystorePasswordAsync(item.Id) is null)
                await keystoreService.SaveKeystorePasswordAsync(item.Id, item.KeystorePassword);
            if (item.KeyPassword is not null && await keystoreService.GetKeyPasswordAsync(item.Id) is null)
                await keystoreService.SaveKeyPasswordAsync(item.Id, item.KeyPassword);
            return;
        }

        await keystoreService.AddKeystoreAsync(
            new AndroidKeystore(
                Id: item.Id,
                Alias: item.Alias,
                FilePath: item.FileName,
                KeystoreType: item.KeystoreType,
                CreatedDate: item.CreatedDate,
                Notes: item.Notes),
            item.Content,
            item.KeystorePassword,
            item.KeyPassword);
    }

    private ISecretItemAdapter? GetAdapter(SecretItemKind kind) =>
        _providerRegistry is null
            ? null
            : _secretItemAdapters.FirstOrDefault(adapter => adapter.Kind == kind);

    private static async Task<bool> TryWriteLocalAsync(
        ISecretItemAdapter adapter,
        SecretItemPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            return await adapter.WriteProviderAsync(
                payload,
                CloudSecretsService.DefaultLocalProviderId,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads each selected certificate as a complete P12 (certificate + private key + password).
    /// The Local Vault copy is used when it holds a private key; otherwise the identity is
    /// exported from the Keychain. Export fails rather than silently writing a backup that
    /// could not restore a working signing identity.
    /// </summary>
    private async Task<List<SecretItemPayload>> ReadCertificatesWithPrivateKeysAsync(
        IReadOnlyList<BackupCertificateInfo> certificates)
    {
        var payloads = new List<SecretItemPayload>();
        var needKeychain = new List<BackupCertificateInfo>();
        var failures = new List<string>();
        var adapter = GetAdapter(SecretItemKind.Certificate);

        foreach (var certificate in certificates)
        {
            SecretItemPayload? payload = null;
            string? vaultError = null;
            if (certificate.InLocalVault && adapter is not null)
            {
                try
                {
                    payload = await adapter.ReadProviderAsync(
                        certificate.Item,
                        CloudSecretsService.DefaultLocalProviderId);
                }
                catch (Exception ex)
                {
                    vaultError = ex.Message;
                    _logger?.LogWarning($"Could not read certificate '{certificate.Item.DisplayName}' from Local Vault: {ex.Message}");
                }
            }

            if (payload is not null &&
                (payload.Item.Kind != SecretItemKind.Certificate ||
                 !string.Equals(payload.Item.Id, certificate.Item.Id, StringComparison.Ordinal)))
            {
                vaultError = "The stored certificate does not match the selected certificate";
                _logger?.LogWarning($"{vaultError}: '{certificate.Item.DisplayName}'.");
                payload = null;
            }

            if (payload is not null && HasPrivateKey(payload))
                payloads.Add(payload);
            else if (certificate.InKeychain)
                needKeychain.Add(certificate);
            else
                failures.Add(vaultError is null
                    ? certificate.Item.DisplayName
                    : $"{certificate.Item.DisplayName} ({vaultError})");
        }

        if (needKeychain.Count > 0)
            payloads.AddRange(await ExportKeychainCertificatesAsync(needKeychain, failures));

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "The private key could not be included for: " + string.Join(", ", failures) +
                ". Install these certificates with their private keys in the Keychain or Local Vault, or deselect them.");
        }

        return payloads;
    }

    private async Task<List<SecretItemPayload>> ExportKeychainCertificatesAsync(
        IReadOnlyList<BackupCertificateInfo> certificates,
        List<string> failures)
    {
        var payloads = new List<SecretItemPayload>();
        var localDiscovery = await GetKeychainIdentitiesAsync();
        var identitiesById = localDiscovery.Identities
            .GroupBy(identity => SecretItemAdapterHelper.SanitizeSerialNumber(identity.SerialNumber!), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var selected = new List<(BackupCertificateInfo Certificate, LocalSigningIdentity Identity)>();
        foreach (var certificate in certificates)
        {
            if (identitiesById.TryGetValue(certificate.Item.Id, out var identity))
                selected.Add((certificate, identity));
            else
                failures.Add(localDiscovery.Errors.Count == 0
                    ? certificate.Item.DisplayName
                    : $"{certificate.Item.DisplayName} ({string.Join(" ", localDiscovery.Errors)})");
        }

        if (selected.Count == 0)
            return payloads;

        // One Keychain export for every identity, so macOS asks for access once rather than
        // once per certificate, then split it into a P12 per certificate.
        byte[] bundle;
        var bundlePassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        try
        {
            bundle = await _localCertificateService!.ExportP12Async(
                selected.Select(s => s.Identity).ToList(),
                bundlePassword);
        }
        catch (Exception ex)
        {
            failures.AddRange(selected.Select(s => $"{s.Certificate.Item.DisplayName} ({ex.Message})"));
            return payloads;
        }

        foreach (var (certificate, identity) in selected)
        {
            try
            {
                var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
                var p12 = CertificateBundleUtilities.ExportSelectedIdentities(
                    bundle,
                    bundlePassword,
                    new[] { identity },
                    password);
                if (!CertificateBundleUtilities.ContainsPrivateKey(p12, password, certificate.Item.Id))
                    throw new InvalidOperationException("The exported identity does not match the selected certificate.");
                payloads.Add(CreateCertificatePayload(identity, p12, password));
            }
            catch (Exception ex)
            {
                failures.Add($"{certificate.Item.DisplayName} ({ex.Message})");
            }
        }

        return payloads;
    }

    private async Task<LocalSigningIdentityDiscoveryResult> GetKeychainIdentitiesAsync()
    {
        if (_localCertificateService is null)
            return new(Array.Empty<LocalSigningIdentity>(), Array.Empty<string>());

        var discovery = await _localCertificateService.GetSigningIdentityDiscoveryAsync();
        var identities = discovery.Identities
            .Where(identity => identity.IsValid && !string.IsNullOrWhiteSpace(identity.SerialNumber))
            .Where(identity => SecretItemAdapterHelper.SanitizeSerialNumber(identity.SerialNumber!).Length > 0)
            .ToList();
        return new(identities, discovery.Errors);
    }

    private static SecretItemPayload CreateCertificatePayload(LocalSigningIdentity identity, byte[] p12, string password)
    {
        var id = SecretItemAdapterHelper.SanitizeSerialNumber(identity.SerialNumber!);
        var separator = identity.CommonName.IndexOf(':');
        var metadata = new CertificateSecretMetadata(
            string.Empty,
            identity.SerialNumber!,
            identity.CommonName,
            separator > 0 ? identity.CommonName[..separator].Trim() : string.Empty,
            identity.ExpirationDate ?? DateTime.MinValue,
            Environment.MachineName,
            DateTime.UtcNow);

        // Same artifact layout CertificateItemAdapter uses, so the restored copy is a normal
        // Local Vault certificate.
        return SecretItemAdapterHelper.CreatePayload(
            new SecretItemRef(SecretItemKind.Certificate, id, identity.CommonName),
            0,
            new[]
            {
                new SecretArtifact($"CERT_{id}_P12", p12),
                new SecretArtifact($"CERT_{id}_PWD", Encoding.UTF8.GetBytes(password)),
                new SecretArtifact($"CERT_{id}_META", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata)))
            });
    }

    /// <summary>
    /// True when a certificate payload carries a P12 that opens with its stored password and
    /// includes the selected certificate's private key.
    /// </summary>
    internal static bool HasPrivateKey(SecretItemPayload payload)
    {
        var p12 = payload.Artifacts.FirstOrDefault(a => a.Key.EndsWith("_P12", StringComparison.OrdinalIgnoreCase));
        var password = payload.Artifacts.FirstOrDefault(a => a.Key.EndsWith("_PWD", StringComparison.OrdinalIgnoreCase));
        return CertificateBundleUtilities.ContainsPrivateKey(
            p12?.Value,
            password is null ? string.Empty : Encoding.UTF8.GetString(password.Value),
            payload.Item.Id);
    }

    private static SecretItemPayload CreatePublishProfilePayload(PublishProfileData data)
    {
        var updatedAt = data.UpdatedAt ?? DateTime.UtcNow;
        var profile = new PublishProfile(
            Id: data.Id,
            Name: data.Name,
            Description: data.Description,
            PublisherId: data.PublisherId,
            RepositoryId: data.RepositoryId,
            RepositoryFullName: data.RepositoryFullName,
            AppleConfigs: data.AppleConfigs,
            AndroidConfigs: data.AndroidConfigs,
            SecretMappings: data.SecretMappings,
            CreatedAt: data.CreatedAt ?? updatedAt,
            UpdatedAt: updatedAt)
        {
            Publishers = data.Publishers,
            AppleIdentities = data.AppleIdentities,
            GoogleIdentities = data.GoogleIdentities
        };

        var artifact = new SecretArtifact(
            PublishProfileItemAdapter.KeyPrefix + profile.Id,
            JsonSerializer.SerializeToUtf8Bytes(profile, PublishProfileJsonOptions));
        return SecretItemAdapterHelper.CreatePayload(
            new SecretItemRef(SecretItemKind.PublishProfile, profile.Id, profile.Name),
            SecretItemAdapterHelper.GetUtcRevision(updatedAt),
            new[] { artifact });
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password, 
            salt, 
            Iterations, 
            HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }

    private async Task<MauiSherpaSettings> BuildCurrentSettingsSnapshotAsync()
    {
        var settings = await _settingsService.GetSettingsAsync();
        settings = await HydrateAppleIdentitiesAsync(settings);
        settings = await HydrateCloudProvidersAsync(settings);
        settings = await HydrateSecretsPublishersAsync(settings);
        settings = await HydrateGoogleIdentitiesAsync(settings);
        settings = await HydratePushProjectsAsync(settings);
        settings = await HydratePublishProfilesAsync(settings);
        return settings;
    }

    private async Task<MauiSherpaSettings> HydratePublishProfilesAsync(MauiSherpaSettings settings)
    {
        if (_publishProfileService is null)
            return settings;

        var profiles = await _publishProfileService.GetProfilesAsync();
        if (profiles.Count == 0)
            return settings;

        return settings with
        {
            PublishProfiles = profiles
                .Select(p => new PublishProfileData(
                    Id: p.Id,
                    Name: p.Name,
                    Description: p.Description,
                    PublisherId: p.PublisherId,
                    RepositoryId: p.RepositoryId,
                    RepositoryFullName: p.RepositoryFullName,
                    AppleConfigs: p.AppleConfigs,
                    AndroidConfigs: p.AndroidConfigs,
                    SecretMappings: p.SecretMappings)
                {
                    Publishers = p.Publishers,
                    AppleIdentities = p.AppleIdentities,
                    GoogleIdentities = p.GoogleIdentities,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt
                })
                .ToList()
        };
    }

    private async Task<MauiSherpaSettings> HydrateAppleIdentitiesAsync(MauiSherpaSettings settings)
    {
        if (_appleIdentityService is null)
            return settings;

        var identities = await _appleIdentityService.GetIdentitiesAsync();
        if (identities.Count == 0)
            return settings;

        var createdAtById = settings.AppleIdentities
            .GroupBy(i => i.Id)
            .ToDictionary(g => g.Key, g => g.First().CreatedAt);

        return settings with
        {
            AppleIdentities = identities
                .Select(identity => new AppleIdentityData(
                    Id: identity.Id,
                    Name: identity.Name,
                    KeyId: identity.KeyId,
                    IssuerId: identity.IssuerId,
                    P8Content: identity.P8KeyContent ?? string.Empty,
                    CreatedAt: createdAtById.TryGetValue(identity.Id, out var createdAt)
                        ? createdAt
                        : DateTime.UtcNow))
                .ToList()
        };
    }

    private async Task<MauiSherpaSettings> HydrateCloudProvidersAsync(MauiSherpaSettings settings)
    {
        if (_cloudSecretsService is null)
            return settings;

        await _cloudSecretsService.InitializeAsync();
        var providers = await _cloudSecretsService.GetProvidersAsync();
        if (providers.Count == 0)
            return settings;

        var activeProviderId = _cloudSecretsService.ActiveProvider?.Id;
        return settings with
        {
            CloudProviders = providers
                .Select(provider => new CloudProviderData(
                    Id: provider.Id,
                    Name: provider.Name,
                    ProviderType: provider.ProviderType,
                    Settings: new Dictionary<string, string>(provider.Settings),
                    IsActive: provider.Id == activeProviderId,
                    DefaultItemKinds: provider.EffectiveDefaultItemKinds.ToList()))
                .ToList(),
            ActiveCloudProviderId = activeProviderId
        };
    }

    private async Task<MauiSherpaSettings> HydrateSecretsPublishersAsync(MauiSherpaSettings settings)
    {
        if (_secretsPublisherService is null)
            return settings;

        var publishers = await _secretsPublisherService.GetPublishersAsync();
        if (publishers.Count == 0)
            return settings;

        return settings with
        {
            SecretsPublishers = publishers
                .Select(publisher => new SecretsPublisherData(
                    Id: publisher.Id,
                    ProviderId: publisher.ProviderId,
                    Name: publisher.Name,
                    Settings: new Dictionary<string, string>(publisher.Settings)))
                .ToList()
        };
    }

    private async Task<MauiSherpaSettings> HydrateGoogleIdentitiesAsync(MauiSherpaSettings settings)
    {
        if (_googleIdentityService is null)
            return settings;

        var identities = await _googleIdentityService.GetIdentitiesAsync();
        if (identities.Count == 0)
            return settings;

        return settings with
        {
            GoogleIdentities = identities
                .Select(identity => new GoogleIdentityData(
                    Id: identity.Id,
                    Name: identity.Name,
                    ProjectId: identity.ProjectId,
                    ClientEmail: identity.ClientEmail,
                    ServiceAccountJson: identity.ServiceAccountJson))
                .ToList()
        };
    }

    private async Task<MauiSherpaSettings> HydratePushProjectsAsync(MauiSherpaSettings settings)
    {
        if (_pushProjectService is null)
            return settings;

        var projects = await _pushProjectService.GetProjectsAsync();
        if (projects.Count == 0)
            return settings;

        // Strip history — only back up project settings, not send history
        return settings with
        {
            PushProjects = projects
                .Select(p => p with { History = new List<PushSendHistoryEntry>() })
                .ToList()
        };
    }

    private static BackupExportSelection ResolveSelection(
        BackupExportSelection? selection,
        MauiSherpaSettings settings,
        IReadOnlyCollection<string> availableCertificateIds,
        IReadOnlyCollection<string> availableKeystoreIds)
    {
        var availableIdentityIds = settings.AppleIdentities.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var availableProviderIds = settings.CloudProviders.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var availablePublisherIds = settings.SecretsPublishers.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var availableGoogleIds = settings.GoogleIdentities.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var availablePushProjectIds = settings.PushProjects.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var availableProfileIds = settings.PublishProfiles.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var availableCertIds = availableCertificateIds.ToHashSet(StringComparer.Ordinal);
        var availableKeystoreIdSet = availableKeystoreIds.ToHashSet(StringComparer.Ordinal);

        if (selection is null)
        {
            return new BackupExportSelection
            {
                IncludePreferences = true,
                AppleIdentityIds = availableIdentityIds.ToList(),
                CloudProviderIds = availableProviderIds.ToList(),
                SecretsPublisherIds = availablePublisherIds.ToList(),
                GoogleIdentityIds = availableGoogleIds.ToList(),
                PushProjectIds = availablePushProjectIds.ToList(),
                PublishProfileIds = availableProfileIds.ToList(),
                CertificateIds = availableCertIds.ToList(),
                KeystoreIds = availableKeystoreIdSet.ToList()
            };
        }

        var selectedIdentityIds = selection.AppleIdentityIds ?? new List<string>();
        var selectedProviderIds = selection.CloudProviderIds ?? new List<string>();
        var selectedPublisherIds = selection.SecretsPublisherIds ?? new List<string>();
        var selectedGoogleIds = selection.GoogleIdentityIds ?? new List<string>();
        var selectedPushProjectIds = selection.PushProjectIds ?? new List<string>();
        var selectedProfileIds = selection.PublishProfileIds ?? new List<string>();
        var selectedCertIds = selection.CertificateIds ?? new List<string>();
        var selectedKeystoreIds = selection.KeystoreIds ?? new List<string>();

        return new BackupExportSelection
        {
            IncludePreferences = selection.IncludePreferences,
            AppleIdentityIds = selectedIdentityIds
                .Where(id => availableIdentityIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            CloudProviderIds = selectedProviderIds
                .Where(id => availableProviderIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            SecretsPublisherIds = selectedPublisherIds
                .Where(id => availablePublisherIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            GoogleIdentityIds = selectedGoogleIds
                .Where(id => availableGoogleIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            PushProjectIds = selectedPushProjectIds
                .Where(id => availablePushProjectIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            PublishProfileIds = selectedProfileIds
                .Where(id => availableProfileIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            CertificateIds = selectedCertIds
                .Where(id => availableCertIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            KeystoreIds = selectedKeystoreIds
                .Where(id => availableKeystoreIdSet.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };
    }

    private static MauiSherpaSettings ApplySelection(MauiSherpaSettings settings, BackupExportSelection selection)
    {
        var selectedIdentityIds = selection.AppleIdentityIds.ToHashSet(StringComparer.Ordinal);
        var selectedProviderIds = selection.CloudProviderIds.ToHashSet(StringComparer.Ordinal);
        var selectedPublisherIds = selection.SecretsPublisherIds.ToHashSet(StringComparer.Ordinal);
        var selectedGoogleIds = selection.GoogleIdentityIds.ToHashSet(StringComparer.Ordinal);
        var selectedPushProjectIds = selection.PushProjectIds.ToHashSet(StringComparer.Ordinal);
        var selectedProfileIds = selection.PublishProfileIds.ToHashSet(StringComparer.Ordinal);

        var cloudProviders = settings.CloudProviders
            .Where(provider => selectedProviderIds.Contains(provider.Id))
            .ToList();
        var activeCloudProviderId = !string.IsNullOrEmpty(settings.ActiveCloudProviderId) &&
                                    selectedProviderIds.Contains(settings.ActiveCloudProviderId)
            ? settings.ActiveCloudProviderId
            : null;

        cloudProviders = cloudProviders
            .Select(provider => provider with { IsActive = provider.Id == activeCloudProviderId })
            .ToList();

        return settings with
        {
            Preferences = selection.IncludePreferences ? settings.Preferences : new AppPreferences(),
            AppleIdentities = settings.AppleIdentities
                .Where(identity => selectedIdentityIds.Contains(identity.Id))
                .ToList(),
            CloudProviders = cloudProviders,
            ActiveCloudProviderId = activeCloudProviderId,
            SecretsPublishers = settings.SecretsPublishers
                .Where(publisher => selectedPublisherIds.Contains(publisher.Id))
                .ToList(),
            GoogleIdentities = settings.GoogleIdentities
                .Where(identity => selectedGoogleIds.Contains(identity.Id))
                .ToList(),
            PushProjects = settings.PushProjects
                .Where(project => selectedPushProjectIds.Contains(project.Id))
                .ToList(),
            PublishProfiles = settings.PublishProfiles
                .Where(profile => selectedProfileIds.Contains(profile.Id))
                .ToList()
        };
    }
}
