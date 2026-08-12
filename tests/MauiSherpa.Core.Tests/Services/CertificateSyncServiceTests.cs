using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class CertificateSyncServiceTests
{
    readonly Mock<ICloudSecretsService> _cloudSecretsService = new();
    readonly Mock<ILocalCertificateService> _localCertificateService = new();
    readonly Mock<ILoggingService> _logger = new();
    readonly CertificateSyncService _sut;

    public CertificateSyncServiceTests()
    {
        _sut = new CertificateSyncService(_cloudSecretsService.Object, _localCertificateService.Object, _logger.Object);
    }

    [Fact]
    public async Task DownloadAndInstallAsync_NoActiveProvider_ReturnsFalse()
    {
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns((CloudSecretsProviderConfig?)null);

        var result = await _sut.DownloadAndInstallAsync("cert-1");

        Assert.False(result);
    }

    [Fact]
    public async Task DownloadAndInstallAsync_ResolvesCertificateId_AndAttemptsSerialInstall()
    {
        var provider = new CloudSecretsProviderConfig("provider-1", "Provider", CloudSecretsProviderType.OnePassword, new());
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(provider);
        _cloudSecretsService.Setup(x => x.ListSecretsAsync("CERT_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "CERT_ABC123_META", "CERT_ABC123_P12", "CERT_ABC123_PWD" });

        var metadata = new CertificateSecretMetadata(
            CertificateId: "cert-1",
            SerialNumber: "ABC123",
            CommonName: "Test Cert",
            CertificateType: "Development",
            ExpirationDate: DateTime.UtcNow.AddDays(10),
            CreatedByMachine: "machine",
            CreatedAt: DateTime.UtcNow);

        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_META", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata)));
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x01, 0x02, 0x03 });
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("password"));
        _localCertificateService.Setup(x => x.ImportP12Async(
                It.Is<byte[]>(data => data.SequenceEqual(new byte[] { 0x01, 0x02, 0x03 })),
                "password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.DownloadAndInstallAsync("cert-1");

        Assert.True(result);
        _cloudSecretsService.Verify(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()), Times.Once);
        _cloudSecretsService.Verify(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()), Times.Once);
        _localCertificateService.Verify(x => x.ImportP12Async(
            It.Is<byte[]>(data => data.SequenceEqual(new byte[] { 0x01, 0x02, 0x03 })),
            "password",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DownloadAndInstallAsync_WhenCertificateIdNotFound_ReturnsFalse()
    {
        var provider = new CloudSecretsProviderConfig("provider-1", "Provider", CloudSecretsProviderType.OnePassword, new());
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(provider);
        _cloudSecretsService.Setup(x => x.ListSecretsAsync("CERT_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "CERT_ABC123_META" });

        var metadata = new CertificateSecretMetadata(
            CertificateId: "different-id",
            SerialNumber: "ABC123",
            CommonName: "Test Cert",
            CertificateType: "Development",
            ExpirationDate: DateTime.UtcNow.AddDays(10),
            CreatedByMachine: "machine",
            CreatedAt: DateTime.UtcNow);

        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_META", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata)));

        var result = await _sut.DownloadAndInstallAsync("cert-1");

        Assert.False(result);
        _cloudSecretsService.Verify(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()), Times.Never);
        _cloudSecretsService.Verify(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadAndInstallBySerialAsync_DelegatesToLocalCertificateImport()
    {
        var provider = new CloudSecretsProviderConfig("provider-1", "Provider", CloudSecretsProviderType.OnePassword, new());
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(provider);
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x01, 0x02, 0x03 });
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("password"));
        _localCertificateService.Setup(x => x.ImportP12Async(
                It.Is<byte[]>(data => data.SequenceEqual(new byte[] { 0x01, 0x02, 0x03 })),
                "password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.DownloadAndInstallBySerialAsync("ABC123");

        Assert.True(result);
        _localCertificateService.Verify(x => x.ImportP12Async(
            It.Is<byte[]>(data => data.SequenceEqual(new byte[] { 0x01, 0x02, 0x03 })),
            "password",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_ExactCloudKey_ReturnsP12AndPassword()
    {
        var provider = new CloudSecretsProviderConfig("provider-1", "Provider", CloudSecretsProviderType.OnePassword, new());
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(provider);
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x01, 0x02, 0x03 });
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("password"));

        var (p12, password) = await _sut.GetCertificateSecretsAsync("ABC123");

        Assert.NotNull(p12);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, p12);
        Assert.Equal("password", password);
        _cloudSecretsService.Verify(x => x.ListSecretsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_ExactKeyMisses_FuzzyMatchesStoredSerialWithLeadingZeros()
    {
        var provider = new CloudSecretsProviderConfig("provider-1", "Provider", CloudSecretsProviderType.OnePassword, new());
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(provider);
        // Exact key for the requested serial does not exist...
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        // ...but a secret was stored under a differently-normalized serial (leading zeros).
        _cloudSecretsService.Setup(x => x.ListSecretsAsync("CERT_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "CERT_00ABC123_META", "CERT_00ABC123_P12", "CERT_00ABC123_PWD" });
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_00ABC123_P12", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x0A, 0x0B });
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_00ABC123_PWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("secret-pwd"));

        var (p12, password) = await _sut.GetCertificateSecretsAsync("ABC123");

        Assert.NotNull(p12);
        Assert.Equal(new byte[] { 0x0A, 0x0B }, p12);
        Assert.Equal("secret-pwd", password);
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_CloudMisses_FallsBackToLocalKeychainExport()
    {
        var provider = new CloudSecretsProviderConfig("provider-1", "Provider", CloudSecretsProviderType.OnePassword, new());
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(provider);
        _cloudSecretsService.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _cloudSecretsService.Setup(x => x.ListSecretsAsync("CERT_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        _localCertificateService.Setup(x => x.IsSupported).Returns(true);
        _localCertificateService.Setup(x => x.GetSigningIdentitiesAsync())
            .ReturnsAsync(new[]
            {
                new LocalSigningIdentity(
                    Identity: "Apple Distribution: Test (TEAM)",
                    CommonName: "Apple Distribution: Test",
                    TeamId: "TEAM",
                    SerialNumber: "00ABC123",
                    ExpirationDate: DateTime.UtcNow.AddDays(30),
                    IsValid: true)
            });
        _localCertificateService.Setup(x => x.ExportP12Async("Apple Distribution: Test (TEAM)", It.IsAny<string>()))
            .ReturnsAsync(new byte[] { 0xAA, 0xBB, 0xCC });
        _cloudSecretsService.Setup(x => x.StoreSecretAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var (p12, password) = await _sut.GetCertificateSecretsAsync("ABC123");

        Assert.NotNull(p12);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, p12);
        Assert.False(string.IsNullOrEmpty(password));
        _localCertificateService.Verify(x => x.ExportP12Async("Apple Distribution: Test (TEAM)", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_NotInCloudOrKeychain_ReturnsNulls()
    {
        var provider = new CloudSecretsProviderConfig("provider-1", "Provider", CloudSecretsProviderType.OnePassword, new());
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(provider);
        _cloudSecretsService.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _cloudSecretsService.Setup(x => x.ListSecretsAsync("CERT_", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        _localCertificateService.Setup(x => x.IsSupported).Returns(false);

        var (p12, password) = await _sut.GetCertificateSecretsAsync("ABC123");

        Assert.Null(p12);
        Assert.Null(password);
    }
}
