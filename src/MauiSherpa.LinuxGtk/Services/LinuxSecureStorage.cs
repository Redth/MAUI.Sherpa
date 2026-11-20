using System.Text;
using Microsoft.Maui.Storage;

namespace MauiSherpa.Services;

public sealed class LinuxSecureStorage : ISecureStorage, IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly object _sync = new();
    private readonly string _applicationId;
    private readonly ILinuxSecretStore _store;
    private bool _disposed;

    public LinuxSecureStorage(string applicationId)
        : this(applicationId, new LibSecretStore())
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Linux secure storage requires Linux and libsecret.");
    }

    internal LinuxSecureStorage(string applicationId, ILinuxSecretStore store)
    {
        ValidateText(applicationId, nameof(applicationId), allowEmpty: false);
        ArgumentNullException.ThrowIfNull(store);
        _applicationId = applicationId;
        _store = store;
    }

    public Task<string?> GetAsync(string key)
    {
        ValidateText(key, nameof(key), allowEmpty: false);
        return Task.Run(() => Execute(() => _store.Get(_applicationId, key)));
    }

    public Task SetAsync(string key, string value)
    {
        ValidateText(key, nameof(key), allowEmpty: false);
        ValidateText(value, nameof(value), allowEmpty: true);
        return Task.Run(() => Execute(() =>
        {
            _store.Set(_applicationId, key, value);
            return true;
        }));
    }

    public bool Remove(string key)
    {
        ValidateText(key, nameof(key), allowEmpty: false);
        return Execute(() => _store.Remove(_applicationId, key));
    }

    public void RemoveAll() => Execute(() => _store.Remove(_applicationId, null));

    public void Dispose()
    {
        lock (_sync)
            _disposed = true;
    }

    private T Execute<T>(Func<T> operation)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                return operation();
            }
            catch (DllNotFoundException ex)
            {
                throw new PlatformNotSupportedException(
                    "Linux secure storage requires libsecret-1.so.0, libglib-2.0.so.0, and libgobject-2.0.so.0.", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new PlatformNotSupportedException(
                    "The installed libsecret or GLib version does not provide the required secure-storage APIs.", ex);
            }
        }
    }

    private static void ValidateText(string value, string parameterName, bool allowEmpty)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!allowEmpty && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("The value must not be empty or whitespace.", parameterName);
        if (value.Contains('\0'))
            throw new ArgumentException("Embedded NUL characters are not supported by libsecret.", parameterName);

        try
        {
            _ = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("The value must contain valid Unicode text.", parameterName);
        }
    }
}

internal interface ILinuxSecretStore
{
    string? Get(string applicationId, string key);
    void Set(string applicationId, string key, string value);
    bool Remove(string applicationId, string? key);
}
