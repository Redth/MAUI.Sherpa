using AppleAppStoreConnect;
using FluentAssertions;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class AppleConnectServiceTests
{
    [Theory]
    [InlineData("DEVELOPMENT", CertificateType.DEVELOPMENT)]
    [InlineData("DISTRIBUTION", CertificateType.DISTRIBUTION)]
    [InlineData("IOS_DEVELOPMENT", CertificateType.IOS_DEVELOPMENT)]
    [InlineData("IOS_DISTRIBUTION", CertificateType.IOS_DISTRIBUTION)]
    [InlineData("MAC_APP_DEVELOPMENT", CertificateType.MAC_APP_DEVELOPMENT)]
    [InlineData("MAC_APP_DISTRIBUTION", CertificateType.MAC_APP_DISTRIBUTION)]
    [InlineData("MAC_INSTALLER_DISTRIBUTION", CertificateType.MAC_INSTALLER_DISTRIBUTION)]
    [InlineData("DEVELOPER_ID_APPLICATION", CertificateType.DEVELOPER_ID_APPLICATION)]
    [InlineData("DEVELOPER_ID_KEXT", CertificateType.DEVELOPER_ID_KEXT)]
    public void ParseCertificateType_ReturnsMatchingAppStoreConnectType(
        string value,
        CertificateType expected)
    {
        AppleConnectService.ParseCertificateType(value).Should().Be(expected);
    }
}
