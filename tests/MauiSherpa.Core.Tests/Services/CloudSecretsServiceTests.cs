using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class CloudSecretsServiceTests
{
    [Fact]
    public async Task InitializeAsync_WithNoProviders_CreatesAndActivatesLocalProvider()
    {
        var secureStorage = new InMemorySecureStorage();
        var service = CreateService(secureStorage: secureStorage);

        await service.InitializeAsync();

        service.ActiveProvider.Should().NotBeNull();
        service.ActiveProvider!.Id.Should().Be("local");
        service.ActiveProvider.Name.Should().Be("Local");
        service.ActiveProvider.ProviderType.Should().Be(CloudSecretsProviderType.Local);
        secureStorage.Values["cloud_secrets_active_provider"].Should().Be("local");

        var providers = await service.GetProvidersAsync();
        providers.Should().ContainSingle(p => p.ProviderType == CloudSecretsProviderType.Local);
    }

    [Fact]
    public async Task InitializeAsync_WithExistingActiveProvider_AddsLocalAndDoesNotOverride()
    {
        var service = CreateService();
        var provider = new CloudSecretsProviderConfig(
            "remote",
            "Remote",
            CloudSecretsProviderType.AzureKeyVault,
            new Dictionary<string, string>
            {
                ["VaultUrl"] = "https://test.vault.azure.net",
                ["TenantId"] = "tenant",
                ["ClientId"] = "client",
                ["ClientSecret"] = "secret"
            });

        await service.SaveProviderAsync(provider);
        await service.SetActiveProviderAsync(provider.Id);

        await service.InitializeAsync();

        service.ActiveProvider.Should().NotBeNull();
        service.ActiveProvider!.Id.Should().Be("remote");
        service.ActiveProvider.ProviderType.Should().Be(CloudSecretsProviderType.AzureKeyVault);

        var providers = await service.GetProvidersAsync();
        providers.Should().Contain(p => p.Id == "remote");
        providers.Should().ContainSingle(p => p.Id == "local" && p.ProviderType == CloudSecretsProviderType.Local);
    }

    [Fact]
    public async Task InitializeAsync_WithExistingProvidersButNoActiveProvider_ActivatesLocal()
    {
        var secureStorage = new InMemorySecureStorage();
        var service = CreateService(secureStorage: secureStorage);
        var provider = new CloudSecretsProviderConfig(
            "remote",
            "Remote",
            CloudSecretsProviderType.AzureKeyVault,
            new Dictionary<string, string>
            {
                ["VaultUrl"] = "https://test.vault.azure.net",
                ["TenantId"] = "tenant",
                ["ClientId"] = "client",
                ["ClientSecret"] = "secret"
            });

        await service.SaveProviderAsync(provider);

        await service.InitializeAsync();

        service.ActiveProvider.Should().NotBeNull();
        service.ActiveProvider!.Id.Should().Be("local");
        secureStorage.Values["cloud_secrets_active_provider"].Should().Be("local");

        var providers = await service.GetProvidersAsync();
        providers.Should().Contain(p => p.Id == "remote");
        providers.Should().ContainSingle(p => p.Id == "local" && p.ProviderType == CloudSecretsProviderType.Local);
    }

    [Fact]
    public async Task InitializeAsync_WithRemoteProviderAndLocalVaultUnavailable_ActivatesRemoteProvider()
    {
        var secureStorage = new InMemorySecureStorage();
        var service = CreateService(
            secureStorage: secureStorage,
            localVaultAccess: new TestLocalVaultAccessService(
                new LocalVaultAccessState(LocalVaultAccessProblem.AccessDenied, "Denied")));
        var provider = new CloudSecretsProviderConfig(
            "remote",
            "Remote",
            CloudSecretsProviderType.AzureKeyVault,
            new Dictionary<string, string>
            {
                ["VaultUrl"] = "https://test.vault.azure.net",
                ["TenantId"] = "tenant",
                ["ClientId"] = "client",
                ["ClientSecret"] = "secret"
            });

        await service.SaveProviderAsync(provider);

        await service.InitializeAsync();

        service.ActiveProvider.Should().NotBeNull();
        service.ActiveProvider!.Id.Should().Be("remote");
        service.ActiveProvider.ProviderType.Should().Be(CloudSecretsProviderType.AzureKeyVault);
        secureStorage.Values["cloud_secrets_active_provider"].Should().Be("remote");

        var providers = await service.GetProvidersAsync();
        providers.Should().Contain(p => p.Id == "remote");
        providers.Should().ContainSingle(p => p.Id == "local" && p.ProviderType == CloudSecretsProviderType.Local);
    }

    [Fact]
    public async Task DeleteProviderAsync_LocalProvider_DoesNotDeleteLocal()
    {
        var service = CreateService();

        await service.InitializeAsync();
        await service.DeleteProviderAsync("local");

        var providers = await service.GetProvidersAsync();
        providers.Should().ContainSingle(p => p.Id == "local" && p.ProviderType == CloudSecretsProviderType.Local);
    }

    [Fact]
    public async Task SaveProviderAsync_WithVaultStore_PersistsMetadataAndSettingsInVault()
    {
        var fileSystem = new InMemoryFileSystem();
        var vaultStore = CreateVaultStore();
        var service = CreateService(fileSystem: fileSystem, vaultStore: vaultStore);
        var provider = new CloudSecretsProviderConfig(
            "remote",
            "Remote",
            CloudSecretsProviderType.AzureKeyVault,
            new Dictionary<string, string>
            {
                ["VaultUrl"] = "https://test.vault.azure.net",
                ["TenantId"] = "tenant",
                ["ClientId"] = "client",
                ["ClientSecret"] = "secret"
            });

        await service.SaveProviderAsync(provider);
        var providers = await service.GetProvidersAsync();
        var metadataItem = await vaultStore.GetAsync(LocalVaultScopes.CloudProvider, "/", "providers");
        var settingsItem = await vaultStore.GetAsync(LocalVaultScopes.CloudProvider, "/providers", "settings-remote");

        providers.Should().ContainSingle(p => p.Id == "local" && p.ProviderType == CloudSecretsProviderType.Local);
        var remote = providers.Should().ContainSingle(p => p.Id == "remote").Subject;
        remote.Settings["VaultUrl"].Should().Be("https://test.vault.azure.net");
        remote.Settings["ClientSecret"].Should().Be("secret");
        metadataItem.Should().NotBeNull();
        settingsItem.Should().NotBeNull();
        fileSystem.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveProviderAsync_WithUnavailableVaultStore_PersistsMetadataAndSettingsToJson()
    {
        var fileSystem = new InMemoryFileSystem();
        var service = CreateService(fileSystem: fileSystem, vaultStore: new ThrowingLocalVaultStore());
        var provider = new CloudSecretsProviderConfig(
            "remote",
            "Remote",
            CloudSecretsProviderType.AzureKeyVault,
            new Dictionary<string, string>
            {
                ["VaultUrl"] = "https://test.vault.azure.net",
                ["TenantId"] = "tenant",
                ["ClientId"] = "client",
                ["ClientSecret"] = "secret"
            });

        await service.SaveProviderAsync(provider);
        var providers = await service.GetProvidersAsync();

        providers.Should().ContainSingle(p => p.Id == "remote");
        fileSystem.Files.Should().ContainKey(Path.Combine(AppDataPath.GetAppDataDirectory(), "cloud-secrets-providers.json"));
        fileSystem.Files.Should().ContainKey(Path.Combine(AppDataPath.GetAppDataDirectory(), "cloud-secrets-remote.json"));
    }

    [Fact]
    public async Task GetProvidersAsync_WithVaultStore_MigratesLegacyJsonAndDeletesFiles()
    {
        var fileSystem = new InMemoryFileSystem();
        var vaultStore = CreateVaultStore();
        var settingsPath = Path.Combine(AppDataPath.GetAppDataDirectory(), "cloud-secrets-providers.json");
        var providerSettingsPath = Path.Combine(AppDataPath.GetAppDataDirectory(), "cloud-secrets-remote.json");
        await fileSystem.WriteFileAsync(settingsPath, """
            [
              {
                "Id": "remote",
                "Name": "Remote",
                "ProviderType": 1,
                "NonSecretSettingKeys": [ "VaultUrl", "TenantId", "ClientId" ]
              }
            ]
            """);
        await fileSystem.WriteFileAsync(providerSettingsPath, """
            {
              "VaultUrl": "https://test.vault.azure.net",
              "TenantId": "tenant",
              "ClientId": "client"
            }
            """);
        var secureStorage = new InMemorySecureStorage();
        await secureStorage.SetAsync("cloud_secrets_provider_remote", """
            {
              "ClientSecret": "secret"
            }
            """);
        var service = CreateService(secureStorage: secureStorage, fileSystem: fileSystem, vaultStore: vaultStore);

        var providers = await service.GetProvidersAsync();

        providers.Should().ContainSingle(p => p.Id == "local" && p.ProviderType == CloudSecretsProviderType.Local);
        var remote = providers.Should().ContainSingle(p => p.Id == "remote").Subject;
        remote.Settings["ClientSecret"].Should().Be("secret");
        (await fileSystem.FileExistsAsync(settingsPath)).Should().BeFalse();
        (await fileSystem.FileExistsAsync(providerSettingsPath)).Should().BeFalse();
        (await vaultStore.GetAsync(LocalVaultScopes.CloudProvider, "/", "providers")).Should().NotBeNull();
        (await vaultStore.GetAsync(LocalVaultScopes.CloudProvider, "/providers", "settings-remote")).Should().NotBeNull();
    }

    private static CloudSecretsService CreateService(
        InMemorySecureStorage? secureStorage = null,
        InMemoryFileSystem? fileSystem = null,
        ILocalVaultStore? vaultStore = null,
        ILocalVaultAccessService? localVaultAccess = null)
    {
        var logger = new TestLogger();
        return new CloudSecretsService(
            secureStorage ?? new InMemorySecureStorage(),
            fileSystem ?? new InMemoryFileSystem(),
            logger,
            new CloudSecretsProviderFactory(logger, new TestLocalSecretsKeyStore(), vaultStore),
            vaultStore,
            localVaultAccess);
    }

    private static SqlCipherLocalVaultStore CreateVaultStore()
    {
        var testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        return new SqlCipherLocalVaultStore(
            new TestLocalSecretsKeyStore(),
            new TestLogger(),
            new LocalVaultOptions(Path.Combine(testDir, "vault.db")));
    }

    private sealed class TestLocalSecretsKeyStore : ILocalSecretsKeyStore
    {
        public Task<string> GetOrCreateKeyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("test-key");
    }

    private sealed class TestLocalVaultAccessService(LocalVaultAccessState state) : ILocalVaultAccessService
    {
        public LocalVaultAccessState GetState() => state;

        public Task<LocalVaultAccessState> RequestAccessAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(state);

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
            => throw new LocalVaultUnavailableException("Vault unavailable");

        public Task<LocalVaultItem?> GetAsync(
            string scope,
            string path,
            string key,
            CancellationToken cancellationToken = default)
            => throw new LocalVaultUnavailableException("Vault unavailable");

        public Task<bool> RemoveAsync(
            string scope,
            string path,
            string key,
            CancellationToken cancellationToken = default)
            => throw new LocalVaultUnavailableException("Vault unavailable");

        public Task<bool> ExistsAsync(
            string scope,
            string path,
            string key,
            CancellationToken cancellationToken = default)
            => throw new LocalVaultUnavailableException("Vault unavailable");

        public Task<IReadOnlyList<LocalVaultItem>> ListAsync(
            string scope,
            string? path = null,
            string? keyPrefix = null,
            CancellationToken cancellationToken = default)
            => throw new LocalVaultUnavailableException("Vault unavailable");
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
        private readonly Dictionary<string, string> _files = new();
        private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, string> Files => _files;

        public Task<string?> ReadFileAsync(string path)
            => Task.FromResult(_files.TryGetValue(path, out var content) ? content : null);

        public Task WriteFileAsync(string path, string content)
        {
            _files[path] = content;
            return Task.CompletedTask;
        }

        public Task<bool> FileExistsAsync(string path) => Task.FromResult(_files.ContainsKey(path));

        public Task<bool> DirectoryExistsAsync(string path) => Task.FromResult(_directories.Contains(path));

        public Task<IReadOnlyList<string>> GetFilesAsync(string path, string searchPattern = "*")
            => Task.FromResult<IReadOnlyList<string>>(_files.Keys.Where(x => x.StartsWith(path, StringComparison.Ordinal)).ToList());

        public Task CreateDirectoryAsync(string path)
        {
            _directories.Add(path);
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string path)
        {
            _files.Remove(path);
            return Task.CompletedTask;
        }

        public Task DeleteDirectoryAsync(string path)
        {
            _directories.Remove(path);
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
