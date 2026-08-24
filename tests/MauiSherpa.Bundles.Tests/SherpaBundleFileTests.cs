using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using FluentAssertions;

namespace MauiSherpa.Bundles.Tests;

public class SherpaBundleFileTests
{
    [Fact]
    public void EncryptDecrypt_RoundTripsBundle()
    {
        var bundle = CreateBundle();

        var encrypted = SherpaBundleFile.Encrypt(bundle, "correct horse battery staple");
        var result = SherpaBundleFile.Decrypt(encrypted, "correct horse battery staple");

        System.Text.Encoding.UTF8.GetString(encrypted).Should().NotContain("super-secret");
        result.Name.Should().Be(bundle.Name);
        result.Environments["production"].Variables["ApiKey"].Should().Be("super-secret");
    }

    [Fact]
    public void Decrypt_WithWrongPassword_Throws()
    {
        var encrypted = SherpaBundleFile.Encrypt(CreateBundle(), "right-password");

        var act = () => SherpaBundleFile.Decrypt(encrypted, "wrong-password");

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WhenTampered_Throws()
    {
        var encrypted = SherpaBundleFile.EncryptV1ForCompatibilityTests(CreateBundle(), "password");
        encrypted[^1] ^= 0x01;

        var act = () => SherpaBundleFile.Decrypt(encrypted, "password");

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithHostileIterationCount_RejectsBeforeKeyDerivation()
    {
        var encrypted = SherpaBundleFile.EncryptV1ForCompatibilityTests(CreateBundle(), "password");
        const int magicLength = 8;
        var headerLength = BinaryPrimitives.ReadInt32LittleEndian(
            encrypted.AsSpan(magicLength, sizeof(int)));
        var headerStart = magicLength + sizeof(int);
        var header = Encoding.UTF8.GetString(encrypted, headerStart, headerLength);
        var hostileHeader = Encoding.UTF8.GetBytes(
            header.Replace("\"iterations\":600000", "\"iterations\":9999999", StringComparison.Ordinal));
        var hostile = new byte[
            magicLength + sizeof(int) + hostileHeader.Length +
            encrypted.Length - headerStart - headerLength];
        encrypted.AsSpan(0, magicLength).CopyTo(hostile);
        BinaryPrimitives.WriteInt32LittleEndian(
            hostile.AsSpan(magicLength, sizeof(int)),
            hostileHeader.Length);
        hostileHeader.CopyTo(hostile.AsSpan(headerStart));
        encrypted.AsSpan(headerStart + headerLength)
            .CopyTo(hostile.AsSpan(headerStart + hostileHeader.Length));

        var act = () => SherpaBundleFile.Decrypt(hostile, "password");

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*Unsupported encrypted file envelope*");
    }

    [Fact]
    public void Decrypt_ReadsVersionOnePack()
    {
        var encrypted = SherpaBundleFile.EncryptV1ForCompatibilityTests(CreateBundle(), "password");

        var result = SherpaBundleFile.Decrypt(encrypted, "password");

        result.Name.Should().Be("Sample");
    }

    [Fact]
    public void Encrypt_CompressesLargeRepeatedPayload()
    {
        var bundle = CreateBundle() with
        {
            Variables = new Dictionary<string, string>
            {
                ["Repeated"] = new string('x', 100_000)
            }
        };

        var encrypted = SherpaBundleFile.Encrypt(bundle, "password");

        encrypted.Length.Should().BeLessThan(2_000);
        SherpaBundleFile.Decrypt(encrypted, "password")
            .Variables["Repeated"].Should().HaveLength(100_000);
    }

    [Fact]
    public void Encrypt_VersionTwoRemovesNestedBase64Overhead()
    {
        var randomAsset = RandomNumberGenerator.GetBytes(30_000);
        var bundle = CreateBundle() with
        {
            Assets = new Dictionary<string, BundleEmbeddedAsset>
            {
                ["Certificate"] = new()
                {
                    Kind = BundleAssetKind.AppleCertificate,
                    FileName = "certificate.p12",
                    ContentBase64 = Convert.ToBase64String(randomAsset)
                }
            }
        };

        var versionOne = SherpaBundleFile.EncryptV1ForCompatibilityTests(bundle, "password");
        var versionTwo = SherpaBundleFile.Encrypt(bundle, "password");

        versionTwo.Length.Should().BeLessThan((int)(versionOne.Length * 0.82));
        SherpaBundleFile.Decrypt(versionTwo, "password")
            .Assets["Certificate"].ContentBase64.Should().Be(Convert.ToBase64String(randomAsset));
    }

    [Fact]
    public void Validate_ReportsUnsafeAndMissingAssets()
    {
        var bundle = CreateBundle() with
        {
            Environments = new Dictionary<string, SherpaBundleEnvironment>
            {
                ["production"] = new()
                {
                    Platforms = new Dictionary<BundlePlatform, BundlePlatformConfiguration>
                    {
                        [BundlePlatform.Android] = new()
                        {
                            Install = new BundleInstallConfiguration { AssetIds = ["missing"] },
                            Build = new BundleBuildConfiguration
                            {
                                Replacements = [new BundleReplacement { Path = "../secret.txt" }]
                            }
                        }
                    }
                }
            }
        };

        var errors = BundleValidator.Validate(bundle);

        errors.Should().Contain(error => error.Contains("missing asset"));
        errors.Should().Contain(error => error.Contains("safe relative path"));
    }

    private static SherpaBundle CreateBundle() => new()
    {
        Name = "Sample",
        Variables = new Dictionary<string, string> { ["BuildNumber"] = "42" },
        SecretVariables = new HashSet<string> { "ApiKey" },
        Environments = new Dictionary<string, SherpaBundleEnvironment>
        {
            ["production"] = new()
            {
                Variables = new Dictionary<string, string> { ["ApiKey"] = "super-secret" },
                Platforms = new Dictionary<BundlePlatform, BundlePlatformConfiguration>
                {
                    [BundlePlatform.Android] = new()
                    {
                        Build = new BundleBuildConfiguration { Project = "App.csproj" },
                        Deploy =
                        [
                            new BundleDeploymentTarget
                            {
                                Provider = BundleDeploymentProvider.GooglePlay
                            }
                        ]
                    }
                }
            }
        }
    };
}
