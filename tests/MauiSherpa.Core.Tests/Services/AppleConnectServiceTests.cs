using AppleAppStoreConnect;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class AppleConnectServiceTests
{
    [Fact]
    public void ParseCertificateType_SupportsEveryKnownAppStoreConnectType()
    {
        var knownTypes = Enum.GetValues<CertificateType>()
            .Where(type => type != CertificateType.Unknown);

        foreach (var type in knownTypes)
        {
            AppleConnectService.ParseCertificateType(type.ToString()).Should().Be(type);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    [InlineData("NOT_A_CERTIFICATE_TYPE")]
    public void ParseCertificateType_RejectsUnsupportedValues(string value)
    {
        var action = () => AppleConnectService.ParseCertificateType(value);

        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Custom Key", "Identity Name", "fallback", "Custom Key")]
    [InlineData(" ", "Identity Name", "fallback", "Identity Name")]
    [InlineData(null, null, "fallback", "fallback")]
    public void ResolveCertificateCommonName_UsesFirstAvailableName(
        string? commonName,
        string? identityName,
        string fallbackName,
        string expected)
    {
        AppleConnectService.ResolveCertificateCommonName(
            commonName,
            identityName,
            fallbackName).Should().Be(expected);
    }

    [Theory]
    [InlineData("Team Signing Key", "Apple Development: Created via API", "Created via API", "Team Signing Key")]
    [InlineData(null, "Apple Development: Created via API", "Created via API", "Apple Development: Created via API")]
    [InlineData(null, null, "Created via API", "Created via API")]
    public void ResolveCertificateDisplayName_PrefersLocalAlias(
        string? alias,
        string? name,
        string? displayName,
        string expected)
    {
        AppleConnectService.ResolveCertificateDisplayName(alias, name, displayName)
            .Should().Be(expected);
    }

    [Fact]
    public void EnsureAppGroupsPreserved_RejectsMissingGroupAssignment()
    {
        var entitlements = new Dictionary<string, object>
        {
            ["com.apple.security.application-groups"] = new object[]
            {
                "group.com.example.shared"
            }
        };
        var regeneratedEntitlements = new Dictionary<string, object>();

        var action = () => AppleConnectService.EnsureAppGroupsPreserved(
            entitlements,
            regeneratedEntitlements);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*missing App Group assignments*group.com.example.shared*existing profile was not deleted*");
    }

    [Fact]
    public void EnsureAppGroupsPreserved_AllowsSelectedGroupAssignment()
    {
        var entitlements = new Dictionary<string, object>
        {
            ["com.apple.security.application-groups"] = new object[]
            {
                "group.com.example.shared"
            }
        };
        var regeneratedEntitlements = new Dictionary<string, object>
        {
            ["com.apple.security.application-groups"] = new object[]
            {
                "group.com.example.shared"
            }
        };

        var action = () => AppleConnectService.EnsureAppGroupsPreserved(
            entitlements,
            regeneratedEntitlements);

        action.Should().NotThrow();
    }

    [Fact]
    public void EnsureAppGroupsPreserved_RejectsDifferentSelectedGroup()
    {
        var entitlements = new Dictionary<string, object>
        {
            ["com.apple.security.application-groups"] = new object[]
            {
                "group.com.example.required"
            }
        };
        var regeneratedEntitlements = new Dictionary<string, object>
        {
            ["com.apple.security.application-groups"] = new object[]
            {
                "group.com.example.other"
            }
        };

        var action = () => AppleConnectService.EnsureAppGroupsPreserved(
            entitlements,
            regeneratedEntitlements);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*group.com.example.required*");
    }

    [Theory]
    [InlineData(ProvisioningProfilesDirectoryOptions.Auto, "/auto")]
    [InlineData(ProvisioningProfilesDirectoryOptions.Xcode16AndLater, "/Users/test/Library/Developer/Xcode/UserData/Provisioning Profiles")]
    [InlineData(ProvisioningProfilesDirectoryOptions.Xcode15AndEarlier, "/Users/test/Library/MobileDevice/Provisioning Profiles")]
    public void ResolveProvisioningProfilesDirectories_UsesConfiguredMode(
        string preference,
        string expected)
    {
        var result = AppleConnectService.ResolveProvisioningProfilesDirectories(
            preference,
            "/auto",
            "/Users/test",
            isApplePlatform: true);

        result.Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public void ResolveProvisioningProfilesDirectories_BothReturnsNewAndLegacyFolders()
    {
        var result = AppleConnectService.ResolveProvisioningProfilesDirectories(
            ProvisioningProfilesDirectoryOptions.Both,
            "/auto",
            "/Users/test",
            isApplePlatform: true);

        result.Should().Equal(
            "/Users/test/Library/Developer/Xcode/UserData/Provisioning Profiles",
            "/Users/test/Library/MobileDevice/Provisioning Profiles");
    }
}
