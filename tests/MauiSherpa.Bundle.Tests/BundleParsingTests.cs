using FluentAssertions;
using MauiSherpa.Bundle.Loading;

namespace MauiSherpa.Bundle.Tests;

public class BundleParsingTests
{
    private const string SampleBundle = """
    {
      "Build": { "ReplaceTokens": { "SentryDsn": "https://default" } },
      "Production": {
        "Variables": { "ApiBaseUrl": "https://api.example.com" },
        "ReplaceTokens": { "Hello": "World", "SentryDsn": "https://prod" },
        "Android": {
          "Setup": { "Keystores": [ { "Content": "QUJD", "KeyAlias": "key0", "KeyPassword": "pw" } ] },
          "Build": { "MSBuildProperties": { "ApplicationId": "org.acme.app" }, "ReplaceTokens": { "Hello": "Android" } },
          "Deploy": [ { "Provider": "PlayStore", "Track": "internal", "ServiceAccountKey": "e30=" } ]
        },
        "iOS": {
          "Setup": { "Profiles": [ { "Content": "UEZG" } ], "Certificates": [ { "Content": "UDEy", "Password": "p" } ] },
          "Deploy": [ { "Provider": "TestFlight", "ApiKey": "a", "IssuerId": "i", "KeyId": "k" } ]
        },
        "Windows": { "Certificate": "UEZY", "CertificatePassword": "pw", "Variables": { "Hello": "Windows" } }
      },
      "Development": { }
    }
    """;

    [Fact]
    public void Parses_build_defaults_and_environments()
    {
        var bundle = SherpaBundleSerializer.Deserialize(SampleBundle);

        bundle.Build!.ReplaceTokens!["SentryDsn"].Should().Be("https://default");
        bundle.Environments.Keys.Should().Contain(new[] { "Production", "Development" });
    }

    [Fact]
    public void Environment_lookup_is_case_insensitive()
    {
        var bundle = SherpaBundleSerializer.Deserialize(SampleBundle);

        bundle.TryGetEnvironment("PRODUCTION", out var name, out var env).Should().BeTrue();
        name.Should().Be("Production");
        env.ReplaceTokens!["Hello"].Should().Be("World");
    }

    [Fact]
    public void Parses_android_platform_block()
    {
        var bundle = SherpaBundleSerializer.Deserialize(SampleBundle);
        bundle.TryGetEnvironment("Production", out _, out var env);

        env.Android!.Setup!.Keystores!.Should().ContainSingle();
        env.Android.Setup.Keystores![0].KeyAlias.Should().Be("key0");
        env.Android.Build!.MSBuildProperties!["ApplicationId"].Should().Be("org.acme.app");
        env.Android.Deploy!.Single().Provider.Should().Be("PlayStore");
        env.Android.Deploy!.Single().GetString("Track").Should().Be("internal");
    }

    [Fact]
    public void Parses_ios_block_with_case_sensitive_json_key()
    {
        var bundle = SherpaBundleSerializer.Deserialize(SampleBundle);
        bundle.TryGetEnvironment("Production", out _, out var env);

        env.IOS!.Setup!.Profiles!.Should().ContainSingle();
        env.IOS.Setup.Certificates!.Single().Password.Should().Be("p");
        env.IOS.Deploy!.Single().Provider.Should().Be("TestFlight");
    }

    [Fact]
    public void Parses_flat_windows_block()
    {
        var bundle = SherpaBundleSerializer.Deserialize(SampleBundle);
        bundle.TryGetEnvironment("Production", out _, out var env);

        env.Windows!.Certificate.Should().Be("UEZY");
        env.Windows.Variables!["Hello"].Should().Be("Windows");
    }

    [Fact]
    public void Tolerates_comments_trailing_commas_and_schema_key()
    {
        const string json = """
        {
          "$schema": "https://schemas.sherpa.dev/sherpabundle/v1.json",
          // a comment
          "Production": { "ReplaceTokens": { "A": "B", }, },
        }
        """;
        var bundle = SherpaBundleSerializer.Deserialize(json);

        bundle.Environments.Should().ContainKey("Production");
        bundle.Environments.Should().NotContainKey("$schema");
    }

    [Fact]
    public void Invalid_json_throws_bundle_exception()
    {
        var act = () => SherpaBundleSerializer.Deserialize("not json");
        act.Should().Throw<SherpaBundleException>();
    }
}
