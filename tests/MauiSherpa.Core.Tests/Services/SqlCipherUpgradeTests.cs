using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Microsoft.Data.Sqlite;
using Moq;
using Shiny.DocumentDb.Sqlite.SqlCipher;

namespace MauiSherpa.Core.Tests.Services;

public sealed class SqlCipherUpgradeTests : IDisposable
{
    private const string FixtureKey = "sherpa-5.2.2-synthetic-fixture-key";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly List<SqlCipherLocalVaultStore> _stores = [];
    private readonly List<SqlCipherDatabaseProvider> _providers = [];
    private readonly Mock<ILoggingService> _logger = new();

    public SqlCipherUpgradeTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Vault522_ReadsEveryScopeAndField_AfterReopening()
    {
        var path = CopyFixture("local-vault.db");
        var expected = ReadExpected<LocalVaultItem>("local-vault.db");
        var store = CreateStore(path);

        await AssertVaultAsync(store, expected);
        store.Dispose();

        await AssertVaultAsync(CreateStore(path), expected);
    }

    [Fact]
    public async Task Vault522_UpdateInsertDelete_PersistWithoutChangingOtherRecords()
    {
        var path = CopyFixture("local-vault.db");
        var expected = ReadExpected<LocalVaultItem>("local-vault.db");
        var store = CreateStore(path);
        var original = expected.First(x => x.Scope == LocalVaultScopes.SecureStorage);
        var updated = await store.PutAsync(original.Scope, original.Path, original.Key,
            Encoding.UTF8.GetBytes("updated"), LocalVaultContentTypes.Text,
            new Dictionary<string, string> { ["updated"] = "true" });
        updated.CreatedAt.Should().Be(original.CreatedAt);
        updated.UpdatedAt.Should().BeAfter(original.UpdatedAt);
        expected[expected.IndexOf(original)] = updated;

        expected.Add(await store.PutAsync(LocalVaultScopes.SecureStorage, "/upgrade", "new",
            [0, 255, 128], LocalVaultContentTypes.Binary));
        var removed = expected.Single(x => x.Key == "empty");
        (await store.RemoveAsync(removed.Scope, removed.Path, removed.Key)).Should().BeTrue();
        expected.Remove(removed);
        store.Dispose();

        var reopened = CreateStore(path);
        await AssertVaultAsync(reopened, expected);
        (await reopened.GetAsync(removed.Scope, removed.Path, removed.Key)).Should().BeNull();
    }

    [Fact]
    public async Task LocalSecrets522_MigratesIntoExisting522Vault_AndSurvivesRestart()
    {
        var legacyPath = CopyFixture("local-secrets.db");
        var vaultPath = CopyFixture("local-vault.db");
        var expectedVault = ReadExpected<LocalVaultItem>("local-vault.db");
        var expectedSecrets = ReadExpected<LocalSqlCipherSecretsProvider.LocalSecretDocument>("local-secrets.db");
        var vault = CreateStore(vaultPath);
        var provider = CreateProvider(legacyPath, vault, FixtureKey);

        foreach (var expected in expectedSecrets)
        {
            (await provider.GetSecretAsync(expected.Key)).Should().Equal(expected.Value);
            var secretPath = SecretPath.FromFlatKey(expected.Key);
            var migrated = await vault.GetAsync(LocalVaultScopes.LocalProviderSecret, secretPath.FolderPath, secretPath.Key);
            migrated.Should().NotBeNull();
            migrated!.Metadata.Should().BeEquivalentTo(new Dictionary<string, string>(expected.Metadata)
            {
                ["OriginalFlatKey"] = expected.Key,
                ["MigratedFrom"] = "local-secrets.db"
            });
        }
        (await provider.ListSecretsAsync()).Should().Contain(expectedSecrets.Select(x => x.Key));
        foreach (var item in expectedVault)
            (await vault.GetAsync(item.Scope, item.Path, item.Key)).Should().BeEquivalentTo(item);

        var marker = await vault.GetAsync(LocalVaultScopes.Migration, "/", "local-provider-legacy-db");
        marker.Should().NotBeNull();
        using var payload = JsonDocument.Parse(marker!.Value);
        payload.RootElement.GetProperty("MigratedCount").GetInt32().Should().Be(expectedSecrets.Count);
        marker.Metadata["SourceDatabaseSha256"].Should().MatchRegex("^[0-9A-F]{64}$");
        File.Exists(legacyPath).Should().BeFalse();
        File.Exists(legacyPath + "-wal").Should().BeFalse();
        File.Exists(legacyPath + "-shm").Should().BeFalse();
        vault.Dispose();

        var reopenedVault = CreateStore(vaultPath);
        var reopened = CreateProvider(legacyPath, reopenedVault, FixtureKey);
        foreach (var expected in expectedSecrets)
            (await reopened.GetSecretAsync(expected.Key)).Should().Equal(expected.Value);

        (await reopened.StoreSecretAsync(expectedSecrets[0].Key, [42])).Should().BeTrue();
        // A restored legacy file must not rerun a completed migration and overwrite newer values.
        File.Copy(FixturePath("local-secrets.db"), legacyPath);
        var restarted = CreateProvider(legacyPath, reopenedVault, FixtureKey);
        (await restarted.GetSecretAsync(expectedSecrets[0].Key)).Should().Equal(new byte[] { 42 });
        File.Exists(legacyPath).Should().BeFalse();
    }

    [Fact]
    public async Task CompletedMigration_DoesNotDeleteDifferentRestoredDatabase()
    {
        var legacyPath = CopyFixture("local-secrets.db");
        var vault = CreateStore(Path.Combine(_directory, "new-vault.db"));
        var provider = CreateProvider(legacyPath, vault, FixtureKey);
        (await provider.GetSecretAsync("sherpa-secrets/api-key")).Should().NotBeNull();

        File.Copy(FixturePath("local-secrets.db"), legacyPath);
        await File.WriteAllTextAsync(legacyPath + "-wal", "different restored WAL");
        var restarted = CreateProvider(legacyPath, vault, FixtureKey);

        (await restarted.GetSecretAsync("sherpa-secrets/api-key")).Should().NotBeNull();
        File.Exists(legacyPath).Should().BeTrue();
        File.Exists(legacyPath + "-wal").Should().BeTrue();
        _logger.Verify(x => x.LogWarning(It.Is<string>(message => message.Contains("different legacy"))));
    }

    [Fact]
    public async Task CompletedMigration_RetriesMainDatabaseCleanupAfterWalWasAlreadyDeleted()
    {
        var legacyPath = Path.Combine(_directory, "partial-cleanup.db");
        await File.WriteAllTextAsync(legacyPath, "migrated database");
        await File.WriteAllTextAsync(legacyPath + "-wal", "migrated WAL");
        var vault = CreateStore(Path.Combine(_directory, "new-vault.db"));
        await vault.PutAsync(
            LocalVaultScopes.Migration,
            "/",
            "local-provider-legacy-db",
            [],
            LocalVaultContentTypes.Json,
            new Dictionary<string, string>
            {
                ["SourceDatabaseSha256"] = GetSha256(legacyPath),
                ["SourceWalSha256"] = GetSha256(legacyPath + "-wal")
            });
        File.Delete(legacyPath + "-wal");
        var provider = CreateProvider(legacyPath, vault, FixtureKey);

        (await provider.GetSecretAsync("missing")).Should().BeNull();
        File.Exists(legacyPath).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Vault522_WrongKeyOrCorruption_FailsWithoutReplacingDatabase(bool corrupt)
    {
        var path = CopyFixture("local-vault.db");
        if (corrupt)
            CorruptHeader(path);
        var before = File.ReadAllBytes(path);
        var store = CreateStore(path, corrupt ? FixtureKey : "wrong-key");

        var read = () => store.ListAsync(LocalVaultScopes.Settings);
        await read.Should().ThrowAsync<SqliteException>();
        store.Dispose();
        File.ReadAllBytes(path).Should().Equal(before);

        if (!corrupt)
            await AssertVaultAsync(CreateStore(path), ReadExpected<LocalVaultItem>("local-vault.db"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalSecrets522_FailedMigration_PreservesSourceAndDoesNotReportMissingSecrets(bool corrupt)
    {
        var legacyPath = CopyFixture("local-secrets.db");
        if (corrupt)
            CorruptHeader(legacyPath);
        var before = File.ReadAllBytes(legacyPath);
        var vault = CreateStore(Path.Combine(_directory, "new-vault.db"));
        var provider = CreateProvider(legacyPath, vault, corrupt ? FixtureKey : "wrong-key");

        var read = () => provider.GetSecretAsync("sherpa-secrets/api-key");
        await read.Should().ThrowAsync<SqliteException>();
        var exists = () => provider.SecretExistsAsync("sherpa-secrets/api-key");
        await exists.Should().ThrowAsync<SqliteException>();
        var list = () => provider.ListSecretsAsync();
        await list.Should().ThrowAsync<SqliteException>();
        var metadata = () => provider.GetSecretMetadataAsync("sherpa-secrets/api-key");
        await metadata.Should().ThrowAsync<SqliteException>();
        (await provider.TestConnectionAsync()).Should().BeFalse();
        (await provider.StoreSecretAsync("new-key", [42])).Should().BeFalse();
        File.ReadAllBytes(legacyPath).Should().Equal(before);
        (await vault.ExistsAsync(LocalVaultScopes.Migration, "/", "local-provider-legacy-db")).Should().BeFalse();
        (await vault.ListAsync(LocalVaultScopes.LocalProviderSecret)).Should().BeEmpty();

        if (!corrupt)
        {
            var retry = CreateProvider(legacyPath, vault, FixtureKey);
            (await retry.GetSecretAsync("sherpa-secrets/api-key")).Should()
                .Equal(ReadExpected<LocalSqlCipherSecretsProvider.LocalSecretDocument>("local-secrets.db")[0].Value);
            File.Exists(legacyPath).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LocalSecrets522_PartialMigrationFailure_RetainsSourceAndRetries(bool failWrite)
    {
        var legacyPath = CopyFixture("local-secrets.db");
        var before = File.ReadAllBytes(legacyPath);
        var vault = CreateStore(Path.Combine(_directory, "new-vault.db"));
        var failingVault = new Mock<ILocalVaultStore>();
        failingVault.SetupGet(x => x.DatabasePath).Returns(vault.DatabasePath);
        failingVault.Setup(x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string scope, string path, string key, CancellationToken ct) => vault.ExistsAsync(scope, path, key, ct));
        var shouldFail = true;
        failingVault.Setup(x => x.PutAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Returns((string scope, string path, string key, byte[] value, string contentType, Dictionary<string, string>? metadata, CancellationToken ct) =>
            {
                if (shouldFail && failWrite && key == "empty")
                    throw new IOException("Simulated disk write failure");
                return vault.PutAsync(scope, path, key, value, contentType, metadata, ct);
            });
        failingVault.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string scope, string path, string key, CancellationToken ct) =>
                shouldFail && !failWrite && key == "empty"
                    ? Task.FromResult<LocalVaultItem?>(null)
                    : vault.GetAsync(scope, path, key, ct));
        var provider = CreateProvider(legacyPath, failingVault.Object, FixtureKey);

        var read = () => provider.GetSecretAsync("sherpa-secrets/api-key");
        if (failWrite)
            await read.Should().ThrowAsync<IOException>();
        else
            await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*verify*");

        File.ReadAllBytes(legacyPath).Should().Equal(before);
        (await vault.GetAsync(LocalVaultScopes.LocalProviderSecret, "/sherpa-secrets", "api-key")).Should().NotBeNull();
        (await vault.ExistsAsync(LocalVaultScopes.Migration, "/", "local-provider-legacy-db")).Should().BeFalse();

        shouldFail = false;
        foreach (var expected in ReadExpected<LocalSqlCipherSecretsProvider.LocalSecretDocument>("local-secrets.db"))
            (await provider.GetSecretAsync(expected.Key)).Should().Equal(expected.Value);
        File.Exists(legacyPath).Should().BeFalse();
        (await vault.ExistsAsync(LocalVaultScopes.Migration, "/", "local-provider-legacy-db")).Should().BeTrue();
    }

    private static async Task AssertVaultAsync(SqlCipherLocalVaultStore store, List<LocalVaultItem> expected)
    {
        foreach (var item in expected)
            (await store.GetAsync(item.Scope, item.Path, item.Key)).Should().BeEquivalentTo(item);
        foreach (var scope in expected.Select(x => x.Scope).Distinct())
            (await store.ListAsync(scope)).Should().BeEquivalentTo(expected.Where(x => x.Scope == scope));
    }

    private SqlCipherLocalVaultStore CreateStore(string path, string key = FixtureKey)
    {
        var store = new SqlCipherLocalVaultStore(CreateKeyStore(key), _logger.Object, new LocalVaultOptions(path));
        _stores.Add(store);
        _providers.Add(new SqlCipherDatabaseProvider(path, key));
        return store;
    }

    private LocalSqlCipherSecretsProvider CreateProvider(string legacyPath, ILocalVaultStore vault, string key)
    {
        var config = new CloudSecretsProviderConfig("local", "Local", CloudSecretsProviderType.Local,
            new Dictionary<string, string> { [LocalSqlCipherSecretsProvider.DatabasePathSettingKey] = legacyPath });
        return new LocalSqlCipherSecretsProvider(config, _logger.Object, vault, CreateKeyStore(key));
    }

    private static ILocalVaultKeyStore CreateKeyStore(string key)
    {
        var keyStore = new Mock<ILocalVaultKeyStore>();
        keyStore.Setup(x => x.GetOrCreateKeyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(key);
        return keyStore.Object;
    }

    private string CopyFixture(string name)
    {
        var path = Path.Combine(_directory, name);
        File.Copy(FixturePath(name), path);
        Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 16).Should().NotBe("SQLite format 3\0");
        return path;
    }

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "SqlCipher522", name);

    private static List<T> ReadExpected<T>(string databaseName) =>
        JsonSerializer.Deserialize<List<T>>(File.ReadAllText(FixturePath(databaseName + ".json")))!;

    private static void CorruptHeader(string path)
    {
        var bytes = File.ReadAllBytes(path);
        bytes[0] ^= 0xff;
        File.WriteAllBytes(path, bytes);
    }

    private static string GetSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public void Dispose()
    {
        foreach (var store in _stores)
            store.Dispose();
        foreach (var provider in _providers)
        {
            using var connection = (SqliteConnection)provider.CreateConnection();
            SqliteConnection.ClearPool(connection);
        }
        Directory.Delete(_directory, recursive: true);
    }
}
