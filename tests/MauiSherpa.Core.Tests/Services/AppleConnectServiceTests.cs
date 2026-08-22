using AppleAppStoreConnect;
using FluentAssertions;
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
}
