using FluentAssertions;
using System.Text;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Shiny.DocumentDb;
using Shiny.DocumentDb.Sqlite.SqlCipher;

namespace MauiSherpa.Core.Tests.Services;

public class LocalSqlCipherSecretsProviderTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public LocalSqlCipherSecretsProviderTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task StoreGetListAndDelete_RoundTripsSecret()
    {
        var provider = CreateProvider();
        var value = Encoding.UTF8.GetBytes("secret-value");

        var stored = await provider.StoreSecretAsync("sherpa-secrets/api-key", value, new Dictionary<string, string>
        {
            ["kind"] = "test"
        });
        var exists = await provider.SecretExistsAsync("sherpa-secrets/api-key");
        var retrieved = await provider.GetSecretAsync("sherpa-secrets/api-key");
        var listed = await provider.ListSecretsAsync("sherpa-secrets/");
        var deleted = await provider.DeleteSecretAsync("sherpa-secrets/api-key");
        var afterDelete = await provider.GetSecretAsync("sherpa-secrets/api-key");

        stored.Should().BeTrue();
        exists.Should().BeTrue();
        retrieved.Should().BeEquivalentTo(value);
        listed.Should().ContainSingle().Which.Should().Be("sherpa-secrets/api-key");
        deleted.Should().BeTrue();
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task Metadata_RoundTripsAndUpdatesIndependently()
    {
        var provider = CreateProvider();
        var value = Encoding.UTF8.GetBytes("secret-value");

        await provider.StoreSecretAsync("sherpa-secrets/api-key", value, new Dictionary<string, string>
        {
            ["environment/name"] = "prod",
            ["owner=email"] = "team@example.com"
        });

        var createdMetadata = await provider.GetSecretMetadataAsync("sherpa-secrets/api-key");
        var updated = await provider.SetSecretMetadataAsync("sherpa-secrets/api-key", new Dictionary<string, string>
        {
            ["environment/name"] = "stage"
        });
        var updatedMetadata = await provider.GetSecretMetadataAsync("sherpa-secrets/api-key");
        var retrieved = await provider.GetSecretAsync("sherpa-secrets/api-key");
        await provider.StoreSecretAsync("sherpa-secrets/api-key", Encoding.UTF8.GetBytes("new-value"));
        var preservedMetadata = await provider.GetSecretMetadataAsync("sherpa-secrets/api-key");

        createdMetadata.Should().ContainKey("environment/name").WhoseValue.Should().Be("prod");
        createdMetadata.Should().ContainKey("owner=email").WhoseValue.Should().Be("team@example.com");
        updated.Should().BeTrue();
        updatedMetadata.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("environment/name", "stage"));
        retrieved.Should().BeEquivalentTo(value);
        preservedMetadata.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("environment/name", "stage"));
    }

    [Fact]
    public async Task SameKey_ReopensExistingDatabase()
    {
        var keyStore = new TestLocalSecretsKeyStore("same-key");
        var databasePath = GetDatabasePath();
        var first = CreateProvider(keyStore, databasePath);

        await first.StoreSecretAsync("key", Encoding.UTF8.GetBytes("value"));

        var second = CreateProvider(keyStore, databasePath);
        var value = await second.GetSecretAsync("key");

        Encoding.UTF8.GetString(value!).Should().Be("value");
    }

    [Fact]
    public async Task KeyStoreFailure_FailsClearly()
    {
        var provider = CreateProvider(new FailingLocalSecretsKeyStore(), GetDatabasePath());

        var tested = await provider.TestConnectionAsync();
        var stored = await provider.StoreSecretAsync("key", Encoding.UTF8.GetBytes("value"));

        tested.Should().BeFalse();
        stored.Should().BeFalse();
    }

    [Fact]
    public async Task LegacyLocalSecretsDatabase_IsMigratedIntoSharedVault()
    {
        var keyStore = new TestLocalSecretsKeyStore("legacy-key");
        var legacyDatabasePath = GetDatabasePath();
        var vaultDatabasePath = GetDatabasePath();
        var legacyStore = new DocumentStore(new DocumentStoreOptions
        {
            DatabaseProvider = new SqlCipherDatabaseProvider(legacyDatabasePath, "legacy-key")
        });
        await legacyStore.Insert(new LocalSqlCipherSecretsProvider.LocalSecretDocument
        {
            Id = "legacy-id",
            Key = "sherpa-secrets/api-key",
            Value = Encoding.UTF8.GetBytes("legacy-value"),
            Metadata = new Dictionary<string, string> { ["source"] = "legacy" },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        var vaultStore = new SqlCipherLocalVaultStore(
            keyStore,
            new TestLogger(),
            new LocalVaultOptions(vaultDatabasePath));
        var config = new CloudSecretsProviderConfig(
            "local",
            "Local",
            CloudSecretsProviderType.Local,
            new Dictionary<string, string>
            {
                [LocalSqlCipherSecretsProvider.DatabasePathSettingKey] = legacyDatabasePath
            });
        var provider = new LocalSqlCipherSecretsProvider(config, new TestLogger(), vaultStore, keyStore);

        var retrieved = await provider.GetSecretAsync("sherpa-secrets/api-key");
        var listed = await provider.ListSecretsAsync("sherpa-secrets/");
        var migratedItem = await vaultStore.GetAsync(LocalVaultScopes.LocalProviderSecret, "/sherpa-secrets", "api-key");

        Encoding.UTF8.GetString(retrieved!).Should().Be("legacy-value");
        listed.Should().ContainSingle().Which.Should().Be("sherpa-secrets/api-key");
        migratedItem.Should().NotBeNull();
        migratedItem!.Metadata["source"].Should().Be("legacy");
        File.Exists(legacyDatabasePath).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private LocalSqlCipherSecretsProvider CreateProvider(
        ILocalSecretsKeyStore? keyStore = null,
        string? databasePath = null)
    {
        var config = new CloudSecretsProviderConfig(
            "local",
            "Local",
            CloudSecretsProviderType.Local,
            new Dictionary<string, string>
            {
                [LocalSqlCipherSecretsProvider.DatabasePathSettingKey] = databasePath ?? GetDatabasePath()
            });

        return new LocalSqlCipherSecretsProvider(
            config,
            new TestLogger(),
            keyStore ?? new TestLocalSecretsKeyStore("test-key"));
    }

    private string GetDatabasePath() => Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.db");

    private sealed class TestLocalSecretsKeyStore : ILocalSecretsKeyStore
    {
        private readonly string _key;

        public TestLocalSecretsKeyStore(string key)
        {
            _key = key;
        }

        public Task<string> GetOrCreateKeyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_key);
    }

    private sealed class FailingLocalSecretsKeyStore : ILocalSecretsKeyStore
    {
        public Task<string> GetOrCreateKeyAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Secure storage unavailable");
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
