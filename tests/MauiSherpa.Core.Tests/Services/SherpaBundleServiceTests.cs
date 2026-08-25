using FluentAssertions;
using MauiSherpa.Bundles;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class SherpaBundleServiceTests
{
    [Fact]
    public async Task SaveBundleAsync_PersistsDefinitionUnderBundlePrefix()
    {
        var cloud = CreateCloudService();
        byte[]? stored = null;
        cloud.Setup(service => service.StoreSecretAsync(
                "sherpa-bundles/bundle-id",
                It.IsAny<byte[]>(),
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], Dictionary<string, string>?, CancellationToken>(
                (_, bytes, _, _) => stored = bytes)
            .ReturnsAsync(true);
        var service = new SherpaBundleService(cloud.Object);

        await service.SaveBundleAsync(CreateDefinition());

        stored.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString(stored!).Should().Contain("\"name\":\"Sample\"");
    }

    [Fact]
    public async Task SaveBundleAsync_WhenProviderRejectsWrite_Throws()
    {
        var cloud = CreateCloudService();
        cloud.Setup(service => service.StoreSecretAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var service = new SherpaBundleService(cloud.Object);

        var act = () => service.SaveBundleAsync(CreateDefinition());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to save*");
    }

    [Fact]
    public async Task GetBundlesAsync_UsesPrefixAndOrdersByName()
    {
        var cloud = CreateCloudService();
        cloud.Setup(service => service.ListSecretsAsync(
                "sherpa-bundles/",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["sherpa-bundles/2", "sherpa-bundles/1"]);
        cloud.Setup(service => service.GetSecretAsync(
                "sherpa-bundles/2",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Serialize(CreateDefinition() with { Id = "2", Name = "Zulu" }));
        cloud.Setup(service => service.GetSecretAsync(
                "sherpa-bundles/1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Serialize(CreateDefinition() with { Id = "1", Name = "Alpha" }));
        var service = new SherpaBundleService(cloud.Object);

        var result = await service.GetBundlesAsync();

        result.Select(definition => definition.Name).Should().Equal("Alpha", "Zulu");
    }

    private static Mock<ICloudSecretsService> CreateCloudService()
    {
        var cloud = new Mock<ICloudSecretsService>();
        cloud.Setup(service => service.InitializeAsync()).Returns(Task.CompletedTask);
        cloud.SetupGet(service => service.ActiveProvider).Returns(new CloudSecretsProviderConfig(
            "local",
            "Local",
            CloudSecretsProviderType.Local,
            new Dictionary<string, string>()));
        return cloud;
    }

    private static byte[] Serialize(SherpaBundleDefinition definition) =>
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            definition,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

    private static SherpaBundleDefinition CreateDefinition() => new()
    {
        Id = "bundle-id",
        Name = "Sample",
        PublishProfileId = "profile",
        Template = new SherpaBundle
        {
            Name = "Sample",
            Environments = new Dictionary<string, SherpaBundleEnvironment>
            {
                ["production"] = new()
                {
                    Platforms = new Dictionary<BundlePlatform, BundlePlatformConfiguration>
                    {
                        [BundlePlatform.Android] = new()
                    }
                }
            }
        }
    };
}
