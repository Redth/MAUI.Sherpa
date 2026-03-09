using FluentAssertions;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Tests;

public class CapabilityCategoriesTests
{
    [Theory]
    [InlineData("CARPLAY_PLAYABLE_CONTENT")]
    [InlineData("MARZIPAN")]
    [InlineData("COMMUNICATION_NOTIFICATIONS")]
    [InlineData("GROUP_ACTIVITIES")]
    [InlineData("EXPOSURE_NOTIFICATION")]
    [InlineData("EXTENDED_VIRTUAL_ADDRESSING")]
    [InlineData("MDMMANAGED_ASSOCIATED_DOMAINS")]
    [InlineData("FILE_PROVIDER_TESTING_MODE")]
    [InlineData("HEALTH_KIT_RECALIBRATE_ESTIMATES")]
    [InlineData("TIME_SENSITIVE_NOTIFICATIONS")]
    [InlineData("FAMILY_CONTROLS")]
    public void IsToggleable_ReturnsFalse_ForNonToggleableCapabilities(string capabilityType)
    {
        CapabilityCategories.IsToggleable(capabilityType).Should().BeFalse();
    }

    [Theory]
    [InlineData("ICLOUD")]
    [InlineData("IN_APP_PURCHASE")]
    [InlineData("GAME_CENTER")]
    [InlineData("PUSH_NOTIFICATIONS")]
    [InlineData("WALLET")]
    [InlineData("ASSOCIATED_DOMAINS")]
    [InlineData("APP_GROUPS")]
    [InlineData("HEALTHKIT")]
    [InlineData("HOMEKIT")]
    [InlineData("APPLE_PAY")]
    [InlineData("DATA_PROTECTION")]
    [InlineData("SIRIKIT")]
    [InlineData("NETWORK_EXTENSIONS")]
    [InlineData("NFC_TAG_READING")]
    [InlineData("APPLE_ID_AUTH")]
    public void IsToggleable_ReturnsTrue_ForApiSupportedCapabilities(string capabilityType)
    {
        CapabilityCategories.IsToggleable(capabilityType).Should().BeTrue();
    }

    [Fact]
    public void NonToggleableCapabilities_ContainsAllKnownNonToggleableTypes()
    {
        CapabilityCategories.NonToggleableCapabilities.Should().HaveCount(11);
    }
}
