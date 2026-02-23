using Security;

namespace MauiSherpa.Services;

/// <summary>
/// macOS implementation of ISecureStorage using Keychain Services.
/// In DEBUG builds, SecureStorageService falls back to file storage anyway.
/// </summary>
public class MacOSSecureStorage : ISecureStorage
{
    public Task<string?> GetAsync(string key)
    {
        var record = new SecRecord(SecKind.GenericPassword)
        {
            Service = "MauiSherpa",
            Account = key,
        };
        var match = SecKeyChain.QueryAsRecord(record, out var status);
        if (status == SecStatusCode.Success && match?.ValueData != null)
            return Task.FromResult<string?>(match.ValueData.ToString());
        return Task.FromResult<string?>(null);
    }

    public Task SetAsync(string key, string value)
    {
        Remove(key); // Remove existing first
        var record = new SecRecord(SecKind.GenericPassword)
        {
            Service = "MauiSherpa",
            Account = key,
            ValueData = Foundation.NSData.FromString(value),
        };
        SecKeyChain.Add(record);
        return Task.CompletedTask;
    }

    public bool Remove(string key)
    {
        var record = new SecRecord(SecKind.GenericPassword)
        {
            Service = "MauiSherpa",
            Account = key,
        };
        return SecKeyChain.Remove(record) == SecStatusCode.Success;
    }

    public void RemoveAll()
    {
        // Not easily implemented with Keychain; no-op for safety
    }

    public Task SetAsync(string key, string value, CancellationToken token = default) =>
        SetAsync(key, value);
}
