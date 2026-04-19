using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class XcodeServiceTests
{
    [Fact]
    public void FindBundledUnxipExecutable_WithRuntimeSpecificBundle_ReturnsBundledPath()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var bundledPath = CreateBundledExecutable(tempDir, "runtimes", "osx-arm64", "native", "unxip");

            var resolved = XcodeService.FindBundledUnxipExecutable(tempDir, "osx-arm64");

            resolved.Should().Be(bundledPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void FindBundledUnxipExecutable_WithMacCatalystRuntime_FallsBackToBundledOsxPath()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var bundledPath = CreateBundledExecutable(tempDir, "runtimes", "osx-arm64", "native", "unxip");

            var resolved = XcodeService.FindBundledUnxipExecutable(tempDir, "maccatalyst-arm64");

            resolved.Should().Be(bundledPath);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void CreateArchiveExtractionCommand_WithSystemPreference_UsesSystemXip()
    {
        var command = XcodeService.CreateArchiveExtractionCommand(
            "/tmp/Xcode_16.0.xip",
            XcodeArchiveExtractorOptions.SystemXip,
            null);

        command.FileName.Should().Be("xip");
        command.Arguments.Should().Be("--expand \"/tmp/Xcode_16.0.xip\"");
        command.Preference.Should().Be(XcodeArchiveExtractorOptions.SystemXip);
        command.FellBackToSystemXip.Should().BeFalse();
    }

    [Fact]
    public void CreateArchiveExtractionCommand_WithUnxipPreferenceAndExecutable_UsesUnxip()
    {
        var command = XcodeService.CreateArchiveExtractionCommand(
            "/tmp/Xcode_16.0.xip",
            XcodeArchiveExtractorOptions.Unxip,
            "/opt/homebrew/bin/unxip");

        command.FileName.Should().Be("/opt/homebrew/bin/unxip");
        command.Arguments.Should().Be("\"/tmp/Xcode_16.0.xip\"");
        command.Preference.Should().Be(XcodeArchiveExtractorOptions.Unxip);
        command.FellBackToSystemXip.Should().BeFalse();
    }

    [Fact]
    public void CreateArchiveExtractionCommand_WithUnxipPreferenceButNoExecutable_FallsBackToSystemXip()
    {
        var command = XcodeService.CreateArchiveExtractionCommand(
            "/tmp/Xcode_16.0.xip",
            XcodeArchiveExtractorOptions.Unxip,
            null);

        command.FileName.Should().Be("xip");
        command.Arguments.Should().Be("--expand \"/tmp/Xcode_16.0.xip\"");
        command.Preference.Should().Be(XcodeArchiveExtractorOptions.SystemXip);
        command.FellBackToSystemXip.Should().BeTrue();
    }

    [Fact]
    public void GetManagedXcodeBundleName_Underscore_UsesVersionOnly()
    {
        var bundleName = XcodeService.GetManagedXcodeBundleName("26.3", XcodeBundleSeparatorOptions.Underscore);

        bundleName.Should().Be("Xcode_26.3.app");
    }

    [Fact]
    public void GetManagedXcodeBundleName_Hyphen_UsesVersionOnly()
    {
        var bundleName = XcodeService.GetManagedXcodeBundleName("26.3", XcodeBundleSeparatorOptions.Hyphen);

        bundleName.Should().Be("Xcode-26.3.app");
    }

    [Theory]
    [InlineData("_", "Xcode_26.3_Beta_2.app")]
    [InlineData("-", "Xcode-26.3-Beta-2.app")]
    public void GetManagedXcodeBundleName_SanitizesBetaVersions(string separator, string expected)
    {
        var bundleName = XcodeService.GetManagedXcodeBundleName("26.3 Beta 2", separator);

        bundleName.Should().Be(expected);
    }

    [Fact]
    public void GetManagedXcodeBundleNameWithBuild_AppendsBuildNumber()
    {
        var bundleName = XcodeService.GetManagedXcodeBundleNameWithBuild("26.3", "17A123", XcodeBundleSeparatorOptions.Underscore);

        bundleName.Should().Be("Xcode_26.3_17A123.app");
    }

    [Theory]
    [InlineData("_", "/Applications/Xcode_26.3.app")]
    [InlineData("-", "/Applications/Xcode-26.3.app")]
    public void ResolveManagedXcodeBundlePath_WhenNoCollision_UsesPlainVersionName(string separator, string expected)
    {
        var bundlePath = XcodeService.ResolveManagedXcodeBundlePath(
            "/Applications",
            "26.3",
            "17A123",
            [],
            separator);

        bundlePath.Should().Be(expected);
    }

    [Theory]
    [InlineData("_", "/Applications/Xcode_26.3.app", "/Applications/Xcode_26.3_17A123.app")]
    [InlineData("-", "/Applications/Xcode-26.3.app", "/Applications/Xcode-26.3-17A123.app")]
    public void ResolveManagedXcodeBundlePath_WhenPlainNameExists_AppendsBuildNumber(
        string separator, string existing, string expected)
    {
        var bundlePath = XcodeService.ResolveManagedXcodeBundlePath(
            "/Applications",
            "26.3",
            "17A123",
            [existing],
            separator);

        bundlePath.Should().Be(expected);
    }

    [Fact]
    public void ResolveManagedXcodeBundlePath_WhenBuildNumberedNameAlsoExists_AppendsNumericSuffix()
    {
        var bundlePath = XcodeService.ResolveManagedXcodeBundlePath(
            "/Applications",
            "26.3",
            "17A123",
            [
                "/Applications/Xcode_26.3.app",
                "/Applications/Xcode_26.3_17A123.app"
            ],
            XcodeBundleSeparatorOptions.Underscore);

        bundlePath.Should().Be("/Applications/Xcode_26.3_17A123_2.app");
    }

    [Fact]
    public void CreateSelectionPlan_WhenCanonicalBundleIsReal_MigratesSelectedBundleToVersionedPath()
    {
        var managedDefaultState = new XcodeManagedDefaultState(
            CanonicalAppPath: "/Applications/Xcode.app",
            Exists: true,
            IsSymlink: false,
            LinkTargetPath: null,
            Version: "26.3",
            BuildNumber: "17A123");

        var plan = XcodeService.CreateSelectionPlan(
            "/Applications/Xcode.app",
            managedDefaultState,
            ["/Applications/Xcode.app"],
            XcodeBundleSeparatorOptions.Underscore);

        plan.SelectedAppPath.Should().Be("/Applications/Xcode_26.3.app");
        plan.MigrationSourcePath.Should().Be("/Applications/Xcode.app");
        plan.MigrationDestinationPath.Should().Be("/Applications/Xcode_26.3.app");
    }

    [Fact]
    public void CreateSelectionPlan_WhenCanonicalMigrationCollides_UsesUniqueDestinationPath()
    {
        var managedDefaultState = new XcodeManagedDefaultState(
            CanonicalAppPath: "/Applications/Xcode.app",
            Exists: true,
            IsSymlink: false,
            LinkTargetPath: null,
            Version: "26.3",
            BuildNumber: "17A123");

        var plan = XcodeService.CreateSelectionPlan(
            "/Applications/Xcode.app",
            managedDefaultState,
            [
                "/Applications/Xcode.app",
                "/Applications/Xcode_26.3.app"
            ],
            XcodeBundleSeparatorOptions.Underscore);

        plan.SelectedAppPath.Should().Be("/Applications/Xcode_26.3_17A123.app");
        plan.MigrationDestinationPath.Should().Be("/Applications/Xcode_26.3_17A123.app");
    }

    [Fact]
    public void CreateSelectionPlan_WhenCanonicalPathIsSymlink_UsesLinkTargetWithoutMigration()
    {
        var managedDefaultState = new XcodeManagedDefaultState(
            CanonicalAppPath: "/Applications/Xcode.app",
            Exists: true,
            IsSymlink: true,
            LinkTargetPath: "/Applications/Xcode_26.3_17A123.app",
            Version: null,
            BuildNumber: null);

        var plan = XcodeService.CreateSelectionPlan(
            "/Applications/Xcode.app",
            managedDefaultState,
            [
                "/Applications/Xcode.app",
                "/Applications/Xcode_26.3_17A123.app"
            ],
            XcodeBundleSeparatorOptions.Underscore);

        plan.SelectedAppPath.Should().Be("/Applications/Xcode_26.3_17A123.app");
        plan.MigrationSourcePath.Should().BeNull();
        plan.MigrationDestinationPath.Should().BeNull();
    }

    [Fact]
    public void ComputeNormalizationPlan_WhenAllBundlesMatch_ReturnsEmptyPlan()
    {
        var plan = XcodeService.ComputeNormalizationPlan(
            [
                ("/Applications/Xcode_26.3.app", "26.3", "17A123"),
                ("/Applications/Xcode_26.2.app", "26.2", "17A500")
            ],
            XcodeBundleSeparatorOptions.Underscore,
            currentSymlinkTarget: null);

        plan.Renames.Should().BeEmpty();
        plan.SymlinkRetargetPath.Should().BeNull();
        plan.HasWork.Should().BeFalse();
    }

    [Fact]
    public void ComputeNormalizationPlan_SwitchUnderscoreToHyphen_RenamesBundles()
    {
        var plan = XcodeService.ComputeNormalizationPlan(
            [
                ("/Applications/Xcode_26.3_17A123.app", "26.3", "17A123"),
                ("/Applications/Xcode_26.2.app", "26.2", "17A500")
            ],
            XcodeBundleSeparatorOptions.Hyphen,
            currentSymlinkTarget: null);

        plan.Renames.Should().HaveCount(2);
        plan.Renames.Should().Contain(r =>
            r.FromPath == "/Applications/Xcode_26.3_17A123.app" &&
            r.ToPath == "/Applications/Xcode-26.3.app");
        plan.Renames.Should().Contain(r =>
            r.FromPath == "/Applications/Xcode_26.2.app" &&
            r.ToPath == "/Applications/Xcode-26.2.app");
    }

    [Fact]
    public void ComputeNormalizationPlan_WhenSymlinkTargetIsRenamed_SetsRetargetPath()
    {
        var plan = XcodeService.ComputeNormalizationPlan(
            [
                ("/Applications/Xcode_26.3_17A123.app", "26.3", "17A123")
            ],
            XcodeBundleSeparatorOptions.Underscore,
            currentSymlinkTarget: "/Applications/Xcode_26.3_17A123.app");

        plan.Renames.Should().ContainSingle()
            .Which.ToPath.Should().Be("/Applications/Xcode_26.3.app");
        plan.SymlinkRetargetPath.Should().Be("/Applications/Xcode_26.3.app");
    }

    [Fact]
    public void ComputeNormalizationPlan_SwappingSeparators_HandlesCollisionViaBuildNumber()
    {
        // Two installs of the same version exist, one in each naming style. After
        // switching to hyphen, both want the plain `Xcode-26.3.app` slot — one must
        // keep the build number suffix to disambiguate.
        var plan = XcodeService.ComputeNormalizationPlan(
            [
                ("/Applications/Xcode_26.3.app", "26.3", "17A123"),
                ("/Applications/Xcode_26.3_17A400.app", "26.3", "17A400")
            ],
            XcodeBundleSeparatorOptions.Hyphen,
            currentSymlinkTarget: null);

        plan.Renames.Should().HaveCount(2);
        plan.Renames.Select(r => r.ToPath).Should().BeEquivalentTo(
            new[] { "/Applications/Xcode-26.3.app", "/Applications/Xcode-26.3-17A400.app" });
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "MauiSherpa-XcodeServiceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static string CreateBundledExecutable(string baseDirectory, params string[] relativeSegments)
    {
        var path = Path.Combine(baseDirectory, Path.Combine(relativeSegments));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
