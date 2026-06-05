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
