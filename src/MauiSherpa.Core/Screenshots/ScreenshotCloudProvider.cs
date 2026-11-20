using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Screenshots;

internal sealed class ScreenshotCloudProvider(
    ScreenshotMode mode, CloudSecretsProviderConfig config, ScreenshotFixtures fixtures) : ICloudSecretsProvider
{
    public CloudSecretsProviderType ProviderType => config.ProviderType;
    public string DisplayName => config.Name;
    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) => throw mode.Blocked("provider connection test");
    public Task<bool> StoreSecretAsync(string key, byte[] value, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) => throw mode.Blocked("secret write");
    public Task<bool> SetSecretMetadataAsync(string key, Dictionary<string, string> metadata, CancellationToken cancellationToken = default) => throw mode.Blocked("secret metadata write");
    public Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default) => throw mode.Blocked("secret deletion");
    public Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(fixtures.GetSecretValue(key));
    public Task<Dictionary<string, string>?> GetSecretMetadataAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(fixtures.Secrets.SingleOrDefault(s => s.Key == key)?.Metadata);
    public Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(fixtures.Secrets.Any(s => s.Key == key));
    public Task<IReadOnlyList<string>> ListSecretsAsync(string? prefix = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(fixtures.Secrets.Where(s => prefix is null || s.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(s => s.Key).ToArray());
}
