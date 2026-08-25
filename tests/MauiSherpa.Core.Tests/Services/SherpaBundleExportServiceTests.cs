using FluentAssertions;
using MauiSherpa.Bundles;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class SherpaBundleExportServiceTests
{
    [Fact]
    public async Task ExportAsync_EmbedsResolvedBinaryAssetsAndSecrets()
    {
        var profileService = new Mock<IPublishProfileService>();
        profileService.Setup(service => service.GetProfileAsync("profile"))
            .ReturnsAsync(CreateProfile());
        profileService.Setup(service => service.ResolveSecretsAsync(
                It.IsAny<PublishProfile>(),
                It.IsAny<IProgress<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                ["ANDROID_RELEASE_KEYSTORE"] = Convert.ToBase64String([1, 2, 3]),
                ["ANDROID_RELEASE_KEYSTORE_PASSWORD"] = "secret-password",
                ["serviceAccountJsonPath"] = "{\"type\":\"service_account\",\"project_id\":\"sample\"}"
            });
        var service = new SherpaBundleExportService(profileService.Object);

        var encrypted = await service.ExportAsync(CreateDefinition(), "password123");
        var bundle = SherpaBundleFile.Decrypt(encrypted, "password123");

        bundle.Assets.Should().ContainKeys(
            "ANDROID_RELEASE_KEYSTORE",
            "serviceAccountJsonPath");
        bundle.Assets["ANDROID_RELEASE_KEYSTORE"].PasswordVariable
            .Should().Be("ANDROID_RELEASE_KEYSTORE_PASSWORD");
        bundle.Variables.Should().ContainKey("ANDROID_RELEASE_KEYSTORE_PASSWORD")
            .WhoseValue.Should().Be("secret-password");
        bundle.SecretVariables.Should().Contain("ANDROID_RELEASE_KEYSTORE_PASSWORD");
        bundle.Environments["production"].Platforms[BundlePlatform.Android]
            .Install.AssetIds.Should().BeEquivalentTo(
                ["ANDROID_RELEASE_KEYSTORE", "serviceAccountJsonPath"]);
    }

    private static SherpaBundleDefinition CreateDefinition() => new()
    {
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

    private static PublishProfile CreateProfile() => new(
        Id: "profile",
        Name: "Profile",
        Description: null,
        PublisherId: null,
        RepositoryId: null,
        RepositoryFullName: null,
        AppleConfigs: [],
        AndroidConfigs: [],
        SecretMappings: [],
        CreatedAt: DateTime.UtcNow,
        UpdatedAt: DateTime.UtcNow);
}
