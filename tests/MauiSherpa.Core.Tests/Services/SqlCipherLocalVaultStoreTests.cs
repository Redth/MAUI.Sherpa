using System.Text;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class SqlCipherLocalVaultStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public SqlCipherLocalVaultStoreTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task PutGetListRemove_RoundTripsVaultItem()
    {
        var store = CreateStore();
        var value = Encoding.UTF8.GetBytes("secret-value");

        var saved = await store.PutAsync(
            LocalVaultScopes.LocalProviderSecret,
            "/managed",
            "api-key",
            value,
            LocalVaultContentTypes.Binary,
            new Dictionary<string, string> { ["kind"] = "test" });

        var loaded = await store.GetAsync(LocalVaultScopes.LocalProviderSecret, "managed", "api-key");
        var listed = await store.ListAsync(LocalVaultScopes.LocalProviderSecret, "/managed");
        var exists = await store.ExistsAsync(LocalVaultScopes.LocalProviderSecret, "/managed", "api-key");
        var removed = await store.RemoveAsync(LocalVaultScopes.LocalProviderSecret, "/managed", "api-key");
        var afterRemove = await store.GetAsync(LocalVaultScopes.LocalProviderSecret, "/managed", "api-key");

        saved.Id.Should().Be(LocalVaultItem.CreateId(LocalVaultScopes.LocalProviderSecret, "/managed", "api-key"));
        loaded.Should().NotBeNull();
        loaded!.Value.Should().BeEquivalentTo(value);
        loaded.Metadata["kind"].Should().Be("test");
        listed.Should().ContainSingle(x => x.Key == "api-key");
        exists.Should().BeTrue();
        removed.Should().BeTrue();
        afterRemove.Should().BeNull();
    }

    [Fact]
    public async Task PutAsync_SamePathAndKeyDifferentScopes_DoNotCollide()
    {
        var store = CreateStore();

        await store.PutAsync(LocalVaultScopes.Settings, "/", "shared", Encoding.UTF8.GetBytes("settings"), LocalVaultContentTypes.Text);
        await store.PutAsync(LocalVaultScopes.SecureStorage, "/", "shared", Encoding.UTF8.GetBytes("secure"), LocalVaultContentTypes.Text);

        var settings = await store.GetAsync(LocalVaultScopes.Settings, "/", "shared");
        var secure = await store.GetAsync(LocalVaultScopes.SecureStorage, "/", "shared");

        Encoding.UTF8.GetString(settings!.Value).Should().Be("settings");
        Encoding.UTF8.GetString(secure!.Value).Should().Be("secure");
        settings.Id.Should().NotBe(secure.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private SqlCipherLocalVaultStore CreateStore(string key = "test-key")
    {
        return new SqlCipherLocalVaultStore(
            new TestLocalVaultKeyStore(key),
            new TestLogger(),
            new LocalVaultOptions(Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.db")));
    }

    private sealed class TestLocalVaultKeyStore : ILocalVaultKeyStore
    {
        private readonly string _key;

        public TestLocalVaultKeyStore(string key)
        {
            _key = key;
        }

        public Task<string> GetOrCreateKeyAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_key);
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
