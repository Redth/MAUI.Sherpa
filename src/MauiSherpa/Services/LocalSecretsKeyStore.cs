using System.Security.Cryptography;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

public class LocalSecretsKeyStore : ILocalSecretsKeyStore, ILocalVaultAccessService
{
    private const string KeyName = "MAUI Sherpa Local Vault";
    private const string LegacyKeyName = "local_secrets_sqlcipher_key";
    private const string AccessDeniedKey = "LocalVault.AccessDenied";
    private const string AccessDeniedMessageKey = "LocalVault.AccessDeniedMessage";
    private const string AccessDeniedAtKey = "LocalVault.AccessDeniedAtUtc";
    private const int KeySize = 32;

    private readonly ISecureStorage _secureStorage;
    private readonly IPreferences _preferences;
    private readonly ILoggingService _logger;
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private string? _cachedKey;

    public LocalSecretsKeyStore(ISecureStorage secureStorage, IPreferences preferences, ILoggingService logger)
    {
        _secureStorage = secureStorage;
        _preferences = preferences;
        _logger = logger;
    }

    public event Action? StateChanged;

    public async Task<string> GetOrCreateKeyAsync(CancellationToken cancellationToken = default)
        => await GetOrCreateKeyAsync(forcePrompt: false, cancellationToken);

    public LocalVaultAccessState GetState()
    {
        if (!string.IsNullOrWhiteSpace(_cachedKey) || !_preferences.Get(AccessDeniedKey, false))
            return LocalVaultAccessState.Available;

        var message = _preferences.Get<string?>(AccessDeniedMessageKey, null);
        var lastFailureText = _preferences.Get<string?>(AccessDeniedAtKey, null);
        DateTime? lastFailureUtc = DateTime.TryParse(lastFailureText, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

        return new LocalVaultAccessState(
            LocalVaultAccessProblem.AccessDenied,
            string.IsNullOrWhiteSpace(message)
                ? "MAUI Sherpa cannot access the Local Vault key in OS secure storage."
                : message,
            lastFailureUtc);
    }

    public async Task<LocalVaultAccessState> RequestAccessAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await GetOrCreateKeyAsync(forcePrompt: true, cancellationToken);
            ClearAccessFailure();
        }
        catch (LocalVaultUnavailableException)
        {
        }

        return GetState();
    }

    private async Task<string> GetOrCreateKeyAsync(bool forcePrompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(_cachedKey))
            return _cachedKey;

        if (!forcePrompt && GetState().RequiresUserAction)
            throw new LocalVaultUnavailableException(GetState().Message ?? "Local vault access requires user approval.");

        await _keyLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_cachedKey))
                return _cachedKey;

            if (!forcePrompt && GetState().RequiresUserAction)
                throw new LocalVaultUnavailableException(GetState().Message ?? "Local vault access requires user approval.");

            var existing = await _secureStorage.GetAsync(KeyName);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                _cachedKey = existing;
                ClearAccessFailure();
                return existing;
            }

            var legacy = await _secureStorage.GetAsync(LegacyKeyName);
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                await _secureStorage.SetAsync(KeyName, legacy);
                _secureStorage.Remove(LegacyKeyName);
                _cachedKey = legacy;
                ClearAccessFailure();
                return legacy;
            }

            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySize));
            await _secureStorage.SetAsync(KeyName, key);
            _cachedKey = key;
            ClearAccessFailure();
            return key;
        }
        catch (LocalVaultUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = "MAUI Sherpa needs access to the Local Vault key in OS secure storage before local secrets can be used.";
            RecordAccessFailure(message, ex);
            _logger.LogError("OS secure storage is required for the MAUI Sherpa local vault key.", ex);
            throw new LocalVaultUnavailableException(message, ex);
        }
        finally
        {
            _keyLock.Release();
        }
    }

    private void RecordAccessFailure(string message, Exception exception)
    {
        _preferences.Set(AccessDeniedKey, true);
        _preferences.Set(AccessDeniedMessageKey, $"{message} {exception.Message}");
        _preferences.Set(AccessDeniedAtKey, DateTime.UtcNow.ToString("O"));
        StateChanged?.Invoke();
    }

    private void ClearAccessFailure()
    {
        var hadFailure = _preferences.Get(AccessDeniedKey, false);
        _preferences.Remove(AccessDeniedKey);
        _preferences.Remove(AccessDeniedMessageKey);
        _preferences.Remove(AccessDeniedAtKey);
        if (hadFailure)
            StateChanged?.Invoke();
    }
}
