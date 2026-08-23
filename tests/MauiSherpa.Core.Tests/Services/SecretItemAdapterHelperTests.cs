using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class SecretItemAdapterHelperTests
{
    [Fact]
    public async Task WriteArtifactsAsync_ReadsSnapshotsWithoutSeparateExistenceChecks()
    {
        var provider = new Mock<ICloudSecretsProvider>(MockBehavior.Strict);
        provider.SetupGet(x => x.ProviderType).Returns(CloudSecretsProviderType.Infisical);
        provider.SetupGet(x => x.DisplayName).Returns("Infisical");
        provider.Setup(x => x.ListSecretsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        provider.Setup(x => x.StoreSecretAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var artifacts = new[]
        {
            new SecretArtifact("first", [1]),
            new SecretArtifact("second", [2]),
        };

        var result = await SecretItemAdapterHelper.WriteArtifactsAsync(
            provider.Object,
            artifacts,
            CancellationToken.None);

        Assert.True(result);
        provider.Verify(
            x => x.SecretExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        provider.Verify(
            x => x.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        provider.Verify(
            x => x.StoreSecretAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task WriteArtifactsAsync_DoesNotUseListAsSnapshotForOtherProviders()
    {
        var provider = new Mock<ICloudSecretsProvider>(MockBehavior.Strict);
        provider.SetupGet(x => x.ProviderType).Returns(CloudSecretsProviderType.AzureKeyVault);
        provider.SetupGet(x => x.DisplayName).Returns("Azure Key Vault");
        provider.Setup(x => x.GetSecretAsync("first", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        provider.Setup(x => x.StoreSecretAsync(
                "first",
                It.IsAny<byte[]>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await SecretItemAdapterHelper.WriteArtifactsAsync(
            provider.Object,
            [new SecretArtifact("first", [1])],
            CancellationToken.None);

        Assert.True(result);
        provider.Verify(
            x => x.ListSecretsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        provider.Verify(
            x => x.GetSecretAsync("first", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
