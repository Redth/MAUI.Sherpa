using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Moq;
using Xunit;

namespace MauiSherpa.Core.Tests.Services;

public class BackupServiceTests
{
    private readonly Mock<IEncryptedSettingsService> _mockSettingsService;
    private readonly Mock<IAppleIdentityService> _mockAppleIdentityService;
    private readonly Mock<ICloudSecretsService> _mockCloudSecretsService;
    private readonly Mock<ISecretsPublisherService> _mockSecretsPublisherService;
    private readonly Mock<IGoogleIdentityService> _mockGoogleIdentityService;
    private readonly BackupService _service;

    public BackupServiceTests()
    {
        _mockSettingsService = new Mock<IEncryptedSettingsService>();
        _mockAppleIdentityService = new Mock<IAppleIdentityService>();
        _mockCloudSecretsService = new Mock<ICloudSecretsService>();
        _mockSecretsPublisherService = new Mock<ISecretsPublisherService>();
        _mockGoogleIdentityService = new Mock<IGoogleIdentityService>();

        _mockAppleIdentityService
            .Setup(x => x.GetIdentitiesAsync())
            .ReturnsAsync(Array.Empty<AppleIdentity>());
        _mockCloudSecretsService
            .Setup(x => x.InitializeAsync())
            .Returns(Task.CompletedTask);
        _mockCloudSecretsService
            .Setup(x => x.GetProvidersAsync())
            .ReturnsAsync(Array.Empty<CloudSecretsProviderConfig>());
        _mockCloudSecretsService
            .SetupGet(x => x.ActiveProvider)
            .Returns((CloudSecretsProviderConfig?)null);
        _mockSecretsPublisherService
            .Setup(x => x.GetPublishersAsync())
            .ReturnsAsync(Array.Empty<SecretsPublisherConfig>());
        _mockGoogleIdentityService
            .Setup(x => x.GetIdentitiesAsync())
            .ReturnsAsync(Array.Empty<GoogleIdentity>());

        _service = new BackupService(
            _mockSettingsService.Object,
            _mockAppleIdentityService.Object,
            _mockCloudSecretsService.Object,
            _mockSecretsPublisherService.Object,
            _mockGoogleIdentityService.Object);
    }

    [Fact]
    public async Task ExportSettingsAsync_CreatesEncryptedBackup()
    {
        var settings = new MauiSherpaSettings
        {
            AppleIdentities = new List<AppleIdentityData>
            {
                new("1", "Test", "KEY", "ISS", "content", DateTime.UtcNow)
            }
        };
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(settings);

        var result = await _service.ExportSettingsAsync("password123");

        result.Should().NotBeEmpty();
        result.Length.Should().BeGreaterThan(68); // Header + salt + nonce + tag + some content
    }

    [Fact]
    public async Task ExportSettingsAsync_StartsWithMagicHeader()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());

        var result = await _service.ExportSettingsAsync("password");

        var header = System.Text.Encoding.UTF8.GetString(result, 0, 8);
        header.Should().Be("MSSBAK01");
    }

    [Fact]
    public async Task ExportSettingsAsync_ThrowsOnEmptyPassword()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExportSettingsAsync(""));
    }

    [Fact]
    public async Task ExportSettingsAsync_ThrowsOnNullPassword()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExportSettingsAsync(null!));
    }

    [Fact]
    public async Task ImportSettingsAsync_DecryptsCorrectly()
    {
        var original = new MauiSherpaSettings
        {
            AppleIdentities = new List<AppleIdentityData>
            {
                new("id1", "My Identity", "KEY123", "ISSUER456", "p8content", DateTime.UtcNow)
            }
        };
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(original);

        var encrypted = await _service.ExportSettingsAsync("mypassword");
        var imported = await _service.ImportSettingsAsync(encrypted, "mypassword");

        imported.AppleIdentities.Should().HaveCount(1);
        imported.AppleIdentities[0].Name.Should().Be("My Identity");
        imported.AppleIdentities[0].KeyId.Should().Be("KEY123");
    }

    [Fact]
    public async Task ExportSettingsAsync_UsesAppleIdentityServiceForP8Content()
    {
        var settings = new MauiSherpaSettings
        {
            AppleIdentities = new List<AppleIdentityData>
            {
                new("id1", "Old Name", "OLDKEY", "OLDISSUER", "", DateTime.UtcNow.AddDays(-1))
            }
        };

        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(settings);
        _mockAppleIdentityService.Setup(x => x.GetIdentitiesAsync()).ReturnsAsync(new List<AppleIdentity>
        {
            new("id1", "Identity 1", "KEY1", "ISS1", null, "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----")
        });

        var encrypted = await _service.ExportSettingsAsync("password123");
        var imported = await _service.ImportSettingsAsync(encrypted, "password123");

        imported.AppleIdentities.Should().ContainSingle();
        imported.AppleIdentities[0].Name.Should().Be("Identity 1");
        imported.AppleIdentities[0].KeyId.Should().Be("KEY1");
        imported.AppleIdentities[0].P8Content.Should().Contain("BEGIN PRIVATE KEY");
    }

    [Fact]
    public async Task ExportSettingsAsync_UsesCloudProvidersAndPublishersFromServices()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());

        var provider = new CloudSecretsProviderConfig(
            "cp1",
            "Infisical Team",
            CloudSecretsProviderType.Infisical,
            new Dictionary<string, string>
            {
                ["ClientId"] = "client",
                ["ClientSecret"] = "secret"
            });
        _mockCloudSecretsService.Setup(x => x.GetProvidersAsync()).ReturnsAsync(new List<CloudSecretsProviderConfig> { provider });
        _mockCloudSecretsService.SetupGet(x => x.ActiveProvider).Returns(provider);

        _mockSecretsPublisherService.Setup(x => x.GetPublishersAsync()).ReturnsAsync(new List<SecretsPublisherConfig>
        {
            new("sp1", "github", "GitHub Actions", new Dictionary<string, string> { ["PersonalAccessToken"] = "pat" })
        });

        var encrypted = await _service.ExportSettingsAsync("password123");
        var imported = await _service.ImportSettingsAsync(encrypted, "password123");

        imported.CloudProviders.Should().ContainSingle();
        imported.CloudProviders[0].Id.Should().Be("cp1");
        imported.ActiveCloudProviderId.Should().Be("cp1");
        imported.SecretsPublishers.Should().ContainSingle();
        imported.SecretsPublishers[0].Id.Should().Be("sp1");
    }

    [Fact]
    public async Task ExportSettingsAsync_WithSelection_ExportsOnlySelectedItems()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings
        {
            Preferences = new AppPreferences { Theme = "Dark", DemoMode = true }
        });
        _mockAppleIdentityService.Setup(x => x.GetIdentitiesAsync()).ReturnsAsync(new List<AppleIdentity>
        {
            new("id1", "Identity 1", "KEY1", "ISS1", null, "P8-1"),
            new("id2", "Identity 2", "KEY2", "ISS2", null, "P8-2")
        });
        _mockCloudSecretsService.Setup(x => x.GetProvidersAsync()).ReturnsAsync(new List<CloudSecretsProviderConfig>
        {
            new("cp1", "Provider 1", CloudSecretsProviderType.Infisical, new Dictionary<string, string> { ["ClientId"] = "one" }),
            new("cp2", "Provider 2", CloudSecretsProviderType.Infisical, new Dictionary<string, string> { ["ClientId"] = "two" })
        });
        _mockCloudSecretsService.SetupGet(x => x.ActiveProvider).Returns(new CloudSecretsProviderConfig(
            "cp2",
            "Provider 2",
            CloudSecretsProviderType.Infisical,
            new Dictionary<string, string> { ["ClientId"] = "two" }));
        _mockSecretsPublisherService.Setup(x => x.GetPublishersAsync()).ReturnsAsync(new List<SecretsPublisherConfig>
        {
            new("sp1", "github", "GitHub 1", new Dictionary<string, string> { ["PersonalAccessToken"] = "pat1" }),
            new("sp2", "gitlab", "GitLab 1", new Dictionary<string, string> { ["PersonalAccessToken"] = "pat2" })
        });

        var encrypted = await _service.ExportSettingsAsync("password123", new BackupExportSelection
        {
            IncludePreferences = false,
            AppleIdentityIds = new List<string> { "id2" },
            CloudProviderIds = new List<string> { "cp2" },
            SecretsPublisherIds = new List<string> { "sp1" }
        });
        var importResult = await _service.ImportBackupAsync(encrypted, "password123");

        importResult.Selection.IncludePreferences.Should().BeFalse();
        importResult.Settings.Preferences.Theme.Should().Be("System");
        importResult.Settings.AppleIdentities.Select(i => i.Id).Should().Equal("id2");
        importResult.Settings.CloudProviders.Select(p => p.Id).Should().Equal("cp2");
        importResult.Settings.ActiveCloudProviderId.Should().Be("cp2");
        importResult.Settings.SecretsPublishers.Select(p => p.Id).Should().Equal("sp1");
    }

    [Fact]
    public async Task ExportSettingsAsync_WithNoSelectedItems_Throws()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());

        var act = () => _service.ExportSettingsAsync("password123", new BackupExportSelection
        {
            IncludePreferences = false,
            AppleIdentityIds = new List<string>(),
            CloudProviderIds = new List<string>(),
            SecretsPublisherIds = new List<string>()
        });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*At least one setting item must be selected*");
    }

    [Fact]
    public async Task ExportSettingsAsync_DefaultSelection_TracksAllExportedItems()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings
        {
            Preferences = new AppPreferences { Theme = "Dark", DemoMode = true }
        });
        _mockAppleIdentityService.Setup(x => x.GetIdentitiesAsync()).ReturnsAsync(new List<AppleIdentity>
        {
            new("id1", "Identity 1", "KEY1", "ISS1", null, "P8-1"),
            new("id2", "Identity 2", "KEY2", "ISS2", null, "P8-2")
        });
        _mockCloudSecretsService.Setup(x => x.GetProvidersAsync()).ReturnsAsync(new List<CloudSecretsProviderConfig>
        {
            new("cp1", "Provider 1", CloudSecretsProviderType.Infisical, new Dictionary<string, string> { ["ClientId"] = "one" })
        });
        _mockCloudSecretsService.SetupGet(x => x.ActiveProvider).Returns(new CloudSecretsProviderConfig(
            "cp1",
            "Provider 1",
            CloudSecretsProviderType.Infisical,
            new Dictionary<string, string> { ["ClientId"] = "one" }));
        _mockSecretsPublisherService.Setup(x => x.GetPublishersAsync()).ReturnsAsync(new List<SecretsPublisherConfig>
        {
            new("sp1", "github", "GitHub 1", new Dictionary<string, string> { ["PersonalAccessToken"] = "pat1" })
        });

        var encrypted = await _service.ExportSettingsAsync("password123");
        var importResult = await _service.ImportBackupAsync(encrypted, "password123");

        importResult.Selection.IncludePreferences.Should().BeTrue();
        importResult.Selection.AppleIdentityIds.Should().BeEquivalentTo(new[] { "id1", "id2" });
        importResult.Selection.CloudProviderIds.Should().BeEquivalentTo(new[] { "cp1" });
        importResult.Selection.SecretsPublisherIds.Should().BeEquivalentTo(new[] { "sp1" });
    }

    [Fact]
    public async Task ExportImport_PreservesExactSensitiveContent()
    {
        var p8 = "-----BEGIN PRIVATE KEY-----\r\nabcDEF123+/=\r\n-----END PRIVATE KEY-----\r\n";
        var provider = new CloudSecretsProviderConfig(
            "cp1",
            "Infisical Team",
            CloudSecretsProviderType.Infisical,
            new Dictionary<string, string>
            {
                ["ClientId"] = "client-id",
                ["ClientSecret"] = "super$ecret!= token"
            });
        var publisher = new SecretsPublisherConfig(
            "sp1",
            "github",
            "GitHub Actions",
            new Dictionary<string, string>
            {
                ["PersonalAccessToken"] = "ghp_exampleToken+/=",
                ["Owner"] = "owner-name"
            });

        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings
        {
            Preferences = new AppPreferences { Theme = "Dark" }
        });
        _mockAppleIdentityService.Setup(x => x.GetIdentitiesAsync()).ReturnsAsync(new List<AppleIdentity>
        {
            new("id1", "Identity 1", "KEY1", "ISS1", null, p8)
        });
        _mockCloudSecretsService.Setup(x => x.GetProvidersAsync()).ReturnsAsync(new List<CloudSecretsProviderConfig> { provider });
        _mockCloudSecretsService.SetupGet(x => x.ActiveProvider).Returns(provider);
        _mockSecretsPublisherService.Setup(x => x.GetPublishersAsync()).ReturnsAsync(new List<SecretsPublisherConfig> { publisher });

        var encrypted = await _service.ExportSettingsAsync("password123");
        var imported = await _service.ImportSettingsAsync(encrypted, "password123");

        imported.AppleIdentities.Should().ContainSingle();
        imported.AppleIdentities[0].P8Content.Should().Be(p8);
        imported.CloudProviders.Should().ContainSingle();
        imported.CloudProviders[0].Settings["ClientSecret"].Should().Be("super$ecret!= token");
        imported.SecretsPublishers.Should().ContainSingle();
        imported.SecretsPublishers[0].Settings["PersonalAccessToken"].Should().Be("ghp_exampleToken+/=");
    }

    [Fact]
    public async Task ImportSettingsAsync_ThrowsOnWrongPassword()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());
        var encrypted = await _service.ExportSettingsAsync("correct");

        // AES-GCM throws AuthenticationTagMismatchException (a CryptographicException subclass)
        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(
            () => _service.ImportSettingsAsync(encrypted, "wrong"));
    }

    [Fact]
    public async Task ImportSettingsAsync_ThrowsOnEmptyPassword()
    {
        var dummyData = new byte[100];

        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.ImportSettingsAsync(dummyData, ""));
    }

    [Fact]
    public async Task ImportSettingsAsync_ThrowsOnInvalidFormat()
    {
        var invalidData = System.Text.Encoding.UTF8.GetBytes("not a valid backup");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ImportSettingsAsync(invalidData, "password"));
    }

    [Fact]
    public async Task ImportSettingsAsync_ThrowsOnTruncatedData()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());
        var encrypted = await _service.ExportSettingsAsync("password");
        
        // Truncate the data
        var truncated = new byte[50];
        Array.Copy(encrypted, truncated, 50);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ImportSettingsAsync(truncated, "password"));
    }

    [Fact]
    public async Task ValidateBackupAsync_ReturnsTrueForValidBackup()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());
        var encrypted = await _service.ExportSettingsAsync("password");

        var isValid = await _service.ValidateBackupAsync(encrypted);

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateBackupAsync_ReturnsFalseForInvalidHeader()
    {
        var invalidData = System.Text.Encoding.UTF8.GetBytes("NOTVALID rest of data...");

        var isValid = await _service.ValidateBackupAsync(invalidData);

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBackupAsync_ReturnsFalseForEmptyData()
    {
        var isValid = await _service.ValidateBackupAsync(Array.Empty<byte>());

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBackupAsync_ReturnsFalseForNull()
    {
        var isValid = await _service.ValidateBackupAsync(null!);

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateBackupAsync_ReturnsFalseForTooShortData()
    {
        var isValid = await _service.ValidateBackupAsync(new byte[] { 1, 2, 3 });

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task RoundTrip_PreservesAllSettings()
    {
        var original = new MauiSherpaSettings
        {
            Version = 1,
            AppleIdentities = new List<AppleIdentityData>
            {
                new("id1", "Identity 1", "K1", "I1", "p8-1", DateTime.UtcNow),
                new("id2", "Identity 2", "K2", "I2", "p8-2", DateTime.UtcNow)
            },
            CloudProviders = new List<CloudProviderData>
            {
                new("cp1", "Azure KV", CloudSecretsProviderType.AzureKeyVault,
                    new Dictionary<string, string> { ["vaultUrl"] = "https://test.vault.azure.net" })
            },
            SecretsPublishers = new List<SecretsPublisherData>
            {
                new("sp1", "cp1", "GitHub Actions",
                    new Dictionary<string, string> { ["owner"] = "test", ["repo"] = "test-repo" })
            },
            GoogleIdentities = new List<GoogleIdentityData>
            {
                new("gi1", "Firebase Prod", "my-project", "sa@proj.iam.gserviceaccount.com", "{\"type\":\"service_account\"}")
            },
            ActiveCloudProviderId = "cp1",
            Preferences = new AppPreferences { Theme = "Dark", AndroidSdkPath = "/path/to/sdk" }
        };
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(original);

        var encrypted = await _service.ExportSettingsAsync("complexP@ssw0rd!");
        var imported = await _service.ImportSettingsAsync(encrypted, "complexP@ssw0rd!");

        imported.AppleIdentities.Should().HaveCount(2);
        imported.CloudProviders.Should().HaveCount(1);
        imported.SecretsPublishers.Should().HaveCount(1);
        imported.GoogleIdentities.Should().HaveCount(1);
        imported.GoogleIdentities[0].Name.Should().Be("Firebase Prod");
        imported.GoogleIdentities[0].ServiceAccountJson.Should().Contain("service_account");
        imported.ActiveCloudProviderId.Should().Be("cp1");
        imported.Preferences.Theme.Should().Be("Dark");
    }

    [Fact]
    public async Task DifferentPasswordsProduceDifferentCiphertexts()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());

        var encrypted1 = await _service.ExportSettingsAsync("password1");
        var encrypted2 = await _service.ExportSettingsAsync("password2");

        encrypted1.Should().NotBeEquivalentTo(encrypted2);
    }

    [Fact]
    public async Task SamePasswordProducesDifferentCiphertexts_DueToDifferentSalt()
    {
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());

        var encrypted1 = await _service.ExportSettingsAsync("samepassword");
        var encrypted2 = await _service.ExportSettingsAsync("samepassword");

        // Due to random salt and nonce, ciphertexts should differ
        encrypted1.Should().NotBeEquivalentTo(encrypted2);
    }

    [Fact]
    public async Task SpecialCharactersInPassword_WorkCorrectly()
    {
        var original = new MauiSherpaSettings
        {
            Preferences = new AppPreferences { Theme = "Special" }
        };
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(original);
        var specialPassword = "P@$$w0rd!#$%^&*()日本語🔐";

        var encrypted = await _service.ExportSettingsAsync(specialPassword);
        var imported = await _service.ImportSettingsAsync(encrypted, specialPassword);

        imported.Preferences.Theme.Should().Be("Special");
    }

    [Fact]
    public async Task LargeSettings_RoundTripCorrectly()
    {
        var identities = Enumerable.Range(1, 50)
            .Select(i => new AppleIdentityData($"id{i}", $"Identity {i}", $"KEY{i}", $"ISS{i}", 
                new string('X', 1000), DateTime.UtcNow))
            .ToList();

        var original = new MauiSherpaSettings { AppleIdentities = identities };
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(original);

        var encrypted = await _service.ExportSettingsAsync("password");
        var imported = await _service.ImportSettingsAsync(encrypted, "password");

        imported.AppleIdentities.Should().HaveCount(50);
    }

    [Fact]
    public async Task RoundTrip_PreservesGoogleIdentityServiceAccountJson()
    {
        var saJson = "{\"type\":\"service_account\",\"project_id\":\"my-proj\",\"private_key\":\"-----BEGIN RSA PRIVATE KEY-----\\nMIIE...\"}";
        var original = new MauiSherpaSettings
        {
            GoogleIdentities = new List<GoogleIdentityData>
            {
                new("gi1", "Firebase Prod", "my-proj", "sa@my-proj.iam.gserviceaccount.com", saJson),
                new("gi2", "Firebase Dev", "dev-proj", "sa@dev-proj.iam.gserviceaccount.com", null)
            }
        };
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(original);

        var encrypted = await _service.ExportSettingsAsync("password123");
        var imported = await _service.ImportSettingsAsync(encrypted, "password123");

        imported.GoogleIdentities.Should().HaveCount(2);
        var gi1 = imported.GoogleIdentities.First(g => g.Id == "gi1");
        gi1.ServiceAccountJson.Should().Be(saJson);
        gi1.ProjectId.Should().Be("my-proj");
        gi1.ClientEmail.Should().Be("sa@my-proj.iam.gserviceaccount.com");

        var gi2 = imported.GoogleIdentities.First(g => g.Id == "gi2");
        gi2.ServiceAccountJson.Should().BeNull();
    }

    [Fact]
    public async Task ExportSettingsAsync_WithSelection_FiltersGoogleIdentities()
    {
        var original = new MauiSherpaSettings
        {
            GoogleIdentities = new List<GoogleIdentityData>
            {
                new("gi1", "Prod", "proj-1", "sa1@proj.iam.gserviceaccount.com", "{\"type\":\"service_account\"}"),
                new("gi2", "Dev", "proj-2", "sa2@proj.iam.gserviceaccount.com", "{\"type\":\"service_account\"}"),
                new("gi3", "Staging", "proj-3", "sa3@proj.iam.gserviceaccount.com", "{\"type\":\"service_account\"}")
            }
        };
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(original);

        var encrypted = await _service.ExportSettingsAsync("password123", new BackupExportSelection
        {
            IncludePreferences = false,
            GoogleIdentityIds = new List<string> { "gi1", "gi3" }
        });
        var importResult = await _service.ImportBackupAsync(encrypted, "password123");

        importResult.Settings.GoogleIdentities.Select(g => g.Id).Should().Equal("gi1", "gi3");
    }

    [Fact]
    public async Task ExportSettingsAsync_DefaultSelection_IncludesAllGoogleIdentities()
    {
        var original = new MauiSherpaSettings
        {
            GoogleIdentities = new List<GoogleIdentityData>
            {
                new("gi1", "Prod", "proj-1", "sa@proj.iam.gserviceaccount.com", "{\"type\":\"service_account\"}"),
                new("gi2", "Dev", "proj-2", "sa@dev.iam.gserviceaccount.com", "{\"type\":\"service_account\"}")
            }
        };
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(original);

        var encrypted = await _service.ExportSettingsAsync("password123");
        var importResult = await _service.ImportBackupAsync(encrypted, "password123");

        importResult.Selection.GoogleIdentityIds.Should().BeEquivalentTo(new[] { "gi1", "gi2" });
        importResult.Settings.GoogleIdentities.Should().HaveCount(2);
    }

    private const string LocalId = CloudSecretsService.DefaultLocalProviderId;

    private static (byte[] P12, string Serial) CreateP12(string password, bool includePrivateKey)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Apple Distribution: Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));
        if (includePrivateKey)
            return (certificate.Export(X509ContentType.Pfx, password), certificate.SerialNumber);

        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return (publicOnly.Export(X509ContentType.Pfx, password), certificate.SerialNumber);
    }

    private static SecretItemPayload CreateCertificatePayload(string id, byte[] p12, string password) =>
        new(
            new SecretItemRef(SecretItemKind.Certificate, id, "Apple Distribution: Test"),
            1,
            "hash",
            new[]
            {
                new SecretArtifact($"CERT_{id}_P12", p12),
                new SecretArtifact($"CERT_{id}_PWD", System.Text.Encoding.UTF8.GetBytes(password))
            });

    private (BackupService Service, Mock<ISecretItemAdapter> CertificateAdapter, List<SecretItemPayload> Written) CreateCertificateBackupService(
        IReadOnlyList<SecretItemRef> vaultCertificates,
        Func<SecretItemRef, SecretItemPayload?> readVault,
        ILocalCertificateService? localCertificateService = null,
        IPublishProfileService? publishProfileService = null,
        ISecretItemAdapter? profileAdapter = null)
    {
        var written = new List<SecretItemPayload>();
        var certificateAdapter = new Mock<ISecretItemAdapter>();
        certificateAdapter.SetupGet(x => x.Kind).Returns(SecretItemKind.Certificate);
        certificateAdapter
            .Setup(x => x.ListProviderItemsAsync(LocalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vaultCertificates);
        certificateAdapter
            .Setup(x => x.ReadProviderAsync(It.IsAny<SecretItemRef>(), LocalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SecretItemRef item, string _, CancellationToken _) => readVault(item));
        certificateAdapter
            .Setup(x => x.WriteProviderAsync(It.IsAny<SecretItemPayload>(), LocalId, It.IsAny<CancellationToken>()))
            .Callback<SecretItemPayload, string, CancellationToken>((payload, _, _) => written.Add(payload))
            .ReturnsAsync(true);
        _mockSettingsService.Setup(x => x.GetSettingsAsync()).ReturnsAsync(new MauiSherpaSettings());

        var adapters = new List<ISecretItemAdapter> { certificateAdapter.Object };
        if (profileAdapter is not null)
            adapters.Add(profileAdapter);

        var service = new BackupService(
            _mockSettingsService.Object,
            _mockAppleIdentityService.Object,
            _mockCloudSecretsService.Object,
            _mockSecretsPublisherService.Object,
            _mockGoogleIdentityService.Object,
            publishProfileService: publishProfileService,
            providerRegistry: new Mock<ISecretsProviderRegistry>().Object,
            secretItemAdapters: adapters,
            localCertificateService: localCertificateService);
        return (service, certificateAdapter, written);
    }

    [Fact]
    public async Task Backup_RoundTrip_RestoresPublishProfilesAndCertificatesToLocalVault()
    {
        var (p12, serial) = CreateP12("vault-pw", includePrivateKey: true);
        var id = SecretItemAdapterHelper.SanitizeSerialNumber(serial);
        var certificate = new SecretItemRef(SecretItemKind.Certificate, id, "Apple Distribution: Test");

        var profileAdapter = new Mock<ISecretItemAdapter>();
        profileAdapter.SetupGet(x => x.Kind).Returns(SecretItemKind.PublishProfile);
        SecretItemPayload? writtenProfile = null;
        profileAdapter
            .Setup(x => x.WriteProviderAsync(It.IsAny<SecretItemPayload>(), LocalId, It.IsAny<CancellationToken>()))
            .Callback<SecretItemPayload, string, CancellationToken>((payload, _, _) => writtenProfile = payload)
            .ReturnsAsync(true);

        var profileService = new Mock<IPublishProfileService>();
        profileService.Setup(x => x.GetProfilesAsync()).ReturnsAsync(new[]
        {
            new PublishProfile("profile-1", "Release", null, null, null, null,
                new List<PublishProfileAppleConfig>(), new List<PublishProfileAndroidConfig>(),
                new List<PublishProfileSecretMapping>(), DateTime.UtcNow, DateTime.UtcNow)
        });

        var (service, _, written) = CreateCertificateBackupService(
            new[] { certificate },
            _ => CreateCertificatePayload(id, p12, "vault-pw"),
            publishProfileService: profileService.Object,
            profileAdapter: profileAdapter.Object);

        var encrypted = await service.ExportSettingsAsync("password123");
        var imported = await service.ImportBackupAsync(encrypted, "password123");
        var result = await service.RestoreStoredItemsAsync(imported);

        result.PublishProfilesRestored.Should().Be(1);
        result.CertificatesRestored.Should().Be(1);
        result.Failures.Should().BeEmpty();
        writtenProfile!.Artifacts.Single().Key.Should().Be("sherpa-publish-profiles/profile-1");
        written.Single().Item.Id.Should().Be(id);
        BackupService.HasPrivateKey(written.Single()).Should().BeTrue();
        _mockCloudSecretsService.Verify(x => x.EnableDefaultLocalProviderAsync(false), Times.Once);
    }

    [Fact]
    public async Task ExportSettingsAsync_FailsWhenCertificateHasNoPrivateKey()
    {
        var (p12, serial) = CreateP12("vault-pw", includePrivateKey: false);
        var id = SecretItemAdapterHelper.SanitizeSerialNumber(serial);
        var (service, _, _) = CreateCertificateBackupService(
            new[] { new SecretItemRef(SecretItemKind.Certificate, id, "Apple Distribution: Test") },
            _ => CreateCertificatePayload(id, p12, "vault-pw"));

        var act = () => service.ExportSettingsAsync("password123");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*private key could not be included*Apple Distribution: Test*");
    }

    [Fact]
    public async Task ExportSettingsAsync_ExportsKeychainOnlyCertificateWithPrivateKey()
    {
        var (bundle, serial) = CreateP12("ignored", includePrivateKey: true);
        var id = SecretItemAdapterHelper.SanitizeSerialNumber(serial);
        var identity = new LocalSigningIdentity(
            "Apple Distribution: Test (TEAM)", "Apple Distribution: Test", "TEAM", serial, DateTime.UtcNow.AddYears(1), true);

        var localCertificates = new Mock<ILocalCertificateService>();
        localCertificates.Setup(x => x.GetSigningIdentityDiscoveryAsync())
            .ReturnsAsync(new LocalSigningIdentityDiscoveryResult(new[] { identity }, Array.Empty<string>()));
        string? bundlePassword = null;
        localCertificates
            .Setup(x => x.ExportP12Async(It.IsAny<IReadOnlyList<LocalSigningIdentity>>(), It.IsAny<string>()))
            .ReturnsAsync((IReadOnlyList<LocalSigningIdentity> _, string password) =>
            {
                bundlePassword = password;
                return CreateKeychainBundle(bundle, "ignored", password);
            });

        var (service, _, written) = CreateCertificateBackupService(
            Array.Empty<SecretItemRef>(),
            _ => null,
            localCertificateService: localCertificates.Object);

        var available = await service.GetBackupCertificatesAsync();
        available.Certificates.Should().ContainSingle(c => c.Item.Id == id && c.InKeychain && !c.InLocalVault);
        available.Errors.Should().BeEmpty();

        var encrypted = await service.ExportSettingsAsync("password123");
        var imported = await service.ImportBackupAsync(encrypted, "password123");
        var result = await service.RestoreStoredItemsAsync(imported);

        bundlePassword.Should().NotBeNull();
        localCertificates.Verify(
            x => x.ExportP12Async(It.IsAny<IReadOnlyList<LocalSigningIdentity>>(), It.IsAny<string>()),
            Times.Once);
        result.CertificatesRestored.Should().Be(1);
        var restored = written.Single();
        restored.Artifacts.Select(a => a.Key).Should().BeEquivalentTo(
            new[] { $"CERT_{id}_P12", $"CERT_{id}_PWD", $"CERT_{id}_META" });
        BackupService.HasPrivateKey(restored).Should().BeTrue();
    }

    [Fact]
    public async Task GetBackupCertificatesAsync_WhenVaultIsUnavailable_ReportsErrorAndKeepsKeychainItems()
    {
        var localCertificates = new Mock<ILocalCertificateService>();
        localCertificates.Setup(x => x.GetSigningIdentityDiscoveryAsync())
            .ReturnsAsync(new LocalSigningIdentityDiscoveryResult(
                new[] { new LocalSigningIdentity("identity", "Keychain certificate", "TEAM", "ABC123", null, true) },
                Array.Empty<string>()));
        var (service, adapter, _) = CreateCertificateBackupService(
            Array.Empty<SecretItemRef>(), _ => null, localCertificates.Object);
        adapter.Setup(x => x.ListProviderItemsAsync(LocalId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Vault locked"));

        var discovery = await service.GetBackupCertificatesAsync();

        discovery.Certificates.Should().ContainSingle(c =>
            c.Item.Id == "ABC123" && c.InKeychain && !c.InLocalVault);
        discovery.Errors.Should().ContainSingle()
            .Which.Should().Contain("Local Vault").And.Contain("Vault locked");
    }

    [Fact]
    public async Task GetBackupCertificatesAsync_WhenLocalProviderCannotBeResolved_ReportsError()
    {
        var registry = new Mock<ISecretsProviderRegistry>();
        var localCertificates = new Mock<ILocalCertificateService>();
        localCertificates.Setup(x => x.GetSigningIdentityDiscoveryAsync())
            .ReturnsAsync(new LocalSigningIdentityDiscoveryResult(
                Array.Empty<LocalSigningIdentity>(), Array.Empty<string>()));
        var adapter = new CertificateItemAdapter(registry.Object, localCertificates.Object);
        var service = new BackupService(
            _mockSettingsService.Object,
            providerRegistry: registry.Object,
            secretItemAdapters: new[] { adapter },
            localCertificateService: localCertificates.Object);

        var discovery = await service.GetBackupCertificatesAsync();

        discovery.Errors.Should().ContainSingle()
            .Which.Should().Contain("Local Vault").And.Contain("unavailable");
    }

    [Fact]
    public async Task GetBackupCertificatesAsync_WhenLocalDiscoveryIsIncomplete_ReportsAvailableItemsAndErrors()
    {
        var localCertificates = new Mock<ILocalCertificateService>();
        localCertificates.Setup(x => x.GetSigningIdentityDiscoveryAsync())
            .ReturnsAsync(new LocalSigningIdentityDiscoveryResult(
                new[] { new LocalSigningIdentity("identity", "Available certificate", "TEAM", "ABC123", null, true) },
                new[] { "Could not read the serial number for another certificate." }));
        var (service, _, _) = CreateCertificateBackupService(
            Array.Empty<SecretItemRef>(), _ => null, localCertificates.Object);

        var discovery = await service.GetBackupCertificatesAsync();
        var export = () => service.ExportSettingsAsync("password123");

        discovery.Certificates.Should().ContainSingle(c => c.Item.Id == "ABC123" && c.InKeychain);
        discovery.Errors.Should().ContainSingle().Which.Should().Contain("another certificate");
        (await export.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Could not list all signing certificates*another certificate*");
    }

    [Fact]
    public async Task ExportSettingsAsync_WhenSelectedCertificateDisappears_FailsEvenWithOtherSettings()
    {
        var (service, _, _) = CreateCertificateBackupService(Array.Empty<SecretItemRef>(), _ => null);
        var selection = new BackupExportSelection { CertificateIds = new List<string> { "ABC123" } };

        var act = () => service.ExportSettingsAsync("password123", selection);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*selected signing certificates are no longer available*ABC123*");
    }

    [Fact]
    public async Task ExportSettingsAsync_WhenVaultFailsAfterSelection_ReportsTheMissingCertificate()
    {
        var certificate = new SecretItemRef(SecretItemKind.Certificate, "ABC123", "Vault certificate");
        var (service, adapter, _) = CreateCertificateBackupService(new[] { certificate }, _ => null);
        adapter.SetupSequence(x => x.ListProviderItemsAsync(LocalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { certificate })
            .ThrowsAsync(new InvalidOperationException("Vault locked"));
        var initial = await service.GetBackupCertificatesAsync();
        var selection = new BackupExportSelection
        {
            CertificateIds = initial.Certificates.Select(c => c.Item.Id).ToList()
        };

        var act = () => service.ExportSettingsAsync("password123", selection);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*no longer available*ABC123*Local Vault*Vault locked*");
    }

    [Fact]
    public async Task ExportSettingsAsync_WhenVaultFailsAfterSelection_UsesAvailableKeychainCopy()
    {
        var (bundle, serial) = CreateP12("source-password", includePrivateKey: true);
        var id = SecretItemAdapterHelper.SanitizeSerialNumber(serial);
        var certificate = new SecretItemRef(SecretItemKind.Certificate, id, "Vault certificate");
        var identity = new LocalSigningIdentity(
            "identity", "Keychain certificate", "TEAM", serial, DateTime.UtcNow.AddYears(1), true);
        var localCertificates = new Mock<ILocalCertificateService>();
        localCertificates.Setup(x => x.GetSigningIdentityDiscoveryAsync())
            .ReturnsAsync(new LocalSigningIdentityDiscoveryResult(new[] { identity }, Array.Empty<string>()));
        localCertificates
            .Setup(x => x.ExportP12Async(It.IsAny<IReadOnlyList<LocalSigningIdentity>>(), It.IsAny<string>()))
            .ReturnsAsync((IReadOnlyList<LocalSigningIdentity> _, string password) =>
                CreateKeychainBundle(bundle, "source-password", password));
        var (service, adapter, _) = CreateCertificateBackupService(
            new[] { certificate }, _ => null, localCertificates.Object);
        adapter.SetupSequence(x => x.ListProviderItemsAsync(LocalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { certificate })
            .ThrowsAsync(new InvalidOperationException("Vault locked"));
        var initial = await service.GetBackupCertificatesAsync();
        var selection = new BackupExportSelection
        {
            CertificateIds = initial.Certificates.Select(c => c.Item.Id).ToList()
        };

        var encrypted = await service.ExportSettingsAsync("password123", selection);
        var imported = await service.ImportBackupAsync(encrypted, "password123");

        imported.Certificates.Should().ContainSingle(c => c.Item.Id == id && BackupService.HasPrivateKey(c));
        localCertificates.Verify(
            x => x.ExportP12Async(It.IsAny<IReadOnlyList<LocalSigningIdentity>>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task ExportSettingsAsync_WhenVaultPayloadHasAnotherId_RejectsIt()
    {
        var (p12, _) = CreateP12("vault-pw", includePrivateKey: true);
        var selected = new SecretItemRef(SecretItemKind.Certificate, "ABC123", "Selected certificate");
        var (service, _, _) = CreateCertificateBackupService(
            new[] { selected }, _ => CreateCertificatePayload("DEF456", p12, "vault-pw"));
        var selection = new BackupExportSelection { CertificateIds = new List<string> { selected.Id } };

        var act = () => service.ExportSettingsAsync("password123", selection);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Selected certificate*does not match the selected certificate*");
    }

    [Fact]
    public async Task ExportSettingsAsync_WhenVaultPayloadContainsAnotherCertificate_RejectsIt()
    {
        var (p12, _) = CreateP12("vault-pw", includePrivateKey: true);
        var selected = new SecretItemRef(SecretItemKind.Certificate, "ABC123", "Selected certificate");
        var (service, _, _) = CreateCertificateBackupService(
            new[] { selected }, _ => CreateCertificatePayload(selected.Id, p12, "vault-pw"));
        var selection = new BackupExportSelection { CertificateIds = new List<string> { selected.Id } };

        var act = () => service.ExportSettingsAsync("password123", selection);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*private key could not be included*Selected certificate*");
    }

    [Fact]
    public async Task ExportSettingsAsync_WhenKeychainExportHasDifferentSerial_RejectsIt()
    {
        var (bundle, _) = CreateP12("source-password", includePrivateKey: true);
        using var bundledCertificate = X509CertificateLoader.LoadPkcs12(bundle, "source-password");
        var identity = new LocalSigningIdentity(
            "identity", "Selected certificate", "TEAM", "ABC123", null, true, bundledCertificate.Thumbprint);
        var localCertificates = new Mock<ILocalCertificateService>();
        localCertificates.Setup(x => x.GetSigningIdentityDiscoveryAsync())
            .ReturnsAsync(new LocalSigningIdentityDiscoveryResult(new[] { identity }, Array.Empty<string>()));
        localCertificates.Setup(x => x.ExportP12Async(It.IsAny<IReadOnlyList<LocalSigningIdentity>>(), It.IsAny<string>()))
            .ReturnsAsync((IReadOnlyList<LocalSigningIdentity> _, string password) =>
                CreateKeychainBundle(bundle, "source-password", password));
        var (service, _, _) = CreateCertificateBackupService(
            Array.Empty<SecretItemRef>(), _ => null, localCertificates.Object);
        var selection = new BackupExportSelection { CertificateIds = new List<string> { "ABC123" } };

        var act = () => service.ExportSettingsAsync("password123", selection);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Selected certificate*exported identity does not match the selected certificate*");
    }

    [Fact]
    public async Task ExportSettingsAsync_WithoutCertificates_DoesNotAccessUnavailableVault()
    {
        var (service, adapter, _) = CreateCertificateBackupService(Array.Empty<SecretItemRef>(), _ => null);
        adapter.Setup(x => x.ListProviderItemsAsync(LocalId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Vault locked"));

        var encrypted = await service.ExportSettingsAsync("password123", new BackupExportSelection());
        var imported = await service.ImportBackupAsync(encrypted, "password123");

        imported.Selection.IncludePreferences.Should().BeTrue();
        imported.Certificates.Should().BeEmpty();
        adapter.Verify(x => x.ListProviderItemsAsync(LocalId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExportSettingsAsync_DefaultSelectionFailsWhenCertificateListIsIncomplete()
    {
        var (service, adapter, _) = CreateCertificateBackupService(Array.Empty<SecretItemRef>(), _ => null);
        adapter.Setup(x => x.ListProviderItemsAsync(LocalId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Vault locked"));

        var act = () => service.ExportSettingsAsync("password123");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Could not list all signing certificates*Local Vault*Vault locked*");
    }

    [Fact]
    public async Task RestoreStoredItemsAsync_RejectsCertificateWithoutPrivateKey()
    {
        var (p12, serial) = CreateP12("pw", includePrivateKey: false);
        var id = SecretItemAdapterHelper.SanitizeSerialNumber(serial);
        var (service, _, written) = CreateCertificateBackupService(Array.Empty<SecretItemRef>(), _ => null);
        var importResult = new BackupImportResult(
            new MauiSherpaSettings(),
            new BackupExportSelection { CertificateIds = new List<string> { id } },
            new[] { CreateCertificatePayload(id, p12, "pw") });

        var result = await service.RestoreStoredItemsAsync(importResult);

        result.CertificatesRestored.Should().Be(0);
        result.Failures.Should().ContainSingle(f => f.Contains("no matching private key"));
        written.Should().BeEmpty();
    }

    [Fact]
    public async Task RestoreStoredItemsAsync_RejectsCertificateWithDifferentSerial()
    {
        var (p12, _) = CreateP12("pw", includePrivateKey: true);
        var (service, _, written) = CreateCertificateBackupService(Array.Empty<SecretItemRef>(), _ => null);
        var importResult = new BackupImportResult(
            new MauiSherpaSettings(),
            new BackupExportSelection { CertificateIds = new List<string> { "ABC123" } },
            new[] { CreateCertificatePayload("ABC123", p12, "pw") });

        var result = await service.RestoreStoredItemsAsync(importResult);

        result.CertificatesRestored.Should().Be(0);
        result.Failures.Should().ContainSingle(f => f.Contains("no matching private key"));
        written.Should().BeEmpty();
    }

    private static byte[] CreateKeychainBundle(byte[] p12, string sourcePassword, string password)
    {
        var collection = X509CertificateLoader.LoadPkcs12Collection(p12, sourcePassword, X509KeyStorageFlags.Exportable);
        try
        {
            return collection.Export(X509ContentType.Pfx, password)!;
        }
        finally
        {
            foreach (var certificate in collection)
                certificate.Dispose();
        }
    }

    [Fact]
    public async Task ExportSettingsAsync_FailsWhenSelectedCertificateIsNoLongerAvailable()
    {
        var (service, _, _) = CreateCertificateBackupService(Array.Empty<SecretItemRef>(), _ => null);

        var act = () => service.ExportSettingsAsync("password123", new BackupExportSelection
        {
            IncludePreferences = true,
            CertificateIds = new List<string> { "GONE123" }
        });

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*no longer available*");
    }

    [Fact]
    public async Task RestoreStoredItemsAsync_ReportsFailuresWhenLocalVaultCannotBeEnabled()
    {
        var (p12, serial) = CreateP12("pw", includePrivateKey: true);
        var id = SecretItemAdapterHelper.SanitizeSerialNumber(serial);
        _mockCloudSecretsService
            .Setup(x => x.EnableDefaultLocalProviderAsync(false))
            .ThrowsAsync(new InvalidOperationException("vault locked"));
        var (service, _, written) = CreateCertificateBackupService(Array.Empty<SecretItemRef>(), _ => null);
        var importResult = new BackupImportResult(
            new MauiSherpaSettings(),
            new BackupExportSelection { CertificateIds = new List<string> { id } },
            new[] { CreateCertificatePayload(id, p12, "pw") });

        var result = await service.RestoreStoredItemsAsync(importResult);

        result.CertificatesRestored.Should().Be(0);
        result.Failures.Should().ContainSingle(f => f.Contains("vault locked"));
        written.Should().BeEmpty();
    }
}
