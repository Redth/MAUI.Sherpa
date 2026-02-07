using MauiSherpa.Core.Interfaces;
using System.Text.Json;

namespace MauiSherpa.Services;

/// <summary>
/// Secure storage service that uses platform Keychain/DPAPI when available,
/// with a fallback to local JSON file for debugging scenarios where entitlements aren't configured.
/// </summary>
public class SecureStorageService : ISecureStorageService
{
    private readonly string _fallbackPath;
    private Dictionary<string, string>? _fallbackCache;
    private bool _usesFallback;

    public SecureStorageService()
    {
        _fallbackPath = Path.Combine(
            MauiSherpa.Core.Services.AppDataPath.GetAppDataDirectory(),
            ".secure-fallback.json");

#if DEBUG
        // Debug builds use ad-hoc signing with a different code signature each build,
        // so macOS Keychain entries become inaccessible after rebuild. Always use fallback.
        _usesFallback = true;
#endif
    }

    public async Task<string?> GetAsync(string key)
    {
        // Try SecureStorage first
        if (!_usesFallback)
        {
            try
            {
                var value = await SecureStorage.Default.GetAsync(key);
                if (value != null)
                    return value;
                // Key not found in SecureStorage - also check fallback in case it was saved there previously
            }
            catch (Exception)
            {
                // Keychain access denied - switch to fallback
                _usesFallback = true;
                System.Diagnostics.Debug.WriteLine("SecureStorage unavailable, using fallback file storage");
            }
        }

        // Fallback to file (either because SecureStorage failed, or key not found in SecureStorage)
        await LoadFallbackCacheAsync();
        return _fallbackCache?.GetValueOrDefault(key);
    }

    public async Task SetAsync(string key, string value)
    {
        // Try SecureStorage first
        if (!_usesFallback)
        {
            try
            {
                await SecureStorage.Default.SetAsync(key, value);
                return;
            }
            catch (Exception)
            {
                _usesFallback = true;
                System.Diagnostics.Debug.WriteLine("SecureStorage unavailable, using fallback file storage");
            }
        }

        // Fallback to file
        await LoadFallbackCacheAsync();
        _fallbackCache ??= new();
        _fallbackCache[key] = value;
        await SaveFallbackCacheAsync();
    }

    public async Task RemoveAsync(string key)
    {
        // Try SecureStorage first
        if (!_usesFallback)
        {
            try
            {
                SecureStorage.Default.Remove(key);
                // Also remove from fallback if it exists there
            }
            catch (Exception)
            {
                _usesFallback = true;
            }
        }

        // Also try fallback file
        await LoadFallbackCacheAsync();
        if (_fallbackCache?.Remove(key) == true)
        {
            await SaveFallbackCacheAsync();
        }
    }

    private async Task LoadFallbackCacheAsync()
    {
        if (_fallbackCache != null) return;

        try
        {
            if (File.Exists(_fallbackPath))
            {
                var json = await File.ReadAllTextAsync(_fallbackPath);
                _fallbackCache = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
            else
            {
                _fallbackCache = new();
            }
        }
        catch
        {
            _fallbackCache = new();
        }
    }

    private async Task SaveFallbackCacheAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_fallbackPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_fallbackCache, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_fallbackPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save fallback cache: {ex.Message}");
        }
    }
}
