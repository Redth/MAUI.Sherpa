using FluentAssertions;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class AppleProfileTypesTests
{
    [Theory]
    [InlineData("TVOS_APP_DEVELOPMENT", "tvOS Development")]
    [InlineData("TVOS_APP_ADHOC", "tvOS Ad Hoc")]
    [InlineData("TVOS_APP_STORE", "tvOS App Store")]
    [InlineData("IOS_APP_STORE", "iOS App Store")]
    [InlineData("MAC_CATALYST_APP_DIRECT", "Mac Catalyst Direct")]
    [InlineData("SOMETHING_NEW", "SOMETHING NEW")]
    [InlineData(null, "-")]
    public void GetDisplayName_NamesEachProfileType(string? profileType, string expected) =>
        AppleProfileTypes.GetDisplayName(profileType).Should().Be(expected);

    [Theory]
    [InlineData("TVOS_APP_STORE", "IOS", "TVOS")]
    [InlineData("IOS_APP_STORE", "IOS", "IOS")]
    [InlineData("MAC_APP_STORE", "MAC_OS", "MAC_OS")]
    [InlineData("MAC_CATALYST_APP_STORE", "IOS", "IOS")]
    [InlineData("MAC_CATALYST_APP_STORE", "MAC_OS", "MAC_OS")]
    public void GetPlatform_PrefersTheProfileTypeOverTheReportedPlatform(
        string profileType,
        string reportedPlatform,
        string expected) =>
        AppleProfileTypes.GetPlatform(profileType, reportedPlatform).Should().Be(expected);

    [Theory]
    [InlineData("TVOS_APP_DEVELOPMENT", "IOS", "APPLE_TV", true)]
    [InlineData("TVOS_APP_DEVELOPMENT", "IOS", "IPHONE", false)]
    [InlineData("IOS_APP_DEVELOPMENT", "IOS", "APPLE_TV", false)]
    [InlineData("IOS_APP_DEVELOPMENT", "IOS", "IPAD", true)]
    [InlineData("IOS_APP_ADHOC", "IOS", "APPLE_VISION_PRO", true)]
    [InlineData("IOS_APP_DEVELOPMENT", "IOS", "APPLE_WATCH", true)]
    [InlineData("TVOS_APP_DEVELOPMENT", "IOS", "APPLE_WATCH", false)]
    [InlineData("MAC_APP_DEVELOPMENT", "MAC_OS", "MAC", true)]
    [InlineData("MAC_APP_DEVELOPMENT", "IOS", "APPLE_TV", false)]
    public void IsDeviceCompatible_KeepsAppleTvsToTvosProfiles(
        string profileType,
        string devicePlatform,
        string deviceClass,
        bool expected) =>
        AppleProfileTypes.IsDeviceCompatible(profileType, devicePlatform, deviceClass).Should().Be(expected);
}
