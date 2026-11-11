using AppleAppStoreConnect;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class AppleConnectServiceTests
{
    [Fact]
    public async Task GetProfilesForIdentityAsync_MissingIdentityDoesNotUseSelectedAccount()
    {
        var selected = new AppleIdentity("identity-b", "Account B", "key", "issuer", null, null);
        var state = new Mock<IAppleIdentityStateService>();
        state.SetupGet(service => service.SelectedIdentity).Returns(selected);
        var identities = new Mock<IAppleIdentityService>();
        identities.Setup(service => service.GetIdentityAsync("identity-a"))
            .ReturnsAsync((AppleIdentity?)null);
        var storage = new Mock<ISecureStorageService>();
        var service = new AppleConnectService(
            state.Object,
            identities.Object,
            storage.Object,
            Mock.Of<IEncryptedSettingsService>(),
            Mock.Of<ILoggingService>());

        var action = () => service.GetProfilesForIdentityAsync("identity-a");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*identity-a*no longer available*");
        identities.Verify(instance => instance.GetIdentityAsync("identity-a"), Times.Once);
        identities.Verify(instance => instance.GetIdentityAsync("identity-b"), Times.Never);
        storage.VerifyNoOtherCalls();
        state.Object.SelectedIdentity.Should().Be(selected);
    }

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

    static AppleDevice TestDevice(string id, string udid) =>
        new(id, udid, $"Device {id}", "IOS", "IPHONE", "ENABLED", null);

    [Fact]
    public void ResolveProfileDevices_UsesCompleteRelationshipData()
    {
        var included = new Dictionary<string, AppleDevice>
        {
            ["d1"] = TestDevice("d1", "U1"),
            ["d2"] = TestDevice("d2", "U2"),
        };

        var result = AppleConnectService.ResolveProfileDevices(
            "Dev", ["d1", "d2"], 2, included, provisionedUdids: null, allDevices: null);

        result.Select(device => device.Id).Should().Equal("d1", "d2");
        result[0].Udid.Should().Be("U1");
    }

    [Fact]
    public void ResolveProfileDevices_KeepsDevicesMissingFromIncludedData()
    {
        var result = AppleConnectService.ResolveProfileDevices(
            "Dev", ["d1"], null, new Dictionary<string, AppleDevice>(), null, null);

        result.Should().ContainSingle().Which.Id.Should().Be("d1");
    }

    [Fact]
    public void ResolveProfileDevices_MapsProvisionedUdidsWhenTheRelationshipIsTruncated()
    {
        var allDevices = Enumerable.Range(1, 60)
            .Select(index => TestDevice($"d{index}", $"UDID-{index}"))
            .ToList();
        var udids = allDevices.Take(55).Select(device => device.Udid.ToLowerInvariant()).ToList();

        var result = AppleConnectService.ResolveProfileDevices(
            "Dev",
            allDevices.Take(50).Select(device => device.Id).ToList(),
            55,
            new Dictionary<string, AppleDevice>(),
            udids,
            allDevices);

        result.Should().HaveCount(55);
        result.Select(device => device.Id).Should().Equal(allDevices.Take(55).Select(device => device.Id));
    }

    [Fact]
    public void ResolveProfileDevices_RefusesWhenATruncatedDeviceCannotBeResolved()
    {
        var allDevices = new[] { TestDevice("d1", "U1") };

        var act = () => AppleConnectService.ResolveProfileDevices(
            "Dev", ["d1"], 2, new Dictionary<string, AppleDevice>(), ["U1", "U-UNKNOWN"], allDevices);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not registered*");
    }

    [Fact]
    public void ResolveProfileDevices_RefusesWhenTruncatedAndTheProfileContentIsUnavailable()
    {
        var act = () => AppleConnectService.ResolveProfileDevices(
            "Dev", ["d1"], 2, new Dictionary<string, AppleDevice>(), null, null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResolveProfileDevices_RefusesWhenTheProfileContentListsFewerDevicesThanReported()
    {
        var allDevices = new[] { TestDevice("d1", "U1"), TestDevice("d2", "U2") };

        var act = () => AppleConnectService.ResolveProfileDevices(
            "Dev", ["d1"], 3, new Dictionary<string, AppleDevice>(), ["U1", "U2"], allDevices);

        act.Should().Throw<InvalidOperationException>().WithMessage("*reports 3*");
    }

    [Fact]
    public void EnsureRelationshipComplete_RefusesTruncatedCertificateLists()
    {
        var act = () => AppleConnectService.EnsureRelationshipComplete("certificates", 50, 51, "Dev");

        act.Should().Throw<InvalidOperationException>().WithMessage("*50 of 51 certificates*");
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(null)]
    public void EnsureRelationshipComplete_AllowsCompleteLists(uint? total)
    {
        var act = () => AppleConnectService.EnsureRelationshipComplete("certificates", 2, total, "Dev");

        act.Should().NotThrow();
    }
}
