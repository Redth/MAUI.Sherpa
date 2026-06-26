using System.Text;
using FluentAssertions;
using MauiSherpa.Bundle.Loading;
using MauiSherpa.Bundle.Models;
using MauiSherpa.Bundle.Pipeline;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Services;
using NSubstitute;

namespace MauiSherpa.Bundle.Tests;

/// <summary>
/// Tests the bundle-creation path the UI drives: <see cref="SherpaBundleBuilderService"/>
/// resolves signing material + secrets from a profile, assembles the bundle model,
/// and writes it as an encrypted SQLCipher file. The collaborating services are
/// mocked so the test stays hermetic.
/// </summary>
public sealed class SherpaBundleBuilderServiceTests : IDisposable
{
    private readonly ICloudSecretsService _cloud = Substitute.For<ICloudSecretsService>();
    private readonly ICertificateSyncService _certSync = Substitute.For<ICertificateSyncService>();
    private readonly IKeystoreService _keystores = Substitute.For<IKeystoreService>();
    private readonly ISecureStorageService _secureStorage = Substitute.For<ISecureStorageService>();
    private readonly IManagedSecretsService _managedSecrets = Substitute.For<IManagedSecretsService>();
    private readonly IAppleConnectService _appleConnect = Substitute.For<IAppleConnectService>();
    private readonly IAppleIdentityService _appleIdentity = Substitute.For<IAppleIdentityService>();
    private readonly IAppleIdentityStateService _identityState = Substitute.For<IAppleIdentityStateService>();
    private readonly ILoggingService _logger = Substitute.For<ILoggingService>();

    private readonly string _keystorePath = Path.Combine(Path.GetTempPath(), $"ks-{Guid.NewGuid():N}.keystore");
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    private static readonly byte[] P12Bytes = Encoding.UTF8.GetBytes("FAKE-P12-DER");
    private static readonly byte[] ProfileBytes = Encoding.UTF8.GetBytes("FAKE-MOBILEPROVISION");
    private static readonly byte[] KeystoreBytes = Encoding.UTF8.GetBytes("FAKE-JKS");

    public SherpaBundleBuilderServiceTests()
    {
        File.WriteAllBytes(_keystorePath, KeystoreBytes);
        _tempFiles.Add(_keystorePath);

        // Apple certificate: serial → cloud keys → p12 + password bytes.
        _certSync.GetCertificateSecretKey("SER123").Returns("cloud/cert/SER123");
        _certSync.GetCertificatePasswordKey("SER123").Returns("cloud/pwd/SER123");
        _cloud.GetSecretAsync("cloud/cert/SER123", Arg.Any<CancellationToken>()).Returns(P12Bytes);
        _cloud.GetSecretAsync("cloud/pwd/SER123", Arg.Any<CancellationToken>()).Returns(Encoding.UTF8.GetBytes("p12-pass"));

        // Provisioning profile download.
        _appleConnect.DownloadProfileAsync("PROF1").Returns(ProfileBytes);

        // Android keystore + its secure-storage password.
        _keystores.ListKeystoresAsync().Returns(new[]
        {
            new AndroidKeystore("KS1", "upload", _keystorePath, "PKCS12", DateTime.UtcNow),
        });
        _secureStorage.GetAsync("android_keystore_pwd_KS1").Returns("ks-pass");

        // Managed secret feeding a replace token.
        _managedSecrets.GetValueAsync("API_TOKEN", Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("https://sentry.example/dsn"));

        // App Store Connect identity for the TestFlight deploy target.
        _appleIdentity.GetIdentityAsync("ID1")
            .Returns(new AppleIdentity("ID1", "Prod Key", "KEYID9", "ISSUER9", null, "P8-KEY-CONTENT"));
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { if (File.Exists(f)) File.Delete(f); } catch { /* best effort */ }
        foreach (var d in _tempDirs)
            try { if (Directory.Exists(d)) Directory.Delete(d, recursive: true); } catch { /* best effort */ }
    }

    private SherpaBundleBuilderService CreateSut() => new(
        _cloud, _certSync, _keystores, _secureStorage, _managedSecrets,
        _appleConnect, _appleIdentity, _identityState, _logger);

    private static PublishProfile SampleProfile() => new(
        Id: "p1",
        Name: "Acme",
        Description: null,
        PublisherId: null,
        RepositoryId: null,
        RepositoryFullName: null,
        AppleConfigs: new List<PublishProfileAppleConfig>
        {
            new(
                Label: "iOS Distribution",
                IdentityId: null,
                Platform: ApplePlatformType.iOS,
                DistributionType: null,
                CertificateSerialNumber: "SER123",
                InstallerCertSerialNumber: null,
                ProfileId: "PROF1",
                ProfileUuid: null,
                IncludeNotarization: false,
                NotarizationAppleIdSecretKey: null,
                NotarizationPasswordSecretKey: null,
                NotarizationTeamIdSecretKey: null,
                NotarizationAppleIdManualValue: null,
                NotarizationPasswordManualValue: null,
                NotarizationTeamIdManualValue: null,
                KeyMappings: new Dictionary<string, List<string>>()),
        },
        AndroidConfigs: new List<PublishProfileAndroidConfig>
        {
            new(Label: "Android", KeystoreId: "KS1", KeyMappings: new Dictionary<string, List<string>>()),
        },
        SecretMappings: new List<PublishProfileSecretMapping>
        {
            new(SourceKey: "API_TOKEN", DestinationKeys: new List<string> { "SentryDsn" }),
        },
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: DateTime.UtcNow)
    {
        BundleSettings = new PublishProfileBundleSettings
        {
            EnvironmentName = "Production",
            Platforms = new List<BundlePlatform> { BundlePlatform.iOS, BundlePlatform.Android },
            Variables = new Dictionary<string, string> { ["buildNumber"] = "42" },
            ReplaceTokens = new Dictionary<string, string> { ["ManualToken"] = "manual-value" },
            DeployTargets = new List<PublishProfileDeployTarget>
            {
                new("TestFlight", BundlePlatform.iOS) { AppleIdentityId = "ID1" },
                new("PlayStore", BundlePlatform.Android) { Fields = new Dictionary<string, string> { ["Track"] = "internal" } },
            },
        },
    };

    [Fact]
    public async Task BuildAsync_assembles_apple_material_under_named_environment()
    {
        var bundle = await CreateSut().BuildAsync(SampleProfile());

        bundle.Environments.Should().ContainKey("Production");
        var env = bundle.Environments["Production"];

        env.IOS!.Setup!.Certificates!.Single().Content.Should().Be(Convert.ToBase64String(P12Bytes));
        env.IOS.Setup.Certificates!.Single().Password.Should().Be("p12-pass");
        env.IOS.Setup.Profiles!.Single().Content.Should().Be(Convert.ToBase64String(ProfileBytes));
    }

    [Fact]
    public async Task BuildAsync_assembles_android_keystore_with_passwords()
    {
        var bundle = await CreateSut().BuildAsync(SampleProfile());
        var ks = bundle.Environments["Production"].Android!.Setup!.Keystores!.Single();

        ks.Content.Should().Be(Convert.ToBase64String(KeystoreBytes));
        ks.KeyAlias.Should().Be("upload");
        ks.StorePassword.Should().Be("ks-pass");
        ks.KeyPassword.Should().Be("ks-pass");
    }

    [Fact]
    public async Task BuildAsync_resolves_secret_mappings_into_replace_tokens()
    {
        var bundle = await CreateSut().BuildAsync(SampleProfile());
        var env = bundle.Environments["Production"];

        env.ReplaceTokens!["SentryDsn"].Should().Be("https://sentry.example/dsn"); // from managed secret
        env.ReplaceTokens!["ManualToken"].Should().Be("manual-value");             // from settings
    }

    [Fact]
    public async Task BuildAsync_inlines_appstore_connect_key_into_testflight_deploy_target()
    {
        var bundle = await CreateSut().BuildAsync(SampleProfile());

        var deploy = bundle.Environments["Production"].IOS!.Deploy!.Single();
        deploy.Provider.Should().Be("TestFlight");
        deploy.GetString("KeyId").Should().Be("KEYID9");
        deploy.GetString("IssuerId").Should().Be("ISSUER9");
        deploy.GetString("ApiKey").Should().Be(Convert.ToBase64String(Encoding.UTF8.GetBytes("P8-KEY-CONTENT")));

        var androidDeploy = bundle.Environments["Production"].Android!.Deploy!.Single();
        androidDeploy.Provider.Should().Be("PlayStore");
        androidDeploy.GetString("Track").Should().Be("internal");
    }

    [Fact]
    public async Task BuildAsync_honors_platform_selection()
    {
        var profile = SampleProfile() with { };
        profile.BundleSettings!.Platforms.Clear();
        profile.BundleSettings.Platforms.Add(BundlePlatform.Android); // Android only

        var bundle = await CreateSut().BuildAsync(profile);

        bundle.Environments["Production"].Android.Should().NotBeNull();
        bundle.Environments["Production"].IOS.Should().BeNull();
    }

    [Fact]
    public async Task BuildAndSaveAsync_writes_encrypted_bundle_that_round_trips_with_password()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bundle-{Guid.NewGuid():N}.sherpabundle");
        _tempFiles.Add(path);

        await CreateSut().BuildAndSaveAsync(SampleProfile(), path, "s3cret");

        File.Exists(path).Should().BeTrue();
        // The file is an encrypted database, not the legacy plain-JSON format.
        new JsonBundleLoader().CanLoad(path).Should().BeFalse();
        new SqlCipherBundleLoader().CanLoad(path).Should().BeTrue();

        var reloaded = await SqlCipherBundleStore.LoadAsync(path, "s3cret");
        reloaded.Environments.Should().ContainKey("Production");
        reloaded.Environments["Production"].IOS!.Setup!.Certificates!.Single().Password.Should().Be("p12-pass");
        reloaded.Environments["Production"].Android!.Setup!.Keystores!.Single().StorePassword.Should().Be("ks-pass");
        reloaded.Environments["Production"].IOS!.Deploy!.Single().GetString("KeyId").Should().Be("KEYID9");
    }

    [Fact]
    public async Task BuildAndSaveAsync_wrong_password_cannot_open_bundle()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bundle-{Guid.NewGuid():N}.sherpabundle");
        _tempFiles.Add(path);

        await CreateSut().BuildAndSaveAsync(SampleProfile(), path, "right-password");

        var act = () => SqlCipherBundleStore.LoadAsync(path, "wrong-password");
        await act.Should().ThrowAsync<SherpaBundleException>();
    }

    [Fact]
    public async Task BuildAndSaveAsync_requires_a_password()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bundle-{Guid.NewGuid():N}.sherpabundle");

        var act = () => CreateSut().BuildAndSaveAsync(SampleProfile(), path, "");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Full loop: the UI builder writes an encrypted bundle, and the CLI's pipeline
    /// (driven with a fake process runner) decrypts it with the same password and
    /// drives the build for the selected platform.
    /// </summary>
    [Fact]
    public async Task EndToEnd_uiBuiltBundle_isConsumedBy_pipeline_with_password()
    {
        var dir = Directory.CreateTempSubdirectory("sherpa-e2e-").FullName;
        _tempDirs.Add(dir);
        File.WriteAllText(
            Path.Combine(dir, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net10.0-android;net10.0-ios</TargetFrameworks></PropertyGroup></Project>");
        var bundlePath = Path.Combine(dir, "app.sherpabundle");

        // UI side: build + encrypt.
        await CreateSut().BuildAndSaveAsync(SampleProfile(), bundlePath, "shared-pw");

        // CLI side: load + decrypt + build.
        var process = new RecordingProcessRunner();
        var options = new SherpaRunOptions
        {
            BundlePath = bundlePath,
            Environment = "Production",
            Platforms = new[] { SherpaPlatform.IOS },
            Steps = new[] { SherpaStep.Build },
            ProjectPath = Path.Combine(dir, "App.csproj"),
            Password = "shared-pw",
        };

        var result = await new SherpaPipeline(process: process).RunAsync(options, NullSherpaLog.Instance);

        result.Platforms.Should().ContainKey("iOS");
        process.AllOf("dotnet").Single(i => i.Arguments.Contains("publish"))
            .Arguments.Should().Contain("net10.0-ios");
    }
}
