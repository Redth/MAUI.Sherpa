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

    private static readonly JsonSerializerOptions PublishProfileJsonOptions = new(JsonSerializerDefaults.Web);

    private record BackupPayload(
        int Version,
        MauiSherpaSettings Settings,
        BackupExportSelection Selection,
        List<SecretItemPayload>? Certificates = null
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
        IEnumerable<ISecretItemAdapter>? secretItemAdapters = null)
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
    }

    public async Task<byte[]> ExportSettingsAsync(string password, BackupExportSelection? selection = null)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password is required", nameof(password));

        var settings = await BuildCurrentSettingsSnapshotAsync();
        var availableCertificates = await GetBackupCertificatesAsync();
        var resolvedSelection = ResolveSelection(
            selection,
            settings,
            availableCertificates.Select(c => c.Id).ToList());
        if (!resolvedSelection.IncludePreferences &&
            resolvedSelection.AppleIdentityIds.Count == 0 &&
            resolvedSelection.CloudProviderIds.Count == 0 &&
            resolvedSelection.SecretsPublisherIds.Count == 0 &&
            resolvedSelection.GoogleIdentityIds.Count == 0 &&
            resolvedSelection.PushProjectIds.Count == 0 &&
            resolvedSelection.PublishProfileIds.Count == 0 &&
            resolvedSelection.CertificateIds.Count == 0)
        {
            throw new InvalidOperationException("At least one setting item must be selected for export.");
        }

        var selectedSettings = ApplySelection(settings, resolvedSelection);
        var certificates = await ReadLocalVaultCertificatesAsync(
            availableCertificates.Where(c => resolvedSelection.CertificateIds.Contains(c.Id)));
        var payload = new BackupPayload(
            Version: BackupPayloadVersion,
            Settings: selectedSettings,
            Selection: resolvedSelection,
            Certificates: certificates);

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
            var resolvedSelection = ResolveSelection(
                payload.Selection,
                payload.Settings,
                certificates.Select(c => c.Item.Id).ToList());
            var selectedSettings = ApplySelection(payload.Settings, resolvedSelection);
            var selectedCertificates = certificates
                .Where(c => resolvedSelection.CertificateIds.Contains(c.Item.Id))
                .ToList();
            return new BackupImportResult(selectedSettings, resolvedSelection, selectedCertificates);
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

    public async Task<IReadOnlyList<SecretItemRef>> GetBackupCertificatesAsync(CancellationToken cancellationToken = default)
    {
        var adapter = GetAdapter(SecretItemKind.Certificate);
        if (adapter is null)
            return Array.Empty<SecretItemRef>();

        try
        {
            return await adapter.ListProviderItemsAsync(CloudSecretsService.DefaultLocalProviderId, cancellationToken);
        }
        catch (Exception)
        {
            // A locked or unavailable Local Vault simply has nothing to back up.
            return Array.Empty<SecretItemRef>();
        }
    }

    public async Task<BackupRestoreResult> RestoreStoredItemsAsync(
        BackupImportResult importResult,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        var profiles = importResult.Settings.PublishProfiles;
        var certificates = importResult.Certificates ?? Array.Empty<SecretItemPayload>();
        if (profiles.Count == 0 && certificates.Count == 0)
            return new BackupRestoreResult(0, 0, failures);

        // Restored items always land in the built-in Local Vault so they are available even
        // when the remote providers they originally came from are unreachable.
        if (_cloudSecretsService is not null)
            await _cloudSecretsService.EnableDefaultLocalProviderAsync(setActiveProvider: false);

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

        return new BackupRestoreResult(profilesRestored, certificatesRestored, failures);
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

    private async Task<List<SecretItemPayload>> ReadLocalVaultCertificatesAsync(IEnumerable<SecretItemRef> certificates)
    {
        var payloads = new List<SecretItemPayload>();
        var adapter = GetAdapter(SecretItemKind.Certificate);
        if (adapter is null)
            return payloads;

        foreach (var certificate in certificates)
        {
            var payload = await adapter.ReadProviderAsync(certificate, CloudSecretsService.DefaultLocalProviderId);
            if (payload is not null)
                payloads.Add(payload);
        }

        return payloads;
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
        IReadOnlyCollection<string> availableCertificateIds)
    {
        var availableIdentityIds = settings.AppleIdentities.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var availableProviderIds = settings.CloudProviders.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var availablePublisherIds = settings.SecretsPublishers.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var availableGoogleIds = settings.GoogleIdentities.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var availablePushProjectIds = settings.PushProjects.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var availableProfileIds = settings.PublishProfiles.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var availableCertIds = availableCertificateIds.ToHashSet(StringComparer.Ordinal);

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
                CertificateIds = availableCertIds.ToList()
            };
        }

        var selectedIdentityIds = selection.AppleIdentityIds ?? new List<string>();
        var selectedProviderIds = selection.CloudProviderIds ?? new List<string>();
        var selectedPublisherIds = selection.SecretsPublisherIds ?? new List<string>();
        var selectedGoogleIds = selection.GoogleIdentityIds ?? new List<string>();
        var selectedPushProjectIds = selection.PushProjectIds ?? new List<string>();
        var selectedProfileIds = selection.PublishProfileIds ?? new List<string>();
        var selectedCertIds = selection.CertificateIds ?? new List<string>();

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
