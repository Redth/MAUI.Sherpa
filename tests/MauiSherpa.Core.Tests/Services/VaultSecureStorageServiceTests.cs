using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class VaultSecureStorageServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public VaultSecureStorageServiceTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task GetAsync_MigratesLegacyValueIntoVaultAndRemovesLegacyCopy()
    {
        var legacy = new InMemoryLegacySecureStorage();
        await legacy.SetAsync("cloud/provider:secret", "secret-value");
        var service = CreateService(legacy);

        var value = await service.GetAsync("cloud/provider:secret");
        var secondRead = await service.GetAsync("cloud/provider:secret");

        value.Should().Be("secret-value");
        secondRead.Should().Be("secret-value");
        legacy.Values.Should().NotContainKey("cloud/provider:secret");
    }

    [Fact]
    public async Task SetAsync_WritesVaultAndRemovesLegacyCopy()
    {
        var legacy = new InMemoryLegacySecureStorage();
        await legacy.SetAsync("api-key", "old-value");
        var service = CreateService(legacy);

        await service.SetAsync("api-key", "new-value");
        var value = await service.GetAsync("api-key");

        value.Should().Be("new-value");
        legacy.Values.Should().NotContainKey("api-key");
    }

    [Fact]
    public async Task RemoveAsync_RemovesVaultAndLegacyValues()
    {
        var legacy = new InMemoryLegacySecureStorage();
        await legacy.SetAsync("api-key", "old-value");
        var service = CreateService(legacy);
        await service.SetAsync("api-key", "new-value");

        await service.RemoveAsync("api-key");

        (await service.GetAsync("api-key")).Should().BeNull();
        legacy.Values.Should().NotContainKey("api-key");
    }

    [Fact]
    public async Task BeforeIntroEnabled_UsesLegacySecureStorageOnly()
    {
        var legacy = new InMemoryLegacySecureStorage();
        await legacy.SetAsync("api-key", "legacy-value");
        var service = new VaultSecureStorageService(
            new ThrowingLocalVaultStore(),
            legacy,
            new TestLogger(),
            new TestLocalVaultIntroductionService(LocalVaultIntroductionState.NotShown));

        var value = await service.GetAsync("api-key");
        await service.SetAsync("new-key", "new-value");
        await service.RemoveAsync("api-key");

        value.Should().Be("legacy-value");
        legacy.Values.Should().NotContainKey("api-key");
        legacy.Values["new-key"].Should().Be("new-value");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private VaultSecureStorageService CreateService(InMemoryLegacySecureStorage legacy)
    {
        var vaultStore = new SqlCipherLocalVaultStore(
            new TestLocalVaultKeyStore(),
            new TestLogger(),
            new LocalVaultOptions(Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.db")));

        return new VaultSecureStorageService(vaultStore, legacy, new TestLogger());
    }

    private sealed class InMemoryLegacySecureStorage : ILegacySecureStorageService
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
