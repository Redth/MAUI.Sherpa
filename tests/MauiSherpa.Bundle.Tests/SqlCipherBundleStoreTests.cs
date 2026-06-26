using FluentAssertions;
using MauiSherpa.Bundle.Loading;
using MauiSherpa.Bundle.Models;

namespace MauiSherpa.Bundle.Tests;

public class SqlCipherBundleStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"sherpa-{Guid.NewGuid():N}.sherpabundle");

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { /* best effort */ }
    }

    private static SherpaBundle SampleBundle() => new()
    {
        Build = new CommonConfig
        {
            Variables = new Dictionary<string, string> { ["buildNumber"] = "1" },
        },
        Environments = new Dictionary<string, EnvironmentBlock>(StringComparer.OrdinalIgnoreCase)
        {
            ["Production"] = new EnvironmentBlock
            {
                ReplaceTokens = new Dictionary<string, string> { ["SentryDsn"] = "https://example" },
                Android = new AndroidPlatform
                {
                    Setup = new AndroidSetup
                    {
                        Keystores = new List<Keystore>
                        {
                            new() { Content = "QkFTRTY0", KeyAlias = "upload", StorePassword = "pw" },
                        },
                    },
                    Deploy = new List<DeployTarget> { new() { Provider = "PlayStore" } },
                },
                IOS = new ApplePlatform
                {
                    Setup = new AppleSetup
                    {
                        Certificates = new List<CertificateRef> { new() { Content = "UDEy", Password = "p12pw" } },
                    },
                },
            },
            ["Staging"] = new EnvironmentBlock
            {
                Windows = new WindowsPlatform(),
            },
        },
    };

    [Fact]
    public async Task RoundTrips_AllEnvironmentsAndBuildBlock()
    {
        var original = SampleBundle();

        await SqlCipherBundleStore.SaveAsync(original, _path, "hunter2");
        var loaded = await SqlCipherBundleStore.LoadAsync(_path, "hunter2");

        loaded.Build!.Variables!["buildNumber"].Should().Be("1");
        loaded.Environments.Keys.Should().BeEquivalentTo(new[] { "Production", "Staging" });

        loaded.TryGetEnvironment("production", out _, out var prod).Should().BeTrue();
        prod.ReplaceTokens!["SentryDsn"].Should().Be("https://example");
        prod.Android!.Setup!.Keystores!.Single().KeyAlias.Should().Be("upload");
        prod.Android!.Deploy!.Single().Provider.Should().Be("PlayStore");
        prod.IOS!.Setup!.Certificates!.Single().Password.Should().Be("p12pw");
    }

    [Fact]
    public async Task WrongPassword_Throws()
    {
        await SqlCipherBundleStore.SaveAsync(SampleBundle(), _path, "correct-password");

        var act = () => SqlCipherBundleStore.LoadAsync(_path, "wrong-password");

        await act.Should().ThrowAsync<SherpaBundleException>();
    }

    [Fact]
    public async Task Save_ReplacesExistingFile_NoOrphanEnvironments()
    {
        await SqlCipherBundleStore.SaveAsync(SampleBundle(), _path, "pw");

        var slim = new SherpaBundle
        {
            Environments = new Dictionary<string, EnvironmentBlock>(StringComparer.OrdinalIgnoreCase)
            {
                ["Production"] = new EnvironmentBlock(),
            },
        };
        await SqlCipherBundleStore.SaveAsync(slim, _path, "pw");

        var loaded = await SqlCipherBundleStore.LoadAsync(_path, "pw");
        loaded.Environments.Keys.Should().BeEquivalentTo(new[] { "Production" });
        loaded.Build.Should().BeNull();
    }

    [Fact]
    public async Task Loader_RecognizesEncryptedBundle_AndDecryptsWithPassword()
    {
        await SqlCipherBundleStore.SaveAsync(SampleBundle(), _path, "pw");

        var json = new JsonBundleLoader();
        var cipher = new SqlCipherBundleLoader();

        json.CanLoad(_path).Should().BeFalse();
        cipher.CanLoad(_path).Should().BeTrue();

        var loaded = await cipher.LoadAsync(_path, "pw");
        loaded.Environments.Should().ContainKey("Production");
    }

    [Fact]
    public async Task Loader_Encrypted_FallsBackToEnvironmentVariable()
    {
        await SqlCipherBundleStore.SaveAsync(SampleBundle(), _path, "env-pw");
        Environment.SetEnvironmentVariable(SqlCipherBundleLoader.PasswordEnvironmentVariable, "env-pw");
        try
        {
            var loaded = await new SqlCipherBundleLoader().LoadAsync(_path, password: null);
            loaded.Environments.Should().ContainKey("Production");
        }
        finally
        {
            Environment.SetEnvironmentVariable(SqlCipherBundleLoader.PasswordEnvironmentVariable, null);
        }
    }
}
