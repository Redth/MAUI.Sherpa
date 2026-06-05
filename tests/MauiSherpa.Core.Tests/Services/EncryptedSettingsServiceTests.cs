using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;
using Xunit;

namespace MauiSherpa.Core.Tests.Services;

public class EncryptedSettingsServiceTests
{
    private readonly string _testDir;
    private readonly TestableEncryptedSettingsService _service;

    public EncryptedSettingsServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"maui-sherpa-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _service = new TestableEncryptedSettingsService(_testDir);
    }

    [Fact]
    public async Task GetSettingsAsync_ReturnsEmptySettings_WhenNoFileExists()
    {
        var settings = await _service.GetSettingsAsync();
        
        settings.Should().NotBeNull();
        settings.AppleIdentities.Should().BeEmpty();
        settings.CloudProviders.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveSettingsAsync_CreatesEncryptedFile()
    {
        var settings = new MauiSherpaSettings
        {
            AppleIdentities = new List<AppleIdentityData>
            {
                new("id1", "Test Identity", "KEY123", "ISSUER456", "p8content", DateTime.UtcNow)
            }
        };

        await _service.SaveSettingsAsync(settings);

        var filePath = Path.Combine(_testDir, "settings.enc");
        File.Exists(filePath).Should().BeTrue();
        
        // Verify file is encrypted (not readable JSON)
        var content = await File.ReadAllBytesAsync(filePath);
        var text = System.Text.Encoding.UTF8.GetString(content);
        text.Should().NotContain("AppleIdentities");
    }

    [Fact]
    public async Task RoundTrip_PreservesSettings()
    {
        var original = new MauiSherpaSettings
        {
            AppleIdentities = new List<AppleIdentityData>
            {
                new("id1", "Identity 1", "KEY1", "ISSUER1", "content1", DateTime.UtcNow)
            },
            CloudProviders = new List<CloudProviderData>
            {
                new("cp1", "Provider 1", CloudSecretsProviderType.AzureKeyVault, 
                    new Dictionary<string, string> { ["key"] = "value" })
            }
        };

        await _service.SaveSettingsAsync(original);
        _service.ClearCache();
        
        var loaded = await _service.GetSettingsAsync();

        loaded.AppleIdentities.Should().HaveCount(1);
        loaded.AppleIdentities[0].Name.Should().Be("Identity 1");
        loaded.CloudProviders.Should().HaveCount(1);
        loaded.CloudProviders[0].Name.Should().Be("Provider 1");
    }

    [Fact]
    public async Task UpdateSettingsAsync_AppliesTransform()
    {
        var initial = new MauiSherpaSettings();
        await _service.SaveSettingsAsync(initial);

        await _service.UpdateSettingsAsync(s => s with
        {
            Preferences = new AppPreferences { Theme = "Dark" }
        });

        var updated = await _service.GetSettingsAsync();
        updated.Preferences.Theme.Should().Be("Dark");
    }

    [Fact]
    public async Task SettingsExistAsync_ReturnsFalse_WhenNoFile()
    {
        var exists = await _service.SettingsExistAsync();
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task SettingsExistAsync_ReturnsTrue_AfterSave()
    {
        await _service.SaveSettingsAsync(new MauiSherpaSettings());
        
        var exists = await _service.SettingsExistAsync();
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SaveSettingsAsync_CreatesBackup_WhenFileExists()
    {
        await _service.SaveSettingsAsync(new MauiSherpaSettings { Version = 1 });
        await _service.SaveSettingsAsync(new MauiSherpaSettings { Version = 2 });

        var backupPath = Path.Combine(_testDir, "settings.enc.bak");
        File.Exists(backupPath).Should().BeTrue();
    }

    [Fact]
    public async Task OnSettingsChanged_FiresOnSave()
    {
        var eventFired = false;
        _service.OnSettingsChanged += () => eventFired = true;

        await _service.SaveSettingsAsync(new MauiSherpaSettings());

        eventFired.Should().BeTrue();
    }

    [Fact]
    public async Task GetSettingsAsync_UsesCachedValue()
    {
        await _service.SaveSettingsAsync(new MauiSherpaSettings
        {
            Preferences = new AppPreferences { Theme = "Light" }
        });

        // First read
        var first = await _service.GetSettingsAsync();
        
        // Modify file directly (simulating external change)
        // This should NOT affect the cached value
        var second = await _service.GetSettingsAsync();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public async Task ClearCache_ForcesReload()
    {
        await _service.SaveSettingsAsync(new MauiSherpaSettings
        {
            Preferences = new AppPreferences { Theme = "Original" }
        });

        var first = await _service.GetSettingsAsync();
        _service.ClearCache();
        var second = await _service.GetSettingsAsync();

        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public async Task SaveSettingsAsync_UpdatesLastModified()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        
        await _service.SaveSettingsAsync(new MauiSherpaSettings());
        _service.ClearCache();
        var settings = await _service.GetSettingsAsync();

        settings.LastModified.Should().BeAfter(before);
    }

    [Fact]
    public async Task MultipleIdentities_RoundTrip()
    {
        var settings = new MauiSherpaSettings
        {
            AppleIdentities = new List<AppleIdentityData>
            {
                new("1", "Dev", "K1", "I1", "p8-1", DateTime.UtcNow),
                new("2", "Prod", "K2", "I2", "p8-2", DateTime.UtcNow),
                new("3", "Test", "K3", "I3", "p8-3", DateTime.UtcNow)
            }
        };

        await _service.SaveSettingsAsync(settings);
        _service.ClearCache();
        var loaded = await _service.GetSettingsAsync();

        loaded.AppleIdentities.Should().HaveCount(3);
        loaded.AppleIdentities.Select(i => i.Name).Should().BeEquivalentTo("Dev", "Prod", "Test");
    }

    [Fact]
    public async Task EmptyCollections_HandledCorrectly()
    {
        var settings = new MauiSherpaSettings
        {
            AppleIdentities = new List<AppleIdentityData>(),
            CloudProviders = new List<CloudProviderData>(),
            SecretsPublishers = new List<SecretsPublisherData>()
        };

        await _service.SaveSettingsAsync(settings);
        _service.ClearCache();
        var loaded = await _service.GetSettingsAsync();

        loaded.AppleIdentities.Should().BeEmpty();
        loaded.CloudProviders.Should().BeEmpty();
        loaded.SecretsPublishers.Should().BeEmpty();
    }

    [Fact]
    public async Task SpecialCharacters_InSettings_PreservedCorrectly()
    {
        var settings = new MauiSherpaSettings
        {
            AppleIdentities = new List<AppleIdentityData>
            {
                new("1", "Test 日本語 émojis 🎉", "KEY", "ISS", "-----BEGIN PRIVATE KEY-----\nbase64==\n-----END PRIVATE KEY-----", DateTime.UtcNow)
            }
        };

        await _service.SaveSettingsAsync(settings);
        _service.ClearCache();
        var loaded = await _service.GetSettingsAsync();

        loaded.AppleIdentities[0].Name.Should().Be("Test 日本語 émojis 🎉");
        loaded.AppleIdentities[0].P8Content.Should().Contain("BEGIN PRIVATE KEY");
    }

    [Fact]
    public async Task GetSettingsAsync_WithVault_MigratesLegacyEncryptedFileAndCleansUp()
    {
        var legacySecureStorage = new InMemorySecureStorage();
        var settingsPath = Path.Combine(_testDir, "vault-migration-settings.enc");
        var legacyService = new EncryptedSettingsService(
            new InMemoryFileSystem(),
            legacySecureStorage,
            vaultStore: null,
            settingsPath);
        await legacyService.SaveSettingsAsync(new MauiSherpaSettings
        {
            Preferences = new AppPreferences { Theme = "Dark" }
        });
        File.Exists(settingsPath).Should().BeTrue();
        legacySecureStorage.Values.Should().ContainKey("MauiSherpa_MasterKey");

        var vaultStore = CreateVaultStore();
        var vaultService = new EncryptedSettingsService(
            new InMemoryFileSystem(),
            legacySecureStorage,
            vaultStore,
            settingsPath);

        var settings = await vaultService.GetSettingsAsync();
        var savedItem = await vaultStore.GetAsync(LocalVaultScopes.Settings, "/", "maui-sherpa-settings");

        settings.Preferences.Theme.Should().Be("Dark");
        savedItem.Should().NotBeNull();
        File.Exists(settingsPath).Should().BeFalse();
        legacySecureStorage.Values.Should().NotContainKey("MauiSherpa_MasterKey");
    }

    [Fact]
    public async Task SaveSettingsAsync_WithVault_DoesNotCreateLegacyEncryptedFile()
    {
        var settingsPath = Path.Combine(_testDir, "vault-only-settings.enc");
        var vaultStore = CreateVaultStore();
        var service = new EncryptedSettingsService(
            new InMemoryFileSystem(),
            new InMemorySecureStorage(),
            vaultStore,
            settingsPath);

        await service.SaveSettingsAsync(new MauiSherpaSettings
        {
            Preferences = new AppPreferences { Theme = "Light" }
        });
        var loaded = await service.GetSettingsAsync();

        loaded.Preferences.Theme.Should().Be("Light");
        File.Exists(settingsPath).Should().BeFalse();
    }

    [Fact]
    public async Task BeforeIntroEnabled_UsesLegacyEncryptedSettingsOnly()
    {
        var settingsPath = Path.Combine(_testDir, "intro-pending-settings.enc");
        var service = new EncryptedSettingsService(
            new InMemoryFileSystem(),
            new InMemorySecureStorage(),
            new ThrowingLocalVaultStore(),
            new TestLocalVaultIntroductionService(LocalVaultIntroductionState.NotShown),
            settingsPath);

        await service.SaveSettingsAsync(new MauiSherpaSettings
        {
            Preferences = new AppPreferences { Theme = "Dark" }
        });
        var settings = await service.GetSettingsAsync();

        settings.Preferences.Theme.Should().Be("Dark");
        File.Exists(settingsPath).Should().BeTrue();
    }

    private SqlCipherLocalVaultStore CreateVaultStore()
    {
        return new SqlCipherLocalVaultStore(
            new TestLocalVaultKeyStore(),
            new TestLogger(),
            new LocalVaultOptions(Path.Combine(_testDir, $"{Guid.NewGuid():N}.db")));
    }

    private sealed class TestLocalVaultKeyStore : ILocalVaultKeyStore
    {
        public Task<string> GetOrCreateKeyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("test-key");
    }

    private sealed class TestLocalVaultIntroductionService(LocalVaultIntroductionState state) : ILocalVaultIntroductionService
    {
        public LocalVaultIntroductionState GetState() => state;
        public Task MarkEnabledAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkDeclinedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public event Action? StateChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class ThrowingLocalVaultStore : ILocalVaultStore
    {
        public string DatabasePath => "throwing";

        public Task<LocalVaultItem> PutAsync(
            string scope,
            string path,
            string key,
            byte[] value,
            string contentType,
            Dictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Vault should not be used before intro is enabled.");

        public Task<LocalVaultItem?> GetAsync(
            string scope,
            string path,
            string key,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Vault should not be used before intro is enabled.");

        public Task<bool> RemoveAsync(
            string scope,
            string path,
            string key,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Vault should not be used before intro is enabled.");

        public Task<bool> ExistsAsync(
            string scope,
            string path,
            string key,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Vault should not be used before intro is enabled.");

        public Task<IReadOnlyList<LocalVaultItem>> ListAsync(
            string scope,
            string? path = null,
            string? keyPrefix = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Vault should not be used before intro is enabled.");
    }

    private sealed class InMemorySecureStorage : ISecureStorageService
    {
        public Dictionary<string, string> Values { get; } = new();

        public Task<string?> GetAsync(string key)
            => Task.FromResult(Values.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(string key, string value)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            Values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryFileSystem : IFileSystemService
    {
        public Task<string?> ReadFileAsync(string path) => Task.FromResult<string?>(null);
        public Task WriteFileAsync(string path, string content) => Task.CompletedTask;
        public Task<bool> FileExistsAsync(string path) => Task.FromResult(File.Exists(path));
        public Task<bool> DirectoryExistsAsync(string path) => Task.FromResult(Directory.Exists(path));
        public Task<IReadOnlyList<string>> GetFilesAsync(string path, string searchPattern = "*") => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task CreateDirectoryAsync(string path)
        {
            Directory.CreateDirectory(path);
            return Task.CompletedTask;
        }
        public Task DeleteFileAsync(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
            return Task.CompletedTask;
        }
        public Task DeleteDirectoryAsync(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            return Task.CompletedTask;
        }
        public void RevealInFileManager(string path) { }
    }

    private sealed class TestLogger : ILoggingService
    {
        public void LogInformation(string message) { }
        public void LogWarning(string message) { }
        public void LogError(string message, Exception? exception = null) { }
        public void LogDebug(string message) { }
        public IReadOnlyList<LogEntry> GetRecentLogs(int maxCount = 500) => Array.Empty<LogEntry>();
        public void ClearLogs() { }
        public event Action? OnLogAdded;
    }
}

/// <summary>
/// Testable version that uses file-based key storage instead of SecureStorage
/// </summary>
public class TestableEncryptedSettingsService : EncryptedSettingsService
{
    private readonly string _testDir;
    private readonly string _settingsPath;
    private readonly string _keyPath;
    private MauiSherpaSettings? _cachedSettings;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public TestableEncryptedSettingsService(string testDir)
    {
        _testDir = testDir;
        _settingsPath = Path.Combine(testDir, "settings.enc");
        _keyPath = Path.Combine(testDir, "master.key");
    }

    public new async Task<MauiSherpaSettings> GetSettingsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_cachedSettings != null)
                return _cachedSettings;

            if (!File.Exists(_settingsPath))
            {
                _cachedSettings = new MauiSherpaSettings();
                return _cachedSettings;
            }

            var encryptedData = await File.ReadAllBytesAsync(_settingsPath);
            var key = await GetOrCreateMasterKeyAsync();
            var json = Decrypt(encryptedData, key);
            _cachedSettings = System.Text.Json.JsonSerializer.Deserialize<MauiSherpaSettings>(json) 
                ?? new MauiSherpaSettings();
            return _cachedSettings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public new async Task SaveSettingsAsync(MauiSherpaSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            if (File.Exists(_settingsPath))
            {
                File.Copy(_settingsPath, _settingsPath + ".bak", overwrite: true);
            }

            var json = System.Text.Json.JsonSerializer.Serialize(settings with { LastModified = DateTime.UtcNow });
            var key = await GetOrCreateMasterKeyAsync();
            var encrypted = Encrypt(json, key);
            await File.WriteAllBytesAsync(_settingsPath, encrypted);

            _cachedSettings = settings;
            RaiseOnSettingsChanged();
        }
        finally
        {
            _lock.Release();
        }
    }

    public new async Task UpdateSettingsAsync(Func<MauiSherpaSettings, MauiSherpaSettings> transform)
    {
        var current = await GetSettingsAsync();
        var updated = transform(current);
        await SaveSettingsAsync(updated);
    }

    public new Task<bool> SettingsExistAsync() => Task.FromResult(File.Exists(_settingsPath));

    public void ClearCache() => _cachedSettings = null;

    protected override async Task<byte[]> GetOrCreateMasterKeyAsync()
    {
        if (File.Exists(_keyPath))
            return await File.ReadAllBytesAsync(_keyPath);

        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        await File.WriteAllBytesAsync(_keyPath, key);
        return key;
    }

    private static byte[] Encrypt(string plaintext, byte[] key)
    {
        var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new System.Security.Cryptography.AesGcm(key, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var result = new byte[12 + 16 + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, 12);
        Buffer.BlockCopy(tag, 0, result, 12, 16);
        Buffer.BlockCopy(ciphertext, 0, result, 28, ciphertext.Length);
        return result;
    }

    private static string Decrypt(byte[] encryptedData, byte[] key)
    {
        var nonce = new byte[12];
        var tag = new byte[16];
        var ciphertext = new byte[encryptedData.Length - 28];

        Buffer.BlockCopy(encryptedData, 0, nonce, 0, 12);
        Buffer.BlockCopy(encryptedData, 12, tag, 0, 16);
        Buffer.BlockCopy(encryptedData, 28, ciphertext, 0, ciphertext.Length);

        var plaintext = new byte[ciphertext.Length];
        using var aes = new System.Security.Cryptography.AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }

    public new event Action? OnSettingsChanged;
    private void RaiseOnSettingsChanged() => OnSettingsChanged?.Invoke();
}
