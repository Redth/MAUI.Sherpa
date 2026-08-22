using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Service for migrating settings from legacy storage formats
/// </summary>
public class SettingsMigrationService : ISettingsMigrationService
{
    private readonly IEncryptedSettingsService _encryptedSettings;
    private readonly IAppleIdentityService _appleIdentityService;
    private readonly ICloudSecretsService _cloudSecrets;
    private readonly ISecretsPublisherService _publisherService;
    private readonly ILoggingService _logger;

    public SettingsMigrationService(
        IEncryptedSettingsService encryptedSettings,
        IAppleIdentityService appleIdentityService,
        ICloudSecretsService cloudSecrets,
        ISecretsPublisherService publisherService,
        ILoggingService logger)
    {
        _encryptedSettings = encryptedSettings;
        _appleIdentityService = appleIdentityService;
        _cloudSecrets = cloudSecrets;
        _publisherService = publisherService;
        _logger = logger;
    }

    public async Task<bool> NeedsMigrationAsync()
    {
        // Check if encrypted settings exist
        if (await _encryptedSettings.SettingsExistAsync())
        {
            var settings = await _encryptedSettings.GetSettingsAsync();
            // If we have data, no migration needed
            if (settings.AppleIdentities.Any() || 
                settings.CloudProviders.Any() || 
                settings.SecretsPublishers.Any())
            {
                return false;
            }
        }

        // Check if legacy data exists
        var legacyIdentities = await _appleIdentityService.GetIdentitiesAsync();
        var legacyProviders = await _cloudSecrets.GetProvidersAsync();
        var legacyPublishers = await _publisherService.GetPublishersAsync();

        return legacyIdentities.Any() || legacyProviders.Any() || legacyPublishers.Any();
    }

    public async Task MigrateAsync()
    {
        _logger.LogInformation("Starting settings migration...");

        var settings = await _encryptedSettings.GetSettingsAsync();
        var modified = false;

        // Migrate Apple identities
        var legacyIdentities = await _appleIdentityService.GetIdentitiesAsync();
        if (legacyIdentities.Any() && !settings.AppleIdentities.Any())
        {
            _logger.LogInformation($"Migrating {legacyIdentities.Count} Apple identities");
            var identityData = legacyIdentities.Select(id => new AppleIdentityData(
                Id: id.Id,
                Name: id.Name,
                KeyId: id.KeyId,
                IssuerId: id.IssuerId,
                P8Content: id.P8KeyContent ?? "",
                CreatedAt: DateTime.UtcNow
            )).ToList();

            settings = settings with { AppleIdentities = identityData };
            modified = true;
        }

        // Migrate cloud providers
        var legacyProviders = await _cloudSecrets.GetProvidersAsync();
        if (legacyProviders.Any() && !settings.CloudProviders.Any())
        {
            _logger.LogInformation($"Migrating {legacyProviders.Count} cloud providers");
            var providerData = legacyProviders.Select(p => new CloudProviderData(
                Id: p.Id,
                Name: p.Name,
                ProviderType: p.ProviderType,
                Settings: new Dictionary<string, string>(p.Settings),
                IsActive: !string.IsNullOrEmpty(p.Name),
                DefaultItemKinds: p.EffectiveDefaultItemKinds.ToList()
            )).ToList();

            var activeProvider = _cloudSecrets.ActiveProvider;
            settings = settings with 
            { 
                CloudProviders = providerData,
                ActiveCloudProviderId = activeProvider?.Id
            };
            modified = true;
        }

        // Migrate secrets publishers
        var legacyPublishers = await _publisherService.GetPublishersAsync();
        if (legacyPublishers.Any() && !settings.SecretsPublishers.Any())
        {
            _logger.LogInformation($"Migrating {legacyPublishers.Count} secrets publishers");
            var publisherData = legacyPublishers.Select(p => new SecretsPublisherData(
                Id: p.Id,
                ProviderId: p.ProviderId,
                Name: p.Name,
                Settings: new Dictionary<string, string>(p.Settings)
            )).ToList();

            settings = settings with { SecretsPublishers = publisherData };
            modified = true;
        }

        if (modified)
        {
            await _encryptedSettings.SaveSettingsAsync(settings);
            _logger.LogInformation("Settings migration completed successfully");
        }
        else
        {
            _logger.LogInformation("No migration needed");
        }
    }
}
