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

        _localCertificateService.SetupGet(x => x.IsSupported).Returns(false);

        var result = await _sut.DownloadAndInstallAsync("cert-1");

        Assert.False(result);
        _cloudSecretsService.Verify(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()), Times.Once);
        _cloudSecretsService.Verify(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()), Times.Once);
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
}
