using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Encrypted settings service using AES-256-GCM with OS keychain for master key
/// </summary>
public class EncryptedSettingsService : IEncryptedSettingsService
{
    private const string MasterKeyStorageKey = "MauiSherpa_MasterKey";
    private const string SettingsFileName = "settings.enc";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32; // 256 bits
    
    private readonly IFileSystemService _fileSystem;
    private readonly ISecureStorageService _secureStorage;
    private readonly string _settingsPath;
    private MauiSherpaSettings? _cachedSettings;
    private readonly SemaphoreSlim _lock = new(1, 1);
    
    public event Action? OnSettingsChanged;

    public EncryptedSettingsService(IFileSystemService fileSystem, ISecureStorageService secureStorage)
    {
        _fileSystem = fileSystem;
        _secureStorage = secureStorage;
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MauiSherpa",
            SettingsFileName);
    }

    // Protected constructor for testing
    protected EncryptedSettingsService()
    {
        _fileSystem = null!;
        _secureStorage = null!;
        _settingsPath = "";
    }

    public async Task<MauiSherpaSettings> GetSettingsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            if (!await SettingsExistAsync())
            {
                _cachedSettings = new MauiSherpaSettings();
                return _cachedSettings;
            }

            var encryptedData = await File.ReadAllBytesAsync(_settingsPath);
            var masterKey = await GetOrCreateMasterKeyAsync();
            var json = Decrypt(encryptedData, masterKey);
            _cachedSettings = JsonSerializer.Deserialize<MauiSherpaSettings>(json) ?? new MauiSherpaSettings();
            return _cachedSettings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveSettingsAsync(MauiSherpaSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Create backup before saving
            if (File.Exists(_settingsPath))
            {
                var backupPath = _settingsPath + ".bak";
                File.Copy(_settingsPath, backupPath, overwrite: true);
            }

            var json = JsonSerializer.Serialize(settings with { LastModified = DateTime.UtcNow });
            var masterKey = await GetOrCreateMasterKeyAsync();
            var encrypted = Encrypt(json, masterKey);
            await File.WriteAllBytesAsync(_settingsPath, encrypted);
            
            _cachedSettings = settings;
            OnSettingsChanged?.Invoke();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateSettingsAsync(Func<MauiSherpaSettings, MauiSherpaSettings> transform)
    {
        var current = await GetSettingsAsync();
        var updated = transform(current);
        await SaveSettingsAsync(updated);
    }

    public Task<bool> SettingsExistAsync()
    {
        return Task.FromResult(File.Exists(_settingsPath));
    }

    protected virtual async Task<byte[]> GetOrCreateMasterKeyAsync()
    {
        var keyBase64 = await _secureStorage.GetAsync(MasterKeyStorageKey);
        if (!string.IsNullOrEmpty(keyBase64))
        {
            return Convert.FromBase64String(keyBase64);
        }

        // Generate new master key
        var key = RandomNumberGenerator.GetBytes(KeySize);
        await _secureStorage.SetAsync(MasterKeyStorageKey, Convert.ToBase64String(key));
        return key;
    }

    private static byte[] Encrypt(string plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Format: [nonce][tag][ciphertext]
        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);
        return result;
    }

    private static string Decrypt(byte[] encryptedData, byte[] key)
    {
        if (encryptedData.Length < NonceSize + TagSize)
            throw new CryptographicException("Invalid encrypted data");

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertext = new byte[encryptedData.Length - NonceSize - TagSize];

        Buffer.BlockCopy(encryptedData, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(encryptedData, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(encryptedData, NonceSize + TagSize, ciphertext, 0, ciphertext.Length);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
