using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
    static readonly byte[] KeyedP12 = CreateP12("password", includePrivateKey: true);
    static readonly byte[] PublicOnlyP12 = CreateP12("password", includePrivateKey: false);

    static byte[] CreateP12(string password, bool includePrivateKey)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Apple Development: Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        if (includePrivateKey)
            return certificate.Export(X509ContentType.Pfx, password);

        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return publicOnly.Export(X509ContentType.Pfx, password);
    }

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
            .ReturnsAsync(KeyedP12);
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("password"));
        _localCertificateService.Setup(x => x.ImportP12Async(
                It.Is<byte[]>(data => data.SequenceEqual(KeyedP12)),
                "password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.DownloadAndInstallAsync("cert-1");

        Assert.True(result);
        _cloudSecretsService.Verify(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()), Times.Once);
        _cloudSecretsService.Verify(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()), Times.Once);
        _localCertificateService.Verify(x => x.ImportP12Async(
            It.Is<byte[]>(data => data.SequenceEqual(KeyedP12)),
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
            .ReturnsAsync(KeyedP12);
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("password"));
        _localCertificateService.Setup(x => x.ImportP12Async(
                It.Is<byte[]>(data => data.SequenceEqual(KeyedP12)),
                "password",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.DownloadAndInstallBySerialAsync("ABC123");

        Assert.True(result);
        _localCertificateService.Verify(x => x.ImportP12Async(
            It.Is<byte[]>(data => data.SequenceEqual(KeyedP12)),
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

    CertificateSyncService CreateWithProviders(params (CloudSecretsProviderConfig Config, Dictionary<string, byte[]> Secrets)[] providers)
    {
        var registry = new Mock<ISecretsProviderRegistry>();
        registry.Setup(x => x.GetProvidersAsync()).ReturnsAsync(providers.Select(p => p.Config).ToList());
        foreach (var (config, secrets) in providers)
        {
            var provider = new Mock<ICloudSecretsProvider>();
            provider.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, CancellationToken _) => secrets.TryGetValue(key, out var value) ? value : null);
            provider.Setup(x => x.ListSecretsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? prefix, CancellationToken _) =>
                    secrets.Keys.Where(k => prefix is null || k.StartsWith(prefix, StringComparison.Ordinal)).ToList());
            registry.Setup(x => x.GetProviderAsync(config.Id)).ReturnsAsync(provider.Object);
        }

        return new CertificateSyncService(
            _cloudSecretsService.Object,
            _localCertificateService.Object,
            _logger.Object,
            registry.Object);
    }

    static Dictionary<string, byte[]> CertificateSecrets(byte[] p12) => new()
    {
        ["CERT_ABC123_P12"] = p12,
        ["CERT_ABC123_PWD"] = Encoding.UTF8.GetBytes("password")
    };

    [Fact]
    public async Task DownloadAndInstallBySerialAsync_SkipsCopyWithoutPrivateKey_AndUsesNextProvider()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var remote = new CloudSecretsProviderConfig("remote", "Remote Vault", CloudSecretsProviderType.AzureKeyVault, new());
        var sut = CreateWithProviders(
            (local, CertificateSecrets(PublicOnlyP12)),
            (remote, CertificateSecrets(KeyedP12)));
        _localCertificateService.Setup(x => x.ImportP12Async(It.IsAny<byte[]>(), "password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await sut.DownloadAndInstallBySerialAsync("ABC123");

        Assert.True(result);
        _localCertificateService.Verify(x => x.ImportP12Async(
            It.Is<byte[]>(data => data.SequenceEqual(KeyedP12)), "password", It.IsAny<CancellationToken>()), Times.Once);
        _localCertificateService.Verify(x => x.ImportP12Async(
            It.Is<byte[]>(data => data.SequenceEqual(PublicOnlyP12)), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadAndInstallBySerialAsync_NoCopyWithPrivateKey_DoesNotImport()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var sut = CreateWithProviders((local, CertificateSecrets(PublicOnlyP12)));

        var result = await sut.DownloadAndInstallBySerialAsync("ABC123");

        Assert.False(result);
        _localCertificateService.Verify(x => x.ImportP12Async(
            It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadAndInstallBySerialAsync_ImportWithoutResultingIdentity_ReturnsFalse()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var sut = CreateWithProviders((local, CertificateSecrets(KeyedP12)));
        _localCertificateService.SetupGet(x => x.IsSupported).Returns(true);
        _localCertificateService.Setup(x => x.ImportP12Async(It.IsAny<byte[]>(), "password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _localCertificateService.Setup(x => x.HasInstalledIdentityAsync("ABC123")).ReturnsAsync(false);

        var result = await sut.DownloadAndInstallBySerialAsync("ABC123");

        Assert.False(result);
    }

    [Fact]
    public async Task DownloadAndInstallBySerialAsync_UntrustedButInstalledIdentity_ReturnsTrue()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var sut = CreateWithProviders((local, CertificateSecrets(KeyedP12)));
        _localCertificateService.SetupGet(x => x.IsSupported).Returns(true);
        _localCertificateService.Setup(x => x.ImportP12Async(It.IsAny<byte[]>(), "password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _localCertificateService.Setup(x => x.HasInstalledIdentityAsync("ABC123")).ReturnsAsync(true);
        _localCertificateService.Setup(x => x.GetSigningIdentitiesAsync())
            .ReturnsAsync(Array.Empty<LocalSigningIdentity>());

        var result = await sut.DownloadAndInstallBySerialAsync("ABC123");

        Assert.True(result);
    }

    [Fact]
    public async Task GetCertificateStatusesAsync_ReportsCopyInNonActiveProvider()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var remote = new CloudSecretsProviderConfig("remote", "Remote Vault", CloudSecretsProviderType.AzureKeyVault, new());
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(local);
        var sut = CreateWithProviders(
            (local, new Dictionary<string, byte[]>()),
            (remote, CertificateSecrets(KeyedP12)));
        var certificate = new AppleCertificate("cert-1", "Apple Development: Test", "DEVELOPMENT", "IOS", DateTime.UtcNow.AddYears(1), "ABC123");

        var status = (await sut.GetCertificateStatusesAsync(new[] { certificate })).Single();

        Assert.Equal(SecretLocation.CloudOnly, status.Location);
        Assert.Equal("remote", status.CloudProviderId);
    }
}
