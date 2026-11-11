using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class CertificateItemAdapterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderEnumerationAndRead_RecognizeNativeKeysAndLeadingZeros(bool azure)
    {
        var separator = azure ? "-" : "_";
        var p12Key = $"CERT{separator}00ABC123{separator}P12";
        var passwordKey = $"CERT{separator}00ABC123{separator}PWD";
        var metadataKey = $"CERT{separator}00ABC123{separator}META";
        var metadata = new CertificateSecretMetadata(
            "cert-1", "ABC123", "Signing certificate", "Development", DateTime.UtcNow.AddDays(1), "machine", DateTime.UtcNow);
        var secrets = new Dictionary<string, byte[]>
        {
            [p12Key] = [1, 2, 3],
            [passwordKey] = Encoding.UTF8.GetBytes("password"),
            [metadataKey] = JsonSerializer.SerializeToUtf8Bytes(metadata),
            [$"CERT{separator}ORPHAN{separator}META"] = JsonSerializer.SerializeToUtf8Bytes(metadata)
        };
        var (adapter, _) = CreateAdapter(secrets, azure);

        var items = await adapter.ListProviderItemsAsync("provider");
        var placements = await adapter.ListProviderItemIdsAsync("provider");
        var item = Assert.Single(items);
        var payload = await adapter.ReadProviderAsync(item, "provider");

        Assert.Equal("ABC123", item.Id);
        Assert.Equal("Signing certificate", item.DisplayName);
        Assert.Equal(new[] { "ABC123" }, placements);
        Assert.NotNull(payload);
        Assert.Equal(item.Id, payload.Item.Id);
        Assert.Equal(new[] { "CERT_ABC123_META", "CERT_ABC123_P12", "CERT_ABC123_PWD" },
            payload.Artifacts.Select(artifact => artifact.Key));
        Assert.Equal(secrets[p12Key], payload.Artifacts.Single(artifact => artifact.Key.EndsWith("_P12")).Value);
        Assert.Equal(secrets[passwordKey], payload.Artifacts.Single(artifact => artifact.Key.EndsWith("_PWD")).Value);
    }

    [Fact]
    public async Task ReadProviderAsync_MismatchedMetadata_DoesNotChangeCertificateId()
    {
        var metadata = new CertificateSecretMetadata(
            "other-id", "DEF456", "Other certificate", "Development", DateTime.UtcNow.AddDays(1), "machine", DateTime.UtcNow);
        var (adapter, _) = CreateAdapter(new Dictionary<string, byte[]>
        {
            ["CERT_ABC123_P12"] = [1, 2, 3],
            ["CERT_ABC123_META"] = JsonSerializer.SerializeToUtf8Bytes(metadata)
        }, azure: false);
        var item = new SecretItemRef(SecretItemKind.Certificate, "ABC123", "Selected certificate");

        var payload = await adapter.ReadProviderAsync(item, "provider");

        Assert.NotNull(payload);
        Assert.Equal(item, payload.Item);
        var storedMetadata = JsonSerializer.Deserialize<CertificateSecretMetadata>(
            payload.Artifacts.Single(artifact => artifact.Key.EndsWith("_META")).Value);
        Assert.Equal("ABC123", storedMetadata?.SerialNumber);
    }

    [Fact]
    public async Task ListProviderItemsAsync_WhenProviderIsUnavailable_ReportsFailure()
    {
        var registry = new Mock<ISecretsProviderRegistry>();
        var adapter = new CertificateItemAdapter(registry.Object, new Mock<ILocalCertificateService>().Object);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.ListProviderItemsAsync("missing"));

        Assert.Contains("unavailable", exception.Message);
    }

    [Theory]
    [InlineData("CERT/ABC123/P12")]
    [InlineData("CERT.ABC123.P12")]
    [InlineData("CERT_ABC/123_P12")]
    public void CertificateKeyParser_RejectsOtherSecretNames(string key)
    {
        Assert.False(CertificateSecretKeys.TryExtractSerial(key, CertificateSecretKeys.P12Suffix, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteProviderAsync_RemovesAllMatchingLegacyKeys(bool azure)
    {
        var separator = azure ? "-" : "_";
        var secrets = new Dictionary<string, byte[]>
        {
            [$"CERT{separator}ABC123{separator}P12"] = [1],
            [$"CERT{separator}00ABC123{separator}P12"] = [2],
            [$"CERT{separator}00ABC123{separator}PWD"] = [3],
            [$"CERT{separator}00ABC123{separator}META"] = [4],
            [$"CERT{separator}DEF456{separator}P12"] = [5]
        };
        var (adapter, provider) = CreateAdapter(secrets, azure);
        provider.Setup(x => x.DeleteSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => secrets.Remove(key));

        var success = await adapter.DeleteProviderAsync(
            new SecretItemRef(SecretItemKind.Certificate, "ABC123", "Signing certificate"), "provider");

        Assert.True(success);
        Assert.Equal(new[] { $"CERT{separator}DEF456{separator}P12" }, secrets.Keys);
        provider.Verify(x => x.SecretExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteProviderAsync_WhenDeletionFails_ReportsFailure()
    {
        var (adapter, provider) = CreateAdapter(new Dictionary<string, byte[]>
        {
            ["CERT_ABC123_P12"] = [1]
        }, azure: false);
        provider.Setup(x => x.DeleteSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var success = await adapter.DeleteProviderAsync(
            new SecretItemRef(SecretItemKind.Certificate, "ABC123", "Signing certificate"), "provider");

        Assert.False(success);
    }

    [Fact]
    public async Task DeleteProviderAsync_WhenListingFails_ReportsFailure()
    {
        var (adapter, provider) = CreateAdapter(new Dictionary<string, byte[]>(), azure: false);
        provider.Setup(x => x.ListSecretsStrictAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Provider unavailable"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.DeleteProviderAsync(new SecretItemRef(SecretItemKind.Certificate, "ABC123", "Signing certificate"), "provider"));

        Assert.Contains("Provider unavailable", exception.Message);
        provider.Verify(x => x.DeleteSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteProviderAsync_WhenListingSwallowsCancellation_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var (adapter, provider) = CreateAdapter(new Dictionary<string, byte[]>(), azure: false);
        provider.Setup(x => x.ListSecretsStrictAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, CancellationToken _) =>
            {
                cancellation.Cancel();
                return Array.Empty<string>();
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.DeleteProviderAsync(
                new SecretItemRef(SecretItemKind.Certificate, "ABC123", "Signing certificate"),
                "provider",
                cancellation.Token));
    }

    private static (CertificateItemAdapter Adapter, Mock<ICloudSecretsProvider> Provider) CreateAdapter(
        Dictionary<string, byte[]> secrets,
        bool azure)
    {
        var provider = new Mock<ICloudSecretsProvider>();
        provider.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
            {
                var storedKey = azure ? key.Replace('_', '-') : key;
                return secrets.TryGetValue(storedKey, out var value) ? value : null;
            });
        provider.Setup(x => x.ListSecretsStrictAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? prefix, CancellationToken _) =>
            {
                var storedPrefix = azure ? prefix?.Replace('_', '-') : prefix;
                return secrets.Keys.Where(key =>
                    storedPrefix is null || key.StartsWith(storedPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
            });
        var registry = new Mock<ISecretsProviderRegistry>();
        registry.Setup(x => x.GetProviderAsync("provider")).ReturnsAsync(provider.Object);
        return (new CertificateItemAdapter(registry.Object, new Mock<ILocalCertificateService>().Object), provider);
    }
}
