using System.Security.Cryptography;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

public class LocalSecretsKeyStore : ILocalSecretsKeyStore
{
    private const string KeyName = "MAUI Sherpa Local Vault";
    private const string LegacyKeyName = "local_secrets_sqlcipher_key";
    private const int KeySize = 32;

    private readonly ISecureStorage _secureStorage;
    private readonly ILoggingService _logger;
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private string? _cachedKey;

    public LocalSecretsKeyStore(ISecureStorage secureStorage, ILoggingService logger)
    {
        _secureStorage = secureStorage;
        _logger = logger;
    }

    public async Task<string> GetOrCreateKeyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(_cachedKey))
            return _cachedKey;

        await _keyLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_cachedKey))
                return _cachedKey;

            var existing = await _secureStorage.GetAsync(KeyName);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                _cachedKey = existing;
                return existing;
            }

            var legacy = await _secureStorage.GetAsync(LegacyKeyName);
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                await _secureStorage.SetAsync(KeyName, legacy);
                _secureStorage.Remove(LegacyKeyName);
                _cachedKey = legacy;
                return legacy;
            }

            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySize));
            await _secureStorage.SetAsync(KeyName, key);
            _cachedKey = key;
            return key;
        }
        catch (Exception ex)
        {
            _logger.LogError("OS secure storage is required for the MAUI Sherpa local vault key.", ex);
            throw new InvalidOperationException(
                "Local secrets storage requires OS secure storage for the MAUI Sherpa local vault key. The local secrets database was not opened.",
                ex);
        }
        finally
        {
            _keyLock.Release();
        }
    }
}
