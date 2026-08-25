using FluentAssertions;

namespace MauiSherpa.Bundles.Tests.Toolchain;

public sealed class BundleAssetMaterializerTests
{
    [Fact]
    public async Task MaterializeAsync_RejectsUnsafeFileName()
    {
        using var workspace = new ToolchainTestWorkspace();
        var materializer = new BundleAssetMaterializer();
        var bundle = new SherpaBundle
        {
            Name = "Unsafe asset",
            Assets = new Dictionary<string, BundleEmbeddedAsset>(StringComparer.OrdinalIgnoreCase)
            {
                ["keystore"] = new()
                {
                    Kind = BundleAssetKind.AndroidKeystore,
                    FileName = "../release.keystore",
                    ContentBase64 = Convert.ToBase64String([1, 2, 3])
                }
            }
        };
        var configuration = new BundlePlatformConfiguration
        {
            Install = new BundleInstallConfiguration
            {
                AssetIds = ["keystore"]
            }
        };

        var act = () => materializer.MaterializeAsync(bundle, configuration, workspace.RootPath);

        await act.Should().ThrowAsync<BundleValidationException>()
            .WithMessage("*unsafe file name*");
    }

    [Fact]
    public async Task MaterializeAsync_RejectsDuplicateOutputVariablesFromDifferentAssets()
    {
        using var workspace = new ToolchainTestWorkspace();
        var materializer = new BundleAssetMaterializer();
        var bundle = new SherpaBundle
        {
            Name = "Duplicate output variable",
            Assets = new Dictionary<string, BundleEmbeddedAsset>(StringComparer.OrdinalIgnoreCase)
            {
                ["profile"] = new()
                {
                    Kind = BundleAssetKind.AppleProvisioningProfile,
                    FileName = "profile.mobileprovision",
                    ContentBase64 = Convert.ToBase64String([1]),
                    OutputVariable = "SigningAssetPath"
                },
                ["certificate"] = new()
                {
                    Kind = BundleAssetKind.AppleCertificate,
                    FileName = "certificate.p12",
                    ContentBase64 = Convert.ToBase64String([2]),
                    OutputVariable = "SigningAssetPath"
                }
            }
        };
        var configuration = new BundlePlatformConfiguration
        {
            Install = new BundleInstallConfiguration
            {
                AssetIds = ["profile", "certificate"]
            }
        };

        var act = () => materializer.MaterializeAsync(bundle, configuration, workspace.RootPath);

        await act.Should().ThrowAsync<BundleValidationException>()
            .WithMessage("*SigningAssetPath*");
    }

    [Fact]
    public async Task MaterializeAsync_WritesUserOnlyPermissionsOnUnix()
    {
        using var workspace = new ToolchainTestWorkspace();
        var materializer = new BundleAssetMaterializer();
        var bundle = new SherpaBundle
        {
            Name = "Permissions",
            Assets = new Dictionary<string, BundleEmbeddedAsset>(StringComparer.OrdinalIgnoreCase)
            {
                ["keystore"] = new()
                {
                    Kind = BundleAssetKind.AndroidKeystore,
                    FileName = "release.keystore",
                    ContentBase64 = Convert.ToBase64String([1, 2, 3]),
                    OutputVariable = "KeystorePath"
                }
            }
        };
        var configuration = new BundlePlatformConfiguration
        {
            Install = new BundleInstallConfiguration
            {
                AssetIds = ["keystore"]
            }
        };

        var result = await materializer.MaterializeAsync(bundle, configuration, workspace.RootPath);

        var assetPath = result["KeystorePath"];
        File.ReadAllBytes(assetPath).Should().Equal([1, 2, 3]);
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(assetPath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.GetUnixFileMode(Path.GetDirectoryName(assetPath)!).Should().Be(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
