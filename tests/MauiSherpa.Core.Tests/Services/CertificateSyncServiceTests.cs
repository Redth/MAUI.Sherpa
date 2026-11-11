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

    static byte[] CreateP12(string password, bool includePrivateKey, string serial = "ABC123")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Apple Development: Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var publicCertificate = request.Create(
            request.SubjectName,
            X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1),
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            Convert.FromHexString("00" + serial));
        using var certificate = publicCertificate.CopyWithPrivateKey(rsa);
        if (includePrivateKey)
            return certificate.Export(X509ContentType.Pfx, password);

        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return publicOnly.Export(X509ContentType.Pfx, password);
    }

    public CertificateSyncServiceTests()
    {
        var provider = new Mock<ICloudSecretsProvider>();
        provider.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string key, CancellationToken token) => _cloudSecretsService.Object.GetSecretAsync(key, token));
        provider.Setup(x => x.StoreSecretAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Returns((string key, byte[] value, Dictionary<string, string>? metadata, CancellationToken token) =>
                _cloudSecretsService.Object.StoreSecretAsync(key, value, metadata, token));
        provider.Setup(x => x.DeleteSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string key, CancellationToken token) => _cloudSecretsService.Object.DeleteSecretAsync(key, token));
        var registry = new Mock<ISecretsProviderRegistry>();
        registry.Setup(x => x.GetProvidersAsync()).ReturnsAsync(Array.Empty<CloudSecretsProviderConfig>());
        registry.Setup(x => x.GetProviderAsync(It.IsAny<string>())).ReturnsAsync(provider.Object);
        _sut = new CertificateSyncService(
            _cloudSecretsService.Object, _localCertificateService.Object, _logger.Object, registry.Object);
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
            .ReturnsAsync(KeyedP12);
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("password"));

        var (p12, password) = await _sut.GetCertificateSecretsAsync("ABC123");

        Assert.NotNull(p12);
        Assert.Equal(KeyedP12, p12);
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
            .ReturnsAsync(KeyedP12);
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_00ABC123_PWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("password"));

        var (p12, password) = await _sut.GetCertificateSecretsAsync("ABC123");

        Assert.NotNull(p12);
        Assert.Equal(KeyedP12, p12);
        Assert.Equal("password", password);
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
            .ReturnsAsync((string _, string password) => CreateP12(password, includePrivateKey: true));
        _cloudSecretsService.Setup(x => x.StoreSecretAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var (p12, password) = await _sut.GetCertificateSecretsAsync("ABC123");

        Assert.NotNull(p12);
        Assert.False(string.IsNullOrEmpty(password));
        Assert.True(CertificateBundleUtilities.ContainsPrivateKey(p12, password, "ABC123"));
        _localCertificateService.Verify(x => x.ExportP12Async("Apple Distribution: Test (TEAM)", It.IsAny<string>()), Times.Once);
        _cloudSecretsService.Verify(x => x.StoreSecretAsync(
            "CERT_ABC123_P12", It.Is<byte[]>(data => data.SequenceEqual(p12)),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _cloudSecretsService.Verify(x => x.StoreSecretAsync(
            "CERT_ABC123_PWD", It.Is<byte[]>(data => Encoding.UTF8.GetString(data) == password),
            It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()), Times.Once);
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
                .ReturnsAsync((string key, CancellationToken _) =>
                {
                    var storedKey = config.ProviderType == CloudSecretsProviderType.AzureKeyVault
                        ? key.Replace('_', '-')
                        : key;
                    return secrets.TryGetValue(storedKey, out var value) ? value : null;
                });
            provider.Setup(x => x.ListSecretsStrictAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? prefix, CancellationToken _) =>
                {
                    var storedPrefix = config.ProviderType == CloudSecretsProviderType.AzureKeyVault
                        ? prefix?.Replace('_', '-')
                        : prefix;
                    return secrets.Keys.Where(k =>
                        storedPrefix is null || k.StartsWith(storedPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
                });
            registry.Setup(x => x.GetProviderAsync(config.Id)).ReturnsAsync(provider.Object);
        }

        return new CertificateSyncService(
            _cloudSecretsService.Object,
            _localCertificateService.Object,
            _logger.Object,
            registry.Object);
    }

    static Dictionary<string, byte[]> CertificateSecrets(
        byte[] p12, string serial = "ABC123", string password = "password", bool azure = false)
    {
        var separator = azure ? "-" : "_";
        return new Dictionary<string, byte[]>
        {
            [$"CERT{separator}{serial}{separator}P12"] = p12,
            [$"CERT{separator}{serial}{separator}PWD"] = Encoding.UTF8.GetBytes(password)
        };
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_FallsBackToLocalVaultWhenRemoteCopyIsMissing()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var remote = new CloudSecretsProviderConfig("remote", "Remote", CloudSecretsProviderType.AzureKeyVault, new());
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(remote);
        var sut = CreateWithProviders(
            (remote, new Dictionary<string, byte[]>()),
            (local, CertificateSecrets(KeyedP12)));

        var (p12, password) = await sut.GetCertificateSecretsAsync("ABC123");

        Assert.Equal(KeyedP12, p12);
        Assert.Equal("password", password);
        _cloudSecretsService.Verify(x => x.StoreSecretAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_PrefersActiveProviderWhenBothCopiesAreUsable()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var remote = new CloudSecretsProviderConfig("remote", "Remote", CloudSecretsProviderType.AzureKeyVault, new());
        var remoteP12 = CreateP12("remote-password", includePrivateKey: true);
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(remote);
        var sut = CreateWithProviders(
            (local, CertificateSecrets(KeyedP12)),
            (remote, CertificateSecrets(remoteP12, password: "remote-password", azure: true)));

        var (p12, password) = await sut.GetCertificateSecretsAsync("ABC123");

        Assert.Equal(remoteP12, p12);
        Assert.Equal("remote-password", password);
    }

    [Fact]
    public async Task DownloadAndInstallBySerialAsync_PrefersLocalVaultWhenBothCopiesAreUsable()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var remote = new CloudSecretsProviderConfig("remote", "Remote", CloudSecretsProviderType.AzureKeyVault, new());
        var remoteP12 = CreateP12("remote-password", includePrivateKey: true);
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(remote);
        var sut = CreateWithProviders(
            (local, CertificateSecrets(KeyedP12)),
            (remote, CertificateSecrets(remoteP12, password: "remote-password", azure: true)));
        _localCertificateService.Setup(x => x.ImportP12Async(
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var installed = await sut.DownloadAndInstallBySerialAsync("ABC123");

        Assert.True(installed);
        _localCertificateService.Verify(x => x.ImportP12Async(
            It.Is<byte[]>(data => data.SequenceEqual(KeyedP12)), "password", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_UnusableFirstCopy_UsesPasswordFromNextProvider()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var remote = new CloudSecretsProviderConfig("remote", "Remote", CloudSecretsProviderType.AzureKeyVault, new());
        var remoteP12 = CreateP12("remote-password", includePrivateKey: true);
        var sut = CreateWithProviders(
            (local, CertificateSecrets(KeyedP12, password: "wrong-password")),
            (remote, CertificateSecrets(remoteP12, password: "remote-password", azure: true)));

        var (p12, password) = await sut.GetCertificateSecretsAsync("ABC123");

        Assert.Equal(remoteP12, p12);
        Assert.Equal("remote-password", password);
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_FirstCopyHasDifferentSerial_UsesNextProvider()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var remote = new CloudSecretsProviderConfig("remote", "Remote", CloudSecretsProviderType.AzureKeyVault, new());
        var wrongCertificate = CreateP12("password", includePrivateKey: true, serial: "DEF456");
        var sut = CreateWithProviders(
            (local, CertificateSecrets(wrongCertificate)),
            (remote, CertificateSecrets(KeyedP12, azure: true)));

        var (p12, password) = await sut.GetCertificateSecretsAsync("ABC123");

        Assert.Equal(KeyedP12, p12);
        Assert.Equal("password", password);
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_FindsPasswordlessBundle()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var passwordlessP12 = CreateP12(string.Empty, includePrivateKey: true);
        var sut = CreateWithProviders((local, new Dictionary<string, byte[]>
        {
            ["CERT_ABC123_P12"] = passwordlessP12
        }));

        var (p12, password) = await sut.GetCertificateSecretsAsync("ABC123");

        Assert.Equal(passwordlessP12, p12);
        Assert.Null(password);
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_WhenRegistryOmitsActiveProvider_UsesActiveProvider()
    {
        var active = new CloudSecretsProviderConfig("active", "Active", CloudSecretsProviderType.OnePassword, new());
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(active);
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()))
            .ReturnsAsync(KeyedP12);
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("password"));
        var sut = CreateWithProviders();

        var (p12, password) = await sut.GetCertificateSecretsAsync("ABC123");

        Assert.Equal(KeyedP12, p12);
        Assert.Equal("password", password);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetCertificateSecretsAsync_WhenActiveProviderCannotBeResolved_UsesActiveService(bool throws)
    {
        var active = new CloudSecretsProviderConfig("active", "Active", CloudSecretsProviderType.OnePassword, new());
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(active);
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()))
            .ReturnsAsync(KeyedP12);
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("password"));
        var registry = new Mock<ISecretsProviderRegistry>();
        registry.Setup(x => x.GetProvidersAsync()).ReturnsAsync(new[] { active });
        if (throws)
            registry.Setup(x => x.GetProviderAsync(active.Id)).ThrowsAsync(new InvalidOperationException("Unavailable"));
        else
            registry.Setup(x => x.GetProviderAsync(active.Id)).ReturnsAsync((ICloudSecretsProvider?)null);
        var sut = new CertificateSyncService(
            _cloudSecretsService.Object, _localCertificateService.Object, _logger.Object, registry.Object);

        var (p12, password) = await sut.GetCertificateSecretsAsync("ABC123");

        Assert.Equal(KeyedP12, p12);
        Assert.Equal("password", password);
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_WhenActiveProviderChanges_DoesNotMixProviderCopies()
    {
        var first = new CloudSecretsProviderConfig("first", "First", CloudSecretsProviderType.OnePassword, new());
        var second = new CloudSecretsProviderConfig("second", "Second", CloudSecretsProviderType.OnePassword, new());
        var active = first;
        _cloudSecretsService.SetupGet(x => x.ActiveProvider).Returns(() => active);
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                active = second;
                return KeyedP12;
            });

        var (p12, password) = await _sut.GetCertificateSecretsAsync("ABC123");

        Assert.Null(p12);
        Assert.Null(password);
        _cloudSecretsService.Verify(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_ResolvesEachProviderOnceForBothSecrets()
    {
        var config = new CloudSecretsProviderConfig("remote", "Remote", CloudSecretsProviderType.OnePassword, new());
        var first = new Mock<ICloudSecretsProvider>();
        first.Setup(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()))
            .ReturnsAsync(KeyedP12);
        first.Setup(x => x.GetSecretAsync("CERT_ABC123_PWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("password"));
        var second = new Mock<ICloudSecretsProvider>();
        var registry = new Mock<ISecretsProviderRegistry>();
        registry.Setup(x => x.GetProvidersAsync()).ReturnsAsync(new[] { config });
        registry.SetupSequence(x => x.GetProviderAsync(config.Id))
            .ReturnsAsync(first.Object)
            .ReturnsAsync(second.Object);
        var sut = new CertificateSyncService(
            _cloudSecretsService.Object, _localCertificateService.Object, _logger.Object, registry.Object);

        var (p12, password) = await sut.GetCertificateSecretsAsync("ABC123");

        Assert.Equal(KeyedP12, p12);
        Assert.Equal("password", password);
        registry.Verify(x => x.GetProviderAsync(config.Id), Times.Once);
        second.Verify(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadAndInstallBySerialAsync_SkipsCopyWithoutPrivateKey_AndUsesNextProvider()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var remote = new CloudSecretsProviderConfig("remote", "Remote Vault", CloudSecretsProviderType.AzureKeyVault, new());
        var sut = CreateWithProviders(
            (local, CertificateSecrets(PublicOnlyP12)),
            (remote, CertificateSecrets(KeyedP12, azure: true)));
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
    public async Task DownloadAndInstallBySerialAsync_InstallsPasswordlessBundle()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var passwordlessP12 = CreateP12(string.Empty, includePrivateKey: true);
        var sut = CreateWithProviders((local, new Dictionary<string, byte[]>
        {
            ["CERT_ABC123_P12"] = passwordlessP12
        }));
        _localCertificateService.Setup(x => x.ImportP12Async(It.IsAny<byte[]>(), string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await sut.DownloadAndInstallBySerialAsync("ABC123");

        Assert.True(result);
        _localCertificateService.Verify(x => x.ImportP12Async(
            It.Is<byte[]>(data => data.SequenceEqual(passwordlessP12)), string.Empty, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCertificateStatusesAsync_ReportsCopyInNonActiveProvider()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var remote = new CloudSecretsProviderConfig("remote", "Remote Vault", CloudSecretsProviderType.AzureKeyVault, new());
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(local);
        var sut = CreateWithProviders(
            (local, new Dictionary<string, byte[]>()),
            (remote, CertificateSecrets(KeyedP12, serial: "00ABC123", azure: true)));
        var certificate = new AppleCertificate("cert-1", "Apple Development: Test", "DEVELOPMENT", "IOS", DateTime.UtcNow.AddYears(1), "ABC123");

        var status = (await sut.GetCertificateStatusesAsync(new[] { certificate })).Single();

        Assert.Equal(SecretLocation.CloudOnly, status.Location);
        Assert.Equal("remote", status.CloudProviderId);
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_FallsBackToLocalVaultWhenActiveProviderLacksCertificate()
    {
        var local = new CloudSecretsProviderConfig(CloudSecretsService.DefaultLocalProviderId, "Local", CloudSecretsProviderType.Local, new());
        var remote = new CloudSecretsProviderConfig("remote", "Remote Vault", CloudSecretsProviderType.AzureKeyVault, new());
        _cloudSecretsService.Setup(x => x.ActiveProvider).Returns(remote);
        _cloudSecretsService.Setup(x => x.ListSecretsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        var sut = CreateWithProviders(
            (local, CertificateSecrets(KeyedP12)),
            (remote, new Dictionary<string, byte[]>()));

        var (p12, password) = await sut.GetCertificateSecretsAsync("ABC123", autoUploadFromKeychain: false);

        Assert.Equal(KeyedP12, p12);
        Assert.Equal("password", password);
    }

    [Fact]
    public async Task AzureCertificate_WithLeadingZeros_CanBeResolvedAndInstalledById()
    {
        var remote = new CloudSecretsProviderConfig("remote", "Remote Vault", CloudSecretsProviderType.AzureKeyVault, new());
        var secrets = CertificateSecrets(KeyedP12, serial: "00ABC123", azure: true);
        var metadata = new CertificateSecretMetadata(
            "cert-1", "ABC123", "Test Cert", "Development", DateTime.UtcNow.AddDays(10), "machine", DateTime.UtcNow);
        secrets["CERT-00ABC123-META"] = JsonSerializer.SerializeToUtf8Bytes(metadata);
        var sut = CreateWithProviders((remote, secrets));
        _localCertificateService.Setup(x => x.ImportP12Async(It.IsAny<byte[]>(), "password", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var (p12, password) = await sut.GetCertificateSecretsAsync("ABC123");
        var installed = await sut.DownloadAndInstallAsync("cert-1");

        Assert.Equal(KeyedP12, p12);
        Assert.Equal("password", password);
        Assert.True(installed);
        _localCertificateService.Verify(x => x.ImportP12Async(
            It.Is<byte[]>(data => data.SequenceEqual(KeyedP12)), "password", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCertificateStatusesAsync_MetadataWithoutP12_DoesNotReportCopy()
    {
        var remote = new CloudSecretsProviderConfig("remote", "Remote Vault", CloudSecretsProviderType.AzureKeyVault, new());
        var sut = CreateWithProviders((remote, new Dictionary<string, byte[]>
        {
            ["CERT-ABC123-META"] = Array.Empty<byte>(),
            ["CERT-ABC123-PWD"] = Array.Empty<byte>()
        }));
        var certificate = new AppleCertificate("cert-1", "Test", "DEVELOPMENT", "IOS", DateTime.UtcNow.AddYears(1), "ABC123");

        var status = (await sut.GetCertificateStatusesAsync(new[] { certificate })).Single();

        Assert.Equal(SecretLocation.None, status.Location);
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_WhenCanceled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.GetCertificateSecretsAsync("ABC123", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_WhenProviderSwallowsCancellation_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        _cloudSecretsService.SetupGet(x => x.ActiveProvider)
            .Returns(new CloudSecretsProviderConfig("active", "Active", CloudSecretsProviderType.OnePassword, new()));
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, CancellationToken _) =>
            {
                cancellation.Cancel();
                return (byte[]?)null;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.GetCertificateSecretsAsync("ABC123", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_WhenKeychainExportIsCanceled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        _localCertificateService.SetupGet(x => x.IsSupported).Returns(true);
        _localCertificateService.Setup(x => x.GetSigningIdentitiesAsync())
            .ReturnsAsync(new[] { new LocalSigningIdentity("identity", "Test", "TEAM", "ABC123", null, true) });
        _localCertificateService.Setup(x => x.ExportP12Async("identity", It.IsAny<string>()))
            .ReturnsAsync((string _, string password) =>
            {
                cancellation.Cancel();
                return CreateP12(password, includePrivateKey: true);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.GetCertificateSecretsAsync("ABC123", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task GetCertificateSecretsAsync_WhenAutoUploadSwallowsCancellation_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        _cloudSecretsService.SetupGet(x => x.ActiveProvider)
            .Returns(new CloudSecretsProviderConfig("active", "Active", CloudSecretsProviderType.OnePassword, new()));
        _cloudSecretsService.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _cloudSecretsService.Setup(x => x.ListSecretsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        _localCertificateService.SetupGet(x => x.IsSupported).Returns(true);
        _localCertificateService.Setup(x => x.GetSigningIdentitiesAsync())
            .ReturnsAsync(new[] { new LocalSigningIdentity("identity", "Test", "TEAM", "ABC123", null, true) });
        _localCertificateService.Setup(x => x.ExportP12Async("identity", It.IsAny<string>()))
            .ReturnsAsync((string _, string password) => CreateP12(password, includePrivateKey: true));
        _cloudSecretsService.Setup(x => x.StoreSecretAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] _, Dictionary<string, string>? _, CancellationToken _) =>
            {
                cancellation.Cancel();
                return false;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.GetCertificateSecretsAsync("ABC123", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task UploadToCloudAsync_WhenCanceledAfterP12_CompletesPasswordBeforeCanceling()
    {
        using var cancellation = new CancellationTokenSource();
        var storedSecrets = new Dictionary<string, byte[]>();
        _cloudSecretsService.SetupGet(x => x.ActiveProvider)
            .Returns(new CloudSecretsProviderConfig("active", "Active", CloudSecretsProviderType.OnePassword, new()));
        _cloudSecretsService.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storedSecrets.TryGetValue(key, out var value) ? value : null);
        _cloudSecretsService.Setup(x => x.StoreSecretAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, byte[] value, Dictionary<string, string>? _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                storedSecrets[key] = value;
                if (key.EndsWith("_P12", StringComparison.Ordinal))
                    cancellation.Cancel();
                return true;
            });
        var certificate = new AppleCertificate("cert-1", "Test", "DEVELOPMENT", "IOS", DateTime.UtcNow, "ABC123");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.UploadToCloudAsync(certificate, KeyedP12, "password", cancellationToken: cancellation.Token));

        Assert.Equal(KeyedP12, storedSecrets["CERT_ABC123_P12"]);
        Assert.Equal("password", Encoding.UTF8.GetString(storedSecrets["CERT_ABC123_PWD"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadToCloudAsync_WhenPasswordWriteFailsAfterCancellation_CleansUpP12(bool throws)
    {
        using var cancellation = new CancellationTokenSource();
        var storedSecrets = new Dictionary<string, byte[]>();
        _cloudSecretsService.SetupGet(x => x.ActiveProvider)
            .Returns(new CloudSecretsProviderConfig("active", "Active", CloudSecretsProviderType.OnePassword, new()));
        _cloudSecretsService.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storedSecrets.TryGetValue(key, out var value) ? value : null);
        _cloudSecretsService.Setup(x => x.StoreSecretAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, byte[] value, Dictionary<string, string>? _, CancellationToken _) =>
            {
                if (key.EndsWith("_P12", StringComparison.Ordinal))
                {
                    storedSecrets[key] = value;
                    cancellation.Cancel();
                    return true;
                }

                if (throws)
                    throw new InvalidOperationException("Password write failed");
                return false;
            });
        _cloudSecretsService.Setup(x => x.DeleteSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                storedSecrets.Remove(key);
                return true;
            });
        var certificate = new AppleCertificate("cert-1", "Test", "DEVELOPMENT", "IOS", DateTime.UtcNow, "ABC123");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.UploadToCloudAsync(certificate, KeyedP12, "password", cancellationToken: cancellation.Token));

        Assert.Empty(storedSecrets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadToCloudAsync_WhenPasswordWriteFails_RestoresPreviousPair(bool throws)
    {
        var oldP12 = CreateP12("old-password", includePrivateKey: true);
        var oldPassword = Encoding.UTF8.GetBytes("old-password");
        var storedSecrets = new Dictionary<string, byte[]>
        {
            ["CERT_ABC123_P12"] = oldP12,
            ["CERT_ABC123_PWD"] = oldPassword
        };
        _cloudSecretsService.SetupGet(x => x.ActiveProvider)
            .Returns(new CloudSecretsProviderConfig("active", "Active", CloudSecretsProviderType.OnePassword, new()));
        _cloudSecretsService.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storedSecrets.TryGetValue(key, out var value) ? value : null);
        _cloudSecretsService.Setup(x => x.StoreSecretAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, byte[] value, Dictionary<string, string>? _, CancellationToken _) =>
            {
                storedSecrets[key] = value;
                if (key == "CERT_ABC123_PWD" && Encoding.UTF8.GetString(value) == "password")
                {
                    if (throws)
                        throw new InvalidOperationException("Password write failed");
                    return false;
                }
                return true;
            });
        _cloudSecretsService.Setup(x => x.DeleteSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => storedSecrets.Remove(key));
        var certificate = new AppleCertificate("cert-1", "Test", "DEVELOPMENT", "IOS", DateTime.UtcNow, "ABC123");

        var result = await _sut.UploadToCloudAsync(certificate, KeyedP12, "password");

        Assert.False(result);
        Assert.Equal(oldP12, storedSecrets["CERT_ABC123_P12"]);
        Assert.Equal(oldPassword, storedSecrets["CERT_ABC123_PWD"]);
    }

    [Fact]
    public async Task UploadToCloudAsync_WhenP12WriteFailsAfterCancellation_RestoresPreviousP12()
    {
        using var cancellation = new CancellationTokenSource();
        var oldP12 = CreateP12("old-password", includePrivateKey: true);
        var storedSecrets = new Dictionary<string, byte[]> { ["CERT_ABC123_P12"] = oldP12 };
        _cloudSecretsService.SetupGet(x => x.ActiveProvider)
            .Returns(new CloudSecretsProviderConfig("active", "Active", CloudSecretsProviderType.OnePassword, new()));
        _cloudSecretsService.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storedSecrets.TryGetValue(key, out var value) ? value : null);
        _cloudSecretsService.Setup(x => x.StoreSecretAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, byte[] value, Dictionary<string, string>? _, CancellationToken _) =>
            {
                storedSecrets[key] = value;
                if (value.SequenceEqual(KeyedP12))
                {
                    cancellation.Cancel();
                    return false;
                }
                return true;
            });
        var certificate = new AppleCertificate("cert-1", "Test", "DEVELOPMENT", "IOS", DateTime.UtcNow, "ABC123");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _sut.UploadToCloudAsync(certificate, KeyedP12, "password", cancellationToken: cancellation.Token));

        Assert.Equal(oldP12, storedSecrets["CERT_ABC123_P12"]);
    }

    [Fact]
    public async Task UploadToCloudAsync_WhenPreviousCopyCannotBeRead_DoesNotOverwriteIt()
    {
        _cloudSecretsService.SetupGet(x => x.ActiveProvider)
            .Returns(new CloudSecretsProviderConfig("active", "Active", CloudSecretsProviderType.OnePassword, new()));
        _cloudSecretsService.Setup(x => x.GetSecretAsync("CERT_ABC123_P12", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cannot read the existing certificate"));
        var certificate = new AppleCertificate("cert-1", "Test", "DEVELOPMENT", "IOS", DateTime.UtcNow, "ABC123");

        var result = await _sut.UploadToCloudAsync(certificate, KeyedP12, "password");

        Assert.False(result);
        _cloudSecretsService.Verify(x => x.StoreSecretAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadToCloudAsync_WhenMetadataWriteFails_ReportsFailureAndKeepsCertificatePair()
    {
        var storedSecrets = new Dictionary<string, byte[]>();
        _cloudSecretsService.SetupGet(x => x.ActiveProvider)
            .Returns(new CloudSecretsProviderConfig("active", "Active", CloudSecretsProviderType.OnePassword, new()));
        _cloudSecretsService.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _cloudSecretsService.Setup(x => x.StoreSecretAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, byte[] value, Dictionary<string, string>? _, CancellationToken _) =>
            {
                if (key.EndsWith("_META", StringComparison.Ordinal))
                    return false;
                storedSecrets[key] = value;
                return true;
            });
        var certificate = new AppleCertificate("cert-1", "Test", "DEVELOPMENT", "IOS", DateTime.UtcNow, "ABC123");
        var metadata = new CertificateSecretMetadata(
            "cert-1", "ABC123", "Test", "DEVELOPMENT", DateTime.UtcNow, "machine", DateTime.UtcNow);

        var result = await _sut.UploadToCloudAsync(certificate, KeyedP12, "password", metadata);

        Assert.False(result);
        Assert.Equal(KeyedP12, storedSecrets["CERT_ABC123_P12"]);
        Assert.Equal("password", Encoding.UTF8.GetString(storedSecrets["CERT_ABC123_PWD"]));
        Assert.DoesNotContain("CERT_ABC123_META", storedSecrets.Keys);
    }

    [Fact]
    public async Task UploadToCloudAsync_WhenActiveProviderCannotBeResolved_DoesNotWriteThroughAnotherProvider()
    {
        var active = new CloudSecretsProviderConfig("active", "Active", CloudSecretsProviderType.OnePassword, new());
        _cloudSecretsService.SetupGet(x => x.ActiveProvider).Returns(active);
        var registry = new Mock<ISecretsProviderRegistry>();
        registry.Setup(x => x.GetProviderAsync(active.Id)).ReturnsAsync((ICloudSecretsProvider?)null);
        var sut = new CertificateSyncService(
            _cloudSecretsService.Object, _localCertificateService.Object, _logger.Object, registry.Object);
        var certificate = new AppleCertificate("cert-1", "Test", "DEVELOPMENT", "IOS", DateTime.UtcNow, "ABC123");

        var result = await sut.UploadToCloudAsync(certificate, KeyedP12, "password");

        Assert.False(result);
        _cloudSecretsService.Verify(x => x.StoreSecretAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadToCloudAsync_WithoutProviderRegistry_DoesNotWriteThroughMutableActiveProvider()
    {
        _cloudSecretsService.SetupGet(x => x.ActiveProvider)
            .Returns(new CloudSecretsProviderConfig("active", "Active", CloudSecretsProviderType.OnePassword, new()));
        var sut = new CertificateSyncService(
            _cloudSecretsService.Object, _localCertificateService.Object, _logger.Object);
        var certificate = new AppleCertificate("cert-1", "Test", "DEVELOPMENT", "IOS", DateTime.UtcNow, "ABC123");

        var result = await sut.UploadToCloudAsync(certificate, KeyedP12, "password");

        Assert.False(result);
        _cloudSecretsService.Verify(x => x.StoreSecretAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadToCloudAsync_WhenActiveProviderChanges_UsesOriginalProviderForBothSecrets()
    {
        var first = new CloudSecretsProviderConfig("first", "First", CloudSecretsProviderType.OnePassword, new());
        var second = new CloudSecretsProviderConfig("second", "Second", CloudSecretsProviderType.OnePassword, new());
        var active = first;
        var storedSecrets = new Dictionary<string, byte[]>();
        _cloudSecretsService.SetupGet(x => x.ActiveProvider).Returns(() => active);
        var provider = new Mock<ICloudSecretsProvider>();
        provider.Setup(x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                storedSecrets.TryGetValue(key, out var value) ? value : null);
        provider.Setup(x => x.StoreSecretAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, byte[] value, Dictionary<string, string>? _, CancellationToken _) =>
            {
                storedSecrets[key] = value;
                active = second;
                return true;
            });
        var registry = new Mock<ISecretsProviderRegistry>();
        registry.Setup(x => x.GetProviderAsync(first.Id)).ReturnsAsync(provider.Object);
        var sut = new CertificateSyncService(
            _cloudSecretsService.Object, _localCertificateService.Object, _logger.Object, registry.Object);
        var certificate = new AppleCertificate("cert-1", "Test", "DEVELOPMENT", "IOS", DateTime.UtcNow, "ABC123");

        var result = await sut.UploadToCloudAsync(certificate, KeyedP12, "password");

        Assert.True(result);
        Assert.Equal(KeyedP12, storedSecrets["CERT_ABC123_P12"]);
        Assert.Equal("password", Encoding.UTF8.GetString(storedSecrets["CERT_ABC123_PWD"]));
        _cloudSecretsService.Verify(x => x.StoreSecretAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
