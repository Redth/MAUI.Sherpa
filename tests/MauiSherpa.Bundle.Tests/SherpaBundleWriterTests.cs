using System.Text.Json;
using FluentAssertions;
using MauiSherpa.Bundle.Loading;
using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Tests;

public class SherpaBundleWriterTests
{
    static SherpaBundle SampleBundle() => new()
    {
        Environments = new Dictionary<string, EnvironmentBlock>(StringComparer.OrdinalIgnoreCase)
        {
            ["Production"] = new EnvironmentBlock
            {
                ReplaceTokens = new() { ["SentryDsn"] = "https://prod" },
                IOS = new ApplePlatform
                {
                    Setup = new AppleSetup
                    {
                        Certificates = new() { new CertificateRef { Content = "p12-b64", Password = "pw" } },
                        Profiles = new() { new ProfileRef { Content = "profile-b64" } },
                    },
                    Deploy = new()
                    {
                        new DeployTarget
                        {
                            Provider = "TestFlight",
                            Fields = new()
                            {
                                ["ApiKey"] = JsonSerializer.SerializeToElement("p8-b64"),
                                ["KeyId"] = JsonSerializer.SerializeToElement("ABC123"),
                            },
                        },
                    },
                },
                Android = new AndroidPlatform
                {
                    Setup = new AndroidSetup
                    {
                        Keystores = new() { new Keystore { Content = "ks-b64", KeyAlias = "upload", KeyPassword = "kp" } },
                    },
                },
            },
        },
    };

    [Fact]
    public void Write_EmitsSchemaAndEnvironmentKey()
    {
        var json = SherpaBundleWriter.Write(SampleBundle());

        json.Should().Contain("\"$schema\"");
        json.Should().Contain(SherpaBundleWriter.SchemaUrl);
        json.Should().Contain("\"Production\"");
    }

    [Fact]
    public void Write_RoundTripsThroughDeserializer()
    {
        var json = SherpaBundleWriter.Write(SampleBundle());

        var parsed = SherpaBundleSerializer.Deserialize(json);

        parsed.TryGetEnvironment("production", out _, out var env).Should().BeTrue();
        env.ReplaceTokens!["SentryDsn"].Should().Be("https://prod");

        env.IOS!.Setup!.Certificates!.Single().Content.Should().Be("p12-b64");
        env.IOS.Setup.Profiles!.Single().Content.Should().Be("profile-b64");
        env.IOS.Deploy!.Single().Provider.Should().Be("TestFlight");
        env.IOS.Deploy!.Single().GetString("ApiKey").Should().Be("p8-b64");

        env.Android!.Setup!.Keystores!.Single().KeyAlias.Should().Be("upload");
    }

    [Fact]
    public void Write_OmitsNullMaps()
    {
        var bundle = new SherpaBundle
        {
            Environments = new Dictionary<string, EnvironmentBlock>(StringComparer.OrdinalIgnoreCase)
            {
                ["Staging"] = new EnvironmentBlock(),
            },
        };

        var json = SherpaBundleWriter.Write(bundle);

        json.Should().NotContain("\"Variables\"");
        json.Should().NotContain("\"ReplaceTokens\"");
        json.Should().NotContain("\"Android\"");
    }
}
