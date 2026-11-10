using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public sealed class SecretBackupActionsTests
{
    [Fact]
    public async Task PickProviderAsync_DuplicateNamesMapToTheChosenProvider()
    {
        var providers = new[]
        {
            Provider("first", "Shared"),
            Provider("second", "Shared"),
            Provider("third", "Cancel")
        };
        var secrets = new Mock<ICloudSecretsService>();
        secrets.Setup(service => service.GetProvidersAsync()).ReturnsAsync(providers);
        var alerts = new Mock<IAlertService>();
        string[] labels = [];
        alerts.Setup(service => service.ShowActionSheetAsync(
                "Choose", "Cancel", null, It.IsAny<string[]>()))
            .Returns((string _, string? _, string? _, string[] buttons) =>
            {
                labels = buttons;
                return Task.FromResult<string?>(buttons[1]);
            });

        var selected = await SecretBackupActions.PickProviderAsync(
            secrets.Object, alerts.Object, "Choose");

        selected.Should().Be(providers[1]);
        labels.Should().OnlyHaveUniqueItems();
        labels.Should().NotContain("Cancel");
    }

    [Fact]
    public async Task PickProviderAsync_CancelDoesNotSelectAProviderNamedCancel()
    {
        var secrets = new Mock<ICloudSecretsService>();
        secrets.Setup(service => service.GetProvidersAsync())
            .ReturnsAsync([Provider("first", "Cancel"), Provider("second", "Other")]);
        var alerts = new Mock<IAlertService>();
        alerts.Setup(service => service.ShowActionSheetAsync(
                "Choose", "Cancel", null, It.IsAny<string[]>()))
            .ReturnsAsync("Cancel");

        var selected = await SecretBackupActions.PickProviderAsync(
            secrets.Object, alerts.Object, "Choose");

        selected.Should().BeNull();
    }

    [Fact]
    public async Task QueueBackupAsync_PreservesTheProfileIdentity()
    {
        var coordinator = new Mock<ISecretSyncCoordinator>();
        coordinator.Setup(service => service.UpdateDesiredProvidersAsync(
                It.IsAny<SecretItemRef>(),
                It.IsAny<IReadOnlyDictionary<string, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("task-id");
        var item = new SecretItemRef(SecretItemKind.ProvisioningProfile, "profile", "Profile")
        {
            AppleIdentityId = "identity-a"
        };

        var queued = await SecretBackupActions.QueueBackupAsync(coordinator.Object, [item], "remote");

        queued.Should().Be(1);
        coordinator.Verify(service => service.UpdateDesiredProvidersAsync(
            It.Is<SecretItemRef>(value => value.AppleIdentityId == "identity-a"),
            It.Is<IReadOnlyDictionary<string, bool>>(changes => changes["remote"]),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static CloudSecretsProviderConfig Provider(string id, string name) =>
        new(id, name, CloudSecretsProviderType.AzureKeyVault, new Dictionary<string, string>());
}
