using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public sealed class ProvisioningProfileItemAdapterTests
{
    private readonly Mock<IAppleConnectService> _apple = new();
    private readonly Mock<IAppleIdentityStateService> _identity = new();

    [Fact]
    public async Task ReadLocalAsync_UsesOriginalIdentityEvenWhenAnotherIsSelected()
    {
        _identity.SetupGet(state => state.SelectedIdentity)
            .Returns(new AppleIdentity("identity-b", "Account B", "key", "issuer", null, null));
        var profile = CreateProfile();
        _apple.Setup(service => service.GetProfilesForIdentityAsync("identity-a"))
            .ReturnsAsync([profile]);
        _apple.Setup(service => service.DownloadProfileForIdentityAsync(profile.Id, "identity-a"))
            .ReturnsAsync([1, 2, 3]);
        var item = CreateItem("identity-a");

        var payload = await CreateAdapter().ReadLocalAsync(item);

        payload.Should().NotBeNull();
        payload!.Item.AppleIdentityId.Should().Be("identity-a");
        payload.Artifacts.Should().Contain(artifact =>
            artifact.Key.EndsWith("/data", StringComparison.Ordinal) &&
            artifact.Value.SequenceEqual(new byte[] { 1, 2, 3 }));
        _apple.Verify(service => service.GetProfilesForIdentityAsync("identity-a"), Times.Once);
        _apple.Verify(service => service.DownloadProfileForIdentityAsync(profile.Id, "identity-a"), Times.Once);
        _apple.Verify(service => service.GetProfilesAsync(), Times.Never);
        _apple.Verify(service => service.DownloadProfileAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ReadLocalAsync_RejectsLegacyItemWithoutAnIdentity()
    {
        var action = () => CreateAdapter().ReadLocalAsync(CreateItem(null));

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no Apple identity*start the backup again*");
        _apple.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadLocalAsync_RejectsEmptyProfileData()
    {
        var profile = CreateProfile();
        _apple.Setup(service => service.GetProfilesForIdentityAsync("identity-a"))
            .ReturnsAsync([profile]);
        _apple.Setup(service => service.DownloadProfileForIdentityAsync(profile.Id, "identity-a"))
            .ReturnsAsync([]);

        var action = () => CreateAdapter().ReadLocalAsync(CreateItem("identity-a"));

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no data*try the backup again*");
    }

    [Fact]
    public async Task ListLocalItemsAsync_AttachesTheSelectedIdentity()
    {
        _identity.SetupGet(state => state.SelectedIdentity)
            .Returns(new AppleIdentity("identity-a", "Account A", "key", "issuer", null, null));
        _apple.Setup(service => service.GetProfilesForIdentityAsync("identity-a"))
            .ReturnsAsync([CreateProfile()]);

        var items = await CreateAdapter().ListLocalItemsAsync();

        items.Should().ContainSingle().Which.AppleIdentityId.Should().Be("identity-a");
    }

    private ProvisioningProfileItemAdapter CreateAdapter() =>
        new(Mock.Of<ISecretsProviderRegistry>(), _apple.Object, _identity.Object);

    private static AppleProfile CreateProfile() =>
        new("profile-id", "Profile", "IOS_APP_DEVELOPMENT", "IOS", "ACTIVE",
            DateTime.UtcNow, null, "profile-uuid");

    private static SecretItemRef CreateItem(string? identityId) =>
        new(SecretItemKind.ProvisioningProfile, "profile-id", "Profile")
        {
            AppleIdentityId = identityId
        };
}
