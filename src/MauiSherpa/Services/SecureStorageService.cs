using MauiSherpa.Core.Interfaces;
using System.Text.Json;

namespace MauiSherpa.Services;

/// <summary>
/// Secure storage service that uses platform Keychain/DPAPI when available,
/// with a fallback to local JSON file for debugging scenarios where entitlements aren't configured.
/// </summary>
public class SecureStorageService : ILegacySecureStorageService
{
    private readonly string _fallbackPath;
    private readonly ISecureStorage _secureStorage;
    private readonly IAlertService? _alertService;
    private Dictionary<string, string>? _fallbackCache;
    private bool _usesFallback;
    private bool _shownFallbackToast;

    public SecureStorageService(ISecureStorage secureStorage, IAlertService? alertService = null)
    {
        _secureStorage = secureStorage;
        _alertService = alertService;
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
                var value = await _secureStorage.GetAsync(key);
                if (value != null)
                    return value;
                // Key not found in SecureStorage - also check fallback in case it was saved there previously
            }
            catch (Exception)
            {
#if DEBUG
                // Keychain access denied - switch to fallback (DEBUG only)
                _usesFallback = true;
                ShowFallbackToast();
#else
                // In release builds, do NOT fall back to plaintext storage
                System.Diagnostics.Debug.WriteLine("SecureStorage unavailable — refusing to fall back to plaintext in Release");
                return null;
#endif
            }
        }

        // Fallback to file (DEBUG only, or migrating from previous fallback)
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
                await _secureStorage.SetAsync(key, value);
                return;
            }
            catch (Exception)
            {
#if DEBUG
                _usesFallback = true;
                ShowFallbackToast();
#else
                System.Diagnostics.Debug.WriteLine("SecureStorage unavailable — refusing to fall back to plaintext in Release");
                throw;
#endif
            }
        }

        // Fallback to file (DEBUG only)
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
                _secureStorage.Remove(key);
                // Also remove from fallback if it exists there
            }
            catch (Exception)
            {
#if DEBUG
                _usesFallback = true;
                ShowFallbackToast();
#else
                return; // Silently fail in release — nothing to remove
#endif
            }
        }

        // Also try fallback file
        await LoadFallbackCacheAsync();
        if (_fallbackCache?.Remove(key) == true)
        {
            await SaveFallbackCacheAsync();
        }
    }

    private void ShowFallbackToast()
    {
        if (_shownFallbackToast) return;
        _shownFallbackToast = true;
        System.Diagnostics.Debug.WriteLine("SecureStorage unavailable, using fallback file storage");
        _ = _alertService?.ShowToastAsync("⚠️ SecureStorage unavailable — using plaintext fallback (DEBUG)");
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
