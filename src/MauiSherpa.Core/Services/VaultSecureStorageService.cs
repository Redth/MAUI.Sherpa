using System.Text;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public sealed class VaultSecureStorageService : ISecureStorageService
{
    private const string OriginalKeyMetadataName = "OriginalKey";

    private readonly ILocalVaultStore _vaultStore;
    private readonly ILegacySecureStorageService _legacySecureStorage;
    private readonly ILoggingService _logger;
    private readonly ILocalVaultIntroductionService? _localVaultIntroduction;

    public VaultSecureStorageService(
        ILocalVaultStore vaultStore,
        ILegacySecureStorageService legacySecureStorage,
        ILoggingService logger,
        ILocalVaultIntroductionService? localVaultIntroduction = null)
    {
        _vaultStore = vaultStore;
        _legacySecureStorage = legacySecureStorage;
        _logger = logger;
        _localVaultIntroduction = localVaultIntroduction;
    }

    public async Task<string?> GetAsync(string key)
    {
        ValidateKey(key);

        if (!IsLocalVaultEnabled())
            return await _legacySecureStorage.GetAsync(key);

        var vaultKey = EncodeKey(key);
        var item = await _vaultStore.GetAsync(LocalVaultScopes.SecureStorage, "/", vaultKey);
        if (item is not null)
            return Encoding.UTF8.GetString(item.Value);

        var legacyValue = await _legacySecureStorage.GetAsync(key);
        if (legacyValue is null)
            return null;

        await SetAsync(key, legacyValue);
        await _legacySecureStorage.RemoveAsync(key);
        _logger.LogInformation($"Migrated secure storage key into local vault: {key}");
        return legacyValue;
    }

    public async Task SetAsync(string key, string value)
    {
        ValidateKey(key);

        if (!IsLocalVaultEnabled())
        {
            await _legacySecureStorage.SetAsync(key, value);
            return;
        }

        var vaultKey = EncodeKey(key);
        await _vaultStore.PutAsync(
            LocalVaultScopes.SecureStorage,
            "/",
            vaultKey,
            Encoding.UTF8.GetBytes(value),
            LocalVaultContentTypes.Text,
            new Dictionary<string, string>
            {
                [OriginalKeyMetadataName] = key
            });

        await _legacySecureStorage.RemoveAsync(key);
    }

    public async Task RemoveAsync(string key)
    {
        ValidateKey(key);

        if (!IsLocalVaultEnabled())
        {
            await _legacySecureStorage.RemoveAsync(key);
            return;
        }

        await _vaultStore.RemoveAsync(LocalVaultScopes.SecureStorage, "/", EncodeKey(key));
        await _legacySecureStorage.RemoveAsync(key);
    }

    private bool IsLocalVaultEnabled() =>
        _localVaultIntroduction?.GetState().IsLocalVaultEnabled != false;

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Secure storage key cannot be empty.", nameof(key));
    }

    private static string EncodeKey(string key)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
