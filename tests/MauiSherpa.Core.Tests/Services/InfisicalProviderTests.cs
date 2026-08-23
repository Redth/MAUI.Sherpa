using System.Text;
using Infisical.Sdk;
using Infisical.Sdk.Model;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class InfisicalProviderTests
{
    [Fact]
    public async Task StoreSecretAsync_UpdatesWithoutPreflightRead_AndStoresNativeMetadata()
    {
        var sdk = CreateSdk();
        UpdateSecretOptions? captured = null;
        sdk.Setup(x => x.UpdateSecretAsync(
                It.IsAny<UpdateSecretOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<UpdateSecretOptions, CancellationToken>((options, _) => captured = options)
            .ReturnsAsync(new Secret());
        var provider = CreateProvider(sdk.Object);

        var result = await provider.StoreSecretAsync(
            "my-secret",
            Encoding.UTF8.GetBytes("value"),
            new Dictionary<string, string>
            {
                ["purpose"] = "test",
                ["owner"] = "sherpa",
            });

        Assert.True(result);
        Assert.NotNull(captured);
        Assert.Equal("MY_SECRET", captured.SecretName);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("value")), captured.NewSecretValue);
        Assert.Collection(
            captured.NewMetadata!,
            item =>
            {
                Assert.Equal("owner", item.Key);
                Assert.Equal("sherpa", item.Value);
            },
            item =>
            {
                Assert.Equal("purpose", item.Key);
                Assert.Equal("test", item.Value);
            });
        sdk.Verify(
            x => x.GetSecretAsync(It.IsAny<GetSecretOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
        sdk.Verify(
            x => x.CreateSecretAsync(It.IsAny<CreateSecretOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StoreSecretAsync_CreatesWhenUpdateReportsNotFound()
    {
        var sdk = CreateSdk();
        sdk.Setup(x => x.UpdateSecretAsync(
                It.IsAny<UpdateSecretOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());
        sdk.Setup(x => x.CreateSecretAsync(
                It.IsAny<CreateSecretOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Secret());
        var provider = CreateProvider(sdk.Object);

        var result = await provider.StoreSecretAsync("new-secret", [1, 2, 3]);

        Assert.True(result);
        sdk.Verify(
            x => x.UpdateSecretAsync(It.IsAny<UpdateSecretOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
        sdk.Verify(
            x => x.CreateSecretAsync(It.IsAny<CreateSecretOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
        sdk.Verify(
            x => x.GetSecretAsync(It.IsAny<GetSecretOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetSecretMetadataAsync_ReturnsNativeMetadataWithoutLegacyRead()
    {
        var sdk = CreateSdk();
        sdk.Setup(x => x.GetSecretAsync(
                It.IsAny<GetSecretOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Secret
            {
                Metadata =
                [
                    new SecretMetadata { Key = "owner", Value = "sherpa" },
                ],
            });
        var provider = CreateProvider(sdk.Object);

        var metadata = await provider.GetSecretMetadataAsync("my-secret");

        Assert.Equal("sherpa", metadata!["owner"]);
        sdk.Verify(
            x => x.GetSecretAsync(It.IsAny<GetSecretOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetSecretMetadataAsync_ReadsSanitizedLegacyMetadataKey()
    {
        var sdk = CreateSdk();
        sdk.SetupSequence(x => x.GetSecretAsync(
                It.IsAny<GetSecretOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Secret())
            .ReturnsAsync(new Secret
            {
                SecretValue = Convert.ToBase64String(
                    CloudSecretMetadata.Serialize(new Dictionary<string, string>
                    {
                        ["owner"] = "legacy",
                    })),
            });
        var provider = CreateProvider(sdk.Object);

        var metadata = await provider.GetSecretMetadataAsync("my-secret");

        Assert.Equal("legacy", metadata!["owner"]);
        var expectedLegacyKey = CloudSecretMetadata
            .GetMetadataKey("MY_SECRET")
            .Replace('-', '_')
            .ToUpperInvariant();
        sdk.Verify(
            x => x.GetSecretAsync(
                It.Is<GetSecretOptions>(options =>
                    options.SecretName == expectedLegacyKey),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StoreSecretAsync_EmptyMetadataDeletesLegacyMetadataAfterCreation()
    {
        var sdk = CreateSdk();
        sdk.Setup(x => x.UpdateSecretAsync(
                It.IsAny<UpdateSecretOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());
        sdk.Setup(x => x.CreateSecretAsync(
                It.IsAny<CreateSecretOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Secret());
        sdk.Setup(x => x.DeleteSecretAsync(
                It.IsAny<DeleteSecretOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound());
        var provider = CreateProvider(sdk.Object);

        var result = await provider.StoreSecretAsync(
            "new-secret",
            [1, 2, 3],
            new Dictionary<string, string>());

        Assert.True(result);
        var expectedLegacyKey = CloudSecretMetadata
            .GetMetadataKey("NEW_SECRET")
            .Replace('-', '_')
            .ToUpperInvariant();
        sdk.Verify(
            x => x.DeleteSecretAsync(
                It.Is<DeleteSecretOptions>(options =>
                    options.SecretName == expectedLegacyKey),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ConcurrentOperationsShareAuthentication()
    {
        var sdk = CreateSdk();
        sdk.Setup(x => x.GetSecretAsync(
                It.IsAny<GetSecretOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Secret());
        var provider = CreateProvider(sdk.Object);

        await Task.WhenAll(
            provider.SecretExistsAsync("first"),
            provider.SecretExistsAsync("second"));

        sdk.Verify(
            x => x.LoginAsync("client-id", "client-secret", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExpiredAuthenticationIsRefreshed()
    {
        var sdk = CreateSdk(expiresInSeconds: 100);
        sdk.Setup(x => x.GetSecretAsync(
                It.IsAny<GetSecretOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Secret());
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var provider = CreateProvider(sdk.Object, timeProvider);

        await provider.SecretExistsAsync("first");
        timeProvider.Advance(TimeSpan.FromSeconds(91));
        await provider.SecretExistsAsync("second");

        sdk.Verify(
            x => x.LoginAsync("client-id", "client-secret", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SecretExistsAsync_DoesNotTreatTransientFailuresAsMissing()
    {
        var sdk = CreateSdk();
        sdk.Setup(x => x.GetSecretAsync(
                It.IsAny<GetSecretOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InfisicalException("Failed to get secret", new HttpRequestException("Service unavailable")));
        var provider = CreateProvider(sdk.Object);

        await Assert.ThrowsAsync<InfisicalException>(() => provider.SecretExistsAsync("my-secret"));
    }

    private static Mock<IInfisicalSdkClient> CreateSdk(decimal expiresInSeconds = 3600)
    {
        var sdk = new Mock<IInfisicalSdkClient>(MockBehavior.Strict);
        sdk.Setup(x => x.LoginAsync(
                "client-id",
                "client-secret",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MachineIdentityCredential("token", expiresInSeconds, expiresInSeconds, "Bearer"));
        return sdk;
    }

    private static InfisicalProvider CreateProvider(
        IInfisicalSdkClient sdk,
        TimeProvider? timeProvider = null)
    {
        var config = new CloudSecretsProviderConfig(
            "infisical",
            "Infisical",
            CloudSecretsProviderType.Infisical,
            new Dictionary<string, string>
            {
                ["SiteUrl"] = "https://example.test",
                ["ClientId"] = "client-id",
                ["ClientSecret"] = "client-secret",
                ["ProjectId"] = "project-id",
                ["Environment"] = "prod",
                ["SecretPath"] = "/maui-sherpa",
            });

        return new InfisicalProvider(
            config,
            Mock.Of<ILoggingService>(),
            _ => sdk,
            timeProvider ?? TimeProvider.System);
    }

    private static InfisicalException NotFound() =>
        new("Failed to update secret", new HttpRequestException("Unexpected response: NotFound (404)"));

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan amount) => utcNow += amount;
    }
}
