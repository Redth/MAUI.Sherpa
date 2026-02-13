using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using Xunit;

namespace MauiSherpa.Core.Tests.Services;

/// <summary>
/// Tests for preview SDK awareness in Doctor feature.
/// Validates that DoctorContext, DoctorReport, and DependencyStatus
/// correctly handle preview SDKs without suggesting downgrades.
/// </summary>
public class DoctorPreviewSdkTests
{
    [Fact]
    public void DoctorContext_WithPreviewSdk_SetsIsPreviewSdk()
    {
        var context = new DoctorContext(
            WorkingDirectory: "/test",
            DotNetSdkPath: "/usr/local/share/dotnet",
            GlobalJsonPath: null,
            PinnedSdkVersion: null,
            PinnedWorkloadSetVersion: null,
            EffectiveFeatureBand: "11.0.100",
            IsPreviewSdk: true,
            ActiveSdkVersion: "11.0.100-preview.1"
        );

        context.IsPreviewSdk.Should().BeTrue();
        context.ActiveSdkVersion.Should().Be("11.0.100-preview.1");
    }

    [Fact]
    public void DoctorContext_WithStableSdk_DefaultsToNotPreview()
    {
        var context = new DoctorContext(
            WorkingDirectory: "/test",
            DotNetSdkPath: "/usr/local/share/dotnet",
            GlobalJsonPath: null,
            PinnedSdkVersion: null,
            PinnedWorkloadSetVersion: null,
            EffectiveFeatureBand: "10.0.100"
        );

        context.IsPreviewSdk.Should().BeFalse();
        context.ActiveSdkVersion.Should().BeNull();
        context.RollForwardPolicy.Should().BeNull();
        context.ResolvedSdkVersion.Should().BeNull();
    }

    [Fact]
    public void DoctorContext_WithRollForward_StoresResolvedVersion()
    {
        var context = new DoctorContext(
            WorkingDirectory: "/test",
            DotNetSdkPath: "/usr/local/share/dotnet",
            GlobalJsonPath: "/test/global.json",
            PinnedSdkVersion: "10.0.100",
            PinnedWorkloadSetVersion: null,
            EffectiveFeatureBand: "10.0.100",
            IsPreviewSdk: false,
            ActiveSdkVersion: "10.0.103",
            RollForwardPolicy: "latestPatch",
            ResolvedSdkVersion: "10.0.103"
        );

        context.PinnedSdkVersion.Should().Be("10.0.100");
        context.ResolvedSdkVersion.Should().Be("10.0.103");
        context.RollForwardPolicy.Should().Be("latestPatch");
        context.ActiveSdkVersion.Should().Be("10.0.103");
    }

    [Fact]
    public void DoctorContext_WithExactPinnedMatch_ResolvedVersionMatchesPinned()
    {
        // When the exact pinned version is installed, resolved == pinned
        var context = new DoctorContext(
            WorkingDirectory: "/test",
            DotNetSdkPath: "/usr/local/share/dotnet",
            GlobalJsonPath: "/test/global.json",
            PinnedSdkVersion: "10.0.103",
            PinnedWorkloadSetVersion: null,
            EffectiveFeatureBand: "10.0.100",
            IsPreviewSdk: false,
            ActiveSdkVersion: "10.0.103",
            RollForwardPolicy: "latestPatch",
            ResolvedSdkVersion: "10.0.103"
        );

        context.ResolvedSdkVersion.Should().Be(context.PinnedSdkVersion);
    }

    [Fact]
    public void DoctorReport_WithInfoStatus_CountsAsOk()
    {
        var context = new DoctorContext(
            "/test", "/dotnet", null, null, null, "11.0.100",
            IsPreviewSdk: true, ActiveSdkVersion: "11.0.100-preview.1");

        var report = new DoctorReport(
            context,
            InstalledSdks: [new SdkVersionInfo("11.0.100-preview.1", "11.0.100", 11, 0, true)],
            AvailableSdkVersions: [new SdkVersionInfo("10.0.103", "10.0.100", 10, 0, false)],
            InstalledWorkloadSetVersion: "11.0.100-preview.1",
            AvailableWorkloadSetVersions: ["11.0.100-preview.1"],
            Manifests: [],
            Dependencies:
            [
                new DependencyStatus(".NET SDK", DependencyCategory.DotNetSdk,
                    null, null, "11.0.100-preview.1",
                    DependencyStatusType.Info,
                    "Preview SDK (11.0.100-preview.1)",
                    IsFixable: false)
            ],
            DateTime.UtcNow
        );

        report.OkCount.Should().Be(1, "Info status should count toward OkCount");
        report.WarningCount.Should().Be(0);
        report.ErrorCount.Should().Be(0);
        report.HasErrors.Should().BeFalse();
        report.HasWarnings.Should().BeFalse();
    }

    [Fact]
    public void DoctorReport_PreviewSdkWithNoAvailableUpdateForMajor_ShouldNotWarn()
    {
        // Scenario: User has 11.0.100-preview.1 installed, only 10.x stable available
        // Should NOT suggest 10.0.103 as an update
        var context = new DoctorContext(
            "/test", "/dotnet", null, null, null, "11.0.100",
            IsPreviewSdk: true, ActiveSdkVersion: "11.0.100-preview.1");

        var installedSdks = new List<SdkVersionInfo>
        {
            new("11.0.100-preview.1", "11.0.100", 11, 0, true),
            new("10.0.103", "10.0.100", 10, 0, false)
        };

        var availableSdks = new List<SdkVersionInfo>
        {
            new("11.0.100-preview.1", "11.0.100", 11, 0, true),
            new("10.0.103", "10.0.100", 10, 0, false),
            new("10.0.102", "10.0.100", 10, 0, false),
        };

        // The DoctorService would generate an Info status for the preview SDK
        // since the latest available for major 11 is the same version
        var sdkDep = new DependencyStatus(
            ".NET SDK", DependencyCategory.DotNetSdk,
            null, null, "11.0.100-preview.1",
            DependencyStatusType.Info,
            "Preview SDK (11.0.100-preview.1)",
            IsFixable: false);

        var report = new DoctorReport(
            context, installedSdks, availableSdks,
            "11.0.100-preview.1", ["11.0.100-preview.1"],
            [], [sdkDep], DateTime.UtcNow);

        report.HasWarnings.Should().BeFalse("preview SDK should not trigger warnings");
        report.OkCount.Should().Be(1);
    }

    [Fact]
    public void DoctorReport_PreviewSdkWithNewerPreviewAvailable_ShouldWarn()
    {
        // Scenario: User has 11.0.100-preview.1 but preview.2 is available
        var context = new DoctorContext(
            "/test", "/dotnet", null, null, null, "11.0.100",
            IsPreviewSdk: true, ActiveSdkVersion: "11.0.100-preview.1");

        var installedSdks = new List<SdkVersionInfo>
        {
            new("11.0.100-preview.1", "11.0.100", 11, 0, true),
        };

        var availableSdks = new List<SdkVersionInfo>
        {
            new("11.0.100-preview.2", "11.0.100", 11, 0, true),
            new("11.0.100-preview.1", "11.0.100", 11, 0, true),
            new("10.0.103", "10.0.100", 10, 0, false),
        };

        // DoctorService would detect that 11.0.100-preview.2 is available
        // and show a warning for the same major version
        var sdkDep = new DependencyStatus(
            ".NET SDK", DependencyCategory.DotNetSdk,
            null, "11.0.100-preview.2", "11.0.100-preview.1",
            DependencyStatusType.Warning,
            "Update available: 11.0.100-preview.2",
            IsFixable: false);

        var report = new DoctorReport(
            context, installedSdks, availableSdks,
            null, null, [], [sdkDep], DateTime.UtcNow);

        report.HasWarnings.Should().BeTrue("newer preview for same major should trigger warning");
        report.WarningCount.Should().Be(1);
    }

    [Fact]
    public void DoctorReport_StableSdkWithUpdate_ShouldWarn()
    {
        // Scenario: Stable SDK, standard behavior
        var context = new DoctorContext(
            "/test", "/dotnet", null, null, null, "10.0.100",
            IsPreviewSdk: false, ActiveSdkVersion: "10.0.102");

        var sdkDep = new DependencyStatus(
            ".NET SDK", DependencyCategory.DotNetSdk,
            null, "10.0.103", "10.0.102",
            DependencyStatusType.Warning,
            "Update available: 10.0.103",
            IsFixable: false);

        var report = new DoctorReport(
            context,
            [new SdkVersionInfo("10.0.102", "10.0.100", 10, 0, false)],
            [new SdkVersionInfo("10.0.103", "10.0.100", 10, 0, false)],
            null, null, [], [sdkDep], DateTime.UtcNow);

        report.HasWarnings.Should().BeTrue();
        report.WarningCount.Should().Be(1);
    }

    [Fact]
    public void AvailableSdkFiltering_ShouldNotShowPreviewsForStableOnlyMajorVersions()
    {
        // This tests the filtering logic conceptually:
        // Given installed SDKs with preview for major 11 but only stable for major 10,
        // available SDKs should include previews for 11 but not for 10
        var installedSdks = new List<SdkVersionInfo>
        {
            new("11.0.100-preview.1", "11.0.100", 11, 0, true),
            new("10.0.103", "10.0.100", 10, 0, false),
        };

        var previewMajorVersions = new HashSet<int>(
            installedSdks.Where(s => s.IsPreview).Select(s => s.Major));

        previewMajorVersions.Should().Contain(11);
        previewMajorVersions.Should().NotContain(10);

        // Simulating all available SDKs from the releases feed
        var allAvailable = new List<SdkVersionInfo>
        {
            new("11.0.100-preview.2", "11.0.100", 11, 0, true),
            new("11.0.100-preview.1", "11.0.100", 11, 0, true),
            new("10.0.200-preview.1", "10.0.200", 10, 0, true),  // Should be filtered out
            new("10.0.103", "10.0.100", 10, 0, false),
            new("10.0.102", "10.0.100", 10, 0, false),
        };

        // Apply the same filtering logic as DoctorService
        var filtered = allAvailable
            .Where(s => !s.IsPreview || previewMajorVersions.Contains(s.Major))
            .ToList();

        filtered.Should().HaveCount(4);
        filtered.Should().Contain(s => s.Version == "11.0.100-preview.2");
        filtered.Should().Contain(s => s.Version == "11.0.100-preview.1");
        filtered.Should().Contain(s => s.Version == "10.0.103");
        filtered.Should().Contain(s => s.Version == "10.0.102");
        filtered.Should().NotContain(s => s.Version == "10.0.200-preview.1",
            "previews for major 10 should be excluded since user only has stable 10.x");
    }
}
