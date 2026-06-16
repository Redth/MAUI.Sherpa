using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class CloudSecretsProviderFactoryTests
{
    [Fact]
    public void SupportedProviders_IncludesLocalFirst()
    {
        var factory = new CloudSecretsProviderFactory(new TestLogger(), new TestLocalSecretsKeyStore());

        factory.SupportedProviders[0].Should().Be(CloudSecretsProviderType.Local);
        factory.SupportedProviders.Should().Contain(CloudSecretsProviderType.Local);
    }

    [Fact]
    public void GetProviderDisplayName_Local_ReturnsLocal()
    {
        var factory = new CloudSecretsProviderFactory(new TestLogger(), new TestLocalSecretsKeyStore());

        factory.GetProviderDisplayName(CloudSecretsProviderType.Local).Should().Be("Local");
    }

    [Fact]
    public void GetProviderSettings_Local_HasNoRequiredSettings()
    {
        var factory = new CloudSecretsProviderFactory(new TestLogger(), new TestLocalSecretsKeyStore());

        factory.GetProviderSettings(CloudSecretsProviderType.Local).Should().BeEmpty();
    }

    [Fact]
    public void CreateProvider_Local_ReturnsLocalProvider()
    {
        var factory = new CloudSecretsProviderFactory(new TestLogger(), new TestLocalSecretsKeyStore());
        var config = new CloudSecretsProviderConfig("local", "Local", CloudSecretsProviderType.Local, new());

        var provider = factory.CreateProvider(config);

        provider.Should().BeOfType<LocalSqlCipherSecretsProvider>();
        provider.ProviderType.Should().Be(CloudSecretsProviderType.Local);
        provider.DisplayName.Should().Be("Local");
    }

    [Fact]
    public void CreateProvider_LocalWithoutKeyStore_Throws()
    {
        var factory = new CloudSecretsProviderFactory(new TestLogger());
        var config = new CloudSecretsProviderConfig("local", "Local", CloudSecretsProviderType.Local, new());

        var act = () => factory.CreateProvider(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Local secrets key storage*");
    }

    private sealed class TestLocalSecretsKeyStore : ILocalSecretsKeyStore
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
