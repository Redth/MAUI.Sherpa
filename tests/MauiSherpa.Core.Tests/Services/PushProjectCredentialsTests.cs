using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class PushProjectCredentialsTests
{
    [Fact]
    public async Task ReplacingPastedCredentials_DoesNotChangeCopiedProject()
    {
        var vault = CreateVault();
        using var firstKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var replacementKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var original = firstKey.ExportPkcs8PrivateKeyPem();
        var replacement = replacementKey.ExportPkcs8PrivateKeyPem();

        var originalReference = await PushProjectCredentials.StorePastedAsync(vault.Object, PushProjectPlatform.Apns, original);
        var replacementReference = await PushProjectCredentials.StorePastedAsync(vault.Object, PushProjectPlatform.Apns, replacement);

        replacementReference.Should().NotBe(originalReference);
        (await PushProjectCredentials.ReadPastedAsync(vault.Object, originalReference)).Should().Be(original);
        (await PushProjectCredentials.ReadPastedAsync(vault.Object, replacementReference)).Should().Be(replacement);
        var serialized = JsonSerializer.Serialize(new PushProject
        {
            ApnsConfig = new() { AuthMode = "paste", P8VaultKey = replacementReference }
        });
        serialized.Should().Contain(replacementReference).And.NotContain("PRIVATE KEY");
    }

    [Theory]
    [InlineData("file")]
    [InlineData("paste")]
    [InlineData("secret")]
    public async Task CustomFcmCredentials_DoNotRequireASavedIdentity(string mode)
    {
        var json = CreateServiceAccount();
        var files = new Mock<IFileSystemService>(MockBehavior.Strict);
        files.Setup(f => f.ReadFileAsync("custom.json")).ReturnsAsync(json);
        var secrets = new Mock<IManagedSecretsService>(MockBehavior.Strict);
        secrets.Setup(s => s.GetValueAsync("fcm/account.json", It.IsAny<CancellationToken>())).ReturnsAsync(Encoding.UTF8.GetBytes(json));
        var vault = CreateVault();
        var reference = mode == "paste" ? await PushProjectCredentials.StorePastedAsync(vault.Object, PushProjectPlatform.Fcm, json) : null;
        var identities = new Mock<IGoogleIdentityService>(MockBehavior.Strict);

        var content = await PushProjectCredentials.ReadFcmAsync(new()
        {
            AuthMode = mode,
            ServiceAccountFilePath = "custom.json",
            ServiceAccountSecretKey = "fcm/account.json",
            ServiceAccountVaultKey = reference,
            SelectedGoogleIdentityId = "stale-identity"
        }, identities.Object, secrets.Object, vault.Object, files.Object);

        content.Should().Be(json);
        identities.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("p8file")]
    [InlineData("paste")]
    [InlineData("secret")]
    public async Task CustomApnsCredentials_ResolveIdentifierSecretsWhenRead(string mode)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = key.ExportPkcs8PrivateKeyPem();
        var files = new Mock<IFileSystemService>(MockBehavior.Strict);
        files.Setup(f => f.ReadFileAsync("custom.p8")).ReturnsAsync(pem);
        var secrets = new Mock<IManagedSecretsService>(MockBehavior.Strict);
        secrets.Setup(s => s.GetValueAsync("apns/key.p8", It.IsAny<CancellationToken>())).ReturnsAsync(Encoding.UTF8.GetBytes(pem));
        secrets.Setup(s => s.GetValueAsync("apns/key-id", It.IsAny<CancellationToken>())).ReturnsAsync(Encoding.UTF8.GetBytes("PROVIDER01"));
        var teamId = "TEAM000001";
        secrets.Setup(s => s.GetValueAsync("apns/team-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Encoding.UTF8.GetBytes(teamId));
        var vault = CreateVault();
        var reference = mode == "paste" ? await PushProjectCredentials.StorePastedAsync(vault.Object, PushProjectPlatform.Apns, pem) : null;
        var identities = new Mock<IAppleIdentityService>(MockBehavior.Strict);
        var config = new ApnsPushProjectConfig
        {
            AuthMode = mode, P8FilePath = "custom.p8", P8SecretKey = "apns/key.p8", P8VaultKey = reference,
            P8KeyId = "STALEKEY01", TeamId = "STALETEAM1", KeyIdSecretKey = "apns/key-id", TeamIdSecretKey = "apns/team-id"
        };

        var first = await PushProjectCredentials.ReadApnsAsync(config, identities.Object, secrets.Object, vault.Object, files.Object);
        teamId = "TEAM000002";
        var second = await PushProjectCredentials.ReadApnsAsync(config, identities.Object, secrets.Object, vault.Object, files.Object);

        first.Should().Be((pem, "PROVIDER01", "TEAM000001"));
        second.Should().Be((pem, "PROVIDER01", "TEAM000002"));
        identities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingPastedCredentials_FailRatherThanUsingAnOldIdentity()
    {
        var vault = CreateVault();
        var identities = new Mock<IGoogleIdentityService>(MockBehavior.Strict);
        var action = () => PushProjectCredentials.ReadFcmAsync(
            new() { AuthMode = "paste", ServiceAccountVaultKey = "missing", SelectedGoogleIdentityId = "old" },
            identities.Object, Mock.Of<IManagedSecretsService>(), vault.Object, Mock.Of<IFileSystemService>());

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*missing from Local Vault*");
        identities.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("""{"type":"authorized_user","project_id":"example","client_email":"test@example.invalid","private_key":"invalid"}""")]
    [InlineData("""{"type":"service_account","project_id":"example","client_email":"test@example.invalid","private_key":"invalid"}""")]
    [InlineData("""{"type":"service_account","project_id":42,"client_email":"test@example.invalid","private_key":"invalid"}""")]
    public void InvalidServiceAccount_IsRejectedWithoutEchoingKeyContent(string json)
    {
        var action = () => PushProjectCredentials.Validate(PushProjectPlatform.Fcm, json);
        action.Should().Throw<ArgumentException>().Which.Message.Should().NotContain(json);
    }

    [Fact]
    public void Apns_RejectsPublicKeysAndWrongCurves()
    {
        using var publicKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var wrongCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        var publicAction = () => PushProjectCredentials.Validate(PushProjectPlatform.Apns, publicKey.ExportSubjectPublicKeyInfoPem());
        var wrongCurveAction = () => PushProjectCredentials.Validate(PushProjectPlatform.Apns, wrongCurve.ExportPkcs8PrivateKeyPem());

        publicAction.Should().Throw<ArgumentException>();
        wrongCurveAction.Should().Throw<ArgumentException>();
    }

    static string CreateServiceAccount()
    {
        using var key = RSA.Create(2048);
        return JsonSerializer.Serialize(new
        {
            type = "service_account",
            project_id = "example-project",
            client_email = "notifications@example.invalid",
            private_key = key.ExportPkcs8PrivateKeyPem(),
            token_uri = "https://oauth2.googleapis.com/token"
        });
    }

    static Mock<ILocalVaultStore> CreateVault()
    {
        var items = new Dictionary<string, LocalVaultItem>();
        var vault = new Mock<ILocalVaultStore>(MockBehavior.Strict);
        vault.Setup(v => v.PutAsync(It.IsAny<string>(), "/", It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string scope, string path, string key, byte[] value, string contentType, Dictionary<string, string>? metadata, CancellationToken ct) =>
            {
                var item = new LocalVaultItem { Scope = scope, Path = path, Key = key, Value = value, ContentType = contentType };
                items.Add(key, item);
                return item;
            });
        vault.Setup(v => v.GetAsync(It.IsAny<string>(), "/", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string scope, string path, string key, CancellationToken ct) =>
                items.GetValueOrDefault(key) is { } item && item.Scope == scope ? item : null);
        return vault;
    }
}
