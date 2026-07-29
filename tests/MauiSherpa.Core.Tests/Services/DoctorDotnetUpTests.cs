using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;
using Xunit;

namespace MauiSherpa.Core.Tests.Services;

/// <summary>
/// Tests for the dotnetup-aware behavior added to Doctor: context fields, the dotnetup
/// presence dependency status, the fixable .NET SDK status, and managed-SDK reconciliation.
/// </summary>
public class DoctorDotnetUpTests
{
    [Fact]
    public void DoctorContext_DotnetUpFields_DefaultToNotInstalled()
    {
        var context = new DoctorContext(
            "/test", "/dotnet", null, null, null, "10.0.100");

        context.DotnetUpInstalled.Should().BeFalse();
        context.DotnetUpVersion.Should().BeNull();
        context.DotnetUpManagedInstallRoot.Should().BeNull();
        context.UsesDotnetUpManagedSdk.Should().BeFalse();
    }

    [Fact]
    public void DoctorContext_DotnetUpFields_RoundTrip()
    {
        var context = new DoctorContext(
            "/test", "/dotnet", null, null, null, "10.0.100",
            DotnetUpInstalled: true,
            DotnetUpVersion: "0.1.4-preview.6.26323.4",
            DotnetUpManagedInstallRoot: "/Users/x/Library/Application Support/dotnet",
            UsesDotnetUpManagedSdk: true);

        context.DotnetUpInstalled.Should().BeTrue();
        context.DotnetUpVersion.Should().Be("0.1.4-preview.6.26323.4");
        context.DotnetUpManagedInstallRoot.Should().Be("/Users/x/Library/Application Support/dotnet");
        context.UsesDotnetUpManagedSdk.Should().BeTrue();
    }

    [Fact]
    public void DotnetUpPresenceStatus_WhenInstalled_IsInfoAndCountsOk()
    {
        var dep = new DependencyStatus(
            "dotnetup", DependencyCategory.DotNetSdk,
            null, null, "0.1.4-preview.6.26323.4",
            DependencyStatusType.Info,
            "Installed (0.1.4-preview.6.26323.4) — manages .NET SDKs & runtimes",
            IsFixable: false);

        var report = MakeReport(dep);

        report.OkCount.Should().Be(1);
        report.WarningCount.Should().Be(0);
        report.HasWarnings.Should().BeFalse();
    }

    [Fact]
    public void DotnetUpPresenceStatus_WhenMissing_IsFixableInstallAction()
    {
        var dep = new DependencyStatus(
            "dotnetup", DependencyCategory.DotNetSdk,
            null, null, null,
            DependencyStatusType.Info,
            "Not installed — install to manage .NET SDKs & runtimes",
            IsFixable: true,
            FixAction: "install-dotnetup");

        dep.IsFixable.Should().BeTrue();
        dep.FixAction.Should().Be("install-dotnetup");

        // Info status keeps the install action out of the warning/error counts.
        var report = MakeReport(dep);
        report.HasWarnings.Should().BeFalse();
        report.HasErrors.Should().BeFalse();
    }

    [Fact]
    public void OutOfDateSdkStatus_WithDotnetUp_EncodesChannelInFixAction()
    {
        var dep = new DependencyStatus(
            ".NET SDK", DependencyCategory.DotNetSdk,
            null, "10.0.103", "9.0.305",
            DependencyStatusType.Warning,
            "Update available: 10.0.103",
            IsFixable: true,
            FixAction: "dotnetup-update-sdk:10.0.103");

        dep.IsFixable.Should().BeTrue();
        dep.FixAction.Should().StartWith("dotnetup-update-sdk:");

        var channel = dep.FixAction!["dotnetup-update-sdk:".Length..];
        channel.Should().Be("10.0.103");
    }

    [Fact]
    public void SdkSource_PrefersDotnetUpManagedRoot_OverMachineInstall()
    {
        // The machine root has an older .NET 11 preview than dotnetup's managed root.
        var local = new List<SdkVersion>
        {
            SdkVersion.Parse("11.0.100-preview.5.26302.115"),
            SdkVersion.Parse("11.0.100-preview.4.26230.115"),
            SdkVersion.Parse("10.0.300")
        };

        var dotnetUpList = DotnetUpParser.ParseList("""
        { "installations": [
          { "component": "SDK", "version": "11.0.100-preview.6.26359.118", "installRoot": "/u/managed", "architecture": "arm64", "isValid": true },
          { "component": "SDK", "version": "10.0.302", "installRoot": "/u/managed", "architecture": "arm64", "isValid": true },
          { "component": "Runtime", "version": "10.0.10", "installRoot": "/u/managed", "architecture": "arm64", "isValid": true }
        ] }
        """);

        var source = DotnetSdkSourceResolver.Resolve(
            local, "/usr/local/share/dotnet", dotnetUpList, "arm64");

        source.IsDotnetUpManaged.Should().BeTrue();
        source.InstallRoot.Should().Be("/u/managed");
        source.Architecture.Should().Be("arm64");
        source.Sdks.Select(s => s.Version).Should().Equal(
            "11.0.100-preview.6.26359.118", "10.0.302");
        source.Sdks.Should().NotContain(
            s => s.Version == "11.0.100-preview.5.26302.115",
            "machine-wide SDKs are ignored once dotnetup owns the toolchain");
    }

    [Fact]
    public void SdkSource_SortsPreviewsByPrereleaseLabel()
    {
        var dotnetUpList = DotnetUpParser.ParseList("""
        { "installations": [
          { "component": "SDK", "version": "11.0.100-preview.5.26302.115", "installRoot": "/u/managed", "architecture": "arm64", "isValid": true },
          { "component": "SDK", "version": "11.0.100-preview.6.26359.118", "installRoot": "/u/managed", "architecture": "arm64", "isValid": true },
          { "component": "SDK", "version": "11.0.100-preview.4.26230.115", "installRoot": "/u/managed", "architecture": "arm64", "isValid": true }
        ] }
        """);

        var source = DotnetSdkSourceResolver.Resolve([], null, dotnetUpList, "arm64");

        source.Sdks[0].Version.Should().Be(
            "11.0.100-preview.6.26359.118",
            "prerelease labels must participate in ordering, not just major.minor.patch");
    }

    [Fact]
    public void SdkSource_PrefersManagedRootMatchingProcessArchitecture()
    {
        var dotnetUpList = DotnetUpParser.ParseList("""
        { "installations": [
          { "component": "SDK", "version": "10.0.400", "installRoot": "/u/x64", "architecture": "x64", "isValid": true },
          { "component": "SDK", "version": "10.0.302", "installRoot": "/u/arm64", "architecture": "arm64", "isValid": true }
        ] }
        """);

        var source = DotnetSdkSourceResolver.Resolve([], null, dotnetUpList, "arm64");

        source.InstallRoot.Should().Be(
            "/u/arm64", "architecture match wins over a newer SDK in a foreign-architecture root");
    }

    [Fact]
    public void SdkSource_WithoutManagedSdks_FallsBackToMachineInstall()
    {
        var local = new List<SdkVersion> { SdkVersion.Parse("10.0.103") };

        // dotnetup is present but only tracks runtimes / has invalid SDK entries.
        var dotnetUpList = DotnetUpParser.ParseList("""
        { "installations": [
          { "component": "Runtime", "version": "10.0.10", "installRoot": "/u/managed", "architecture": "arm64", "isValid": true },
          { "component": "SDK", "version": "10.0.302", "installRoot": "/u/managed", "architecture": "arm64", "isValid": false }
        ] }
        """);

        var source = DotnetSdkSourceResolver.Resolve(
            local, "/usr/local/share/dotnet", dotnetUpList, "arm64");

        source.IsDotnetUpManaged.Should().BeFalse();
        source.InstallRoot.Should().Be("/usr/local/share/dotnet");
        source.Sdks.Should().ContainSingle().Which.Version.Should().Be("10.0.103");
    }

    [Fact]
    public void SdkSource_WithoutDotnetUp_UsesMachineInstall()
    {
        var local = new List<SdkVersion>
        {
            SdkVersion.Parse("11.0.100-preview.4.26230.115"),
            SdkVersion.Parse("11.0.100-preview.5.26302.115")
        };

        var source = DotnetSdkSourceResolver.Resolve(
            local, "/usr/local/share/dotnet", dotnetUpList: null, "arm64");

        source.IsDotnetUpManaged.Should().BeFalse();
        source.Sdks[0].Version.Should().Be("11.0.100-preview.5.26302.115");
    }

    [Fact]
    public void FindSdkChannelPreview_PrefersSpecificChannelOverMovingAlias()
    {
        var active = SdkVersion.Parse("11.0.100-preview.6.26359.118");
        var previews = new List<DotnetUpdatePreview>
        {
            new()
            {
                Component = DotnetUpComponent.Sdk,
                Channel = "preview",
                InstalledVersion = active.Version,
                AvailableVersion = active.Version
            },
            new()
            {
                Component = DotnetUpComponent.Sdk,
                Channel = "11.0.1xx",
                InstalledVersion = active.Version,
                AvailableVersion = active.Version
            },
            new()
            {
                Component = DotnetUpComponent.Sdk,
                Channel = "10.0.3xx",
                InstalledVersion = "10.0.302",
                AvailableVersion = "10.0.302"
            }
        };

        var match = DoctorService.FindSdkChannelPreview(previews, active);

        match.Should().NotBeNull();
        match!.Channel.Should().Be("11.0.1xx");
    }

    [Fact]
    public void FindSdkChannelPreview_WhenNoChannelOwnsTheActiveSdk_ReturnsNull()
    {
        var active = SdkVersion.Parse("11.0.100-preview.6.26359.118");
        var previews = new List<DotnetUpdatePreview>
        {
            new()
            {
                Component = DotnetUpComponent.Sdk,
                Channel = "10.0.3xx",
                InstalledVersion = "10.0.302",
                AvailableVersion = "10.0.302"
            }
        };

        DoctorService.FindSdkChannelPreview(previews, active).Should().BeNull();
    }

    [Fact]
    public void FindSdkChannelPreview_IgnoresPinnedSpecs()
    {
        var active = SdkVersion.Parse("10.0.302");
        var previews = new List<DotnetUpdatePreview>
        {
            new()
            {
                Component = DotnetUpComponent.Sdk,
                Channel = "10.0.302",
                InstalledVersion = "10.0.302",
                AvailableVersion = "10.0.302",
                IsPinned = true
            }
        };

        DoctorService.FindSdkChannelPreview(previews, active).Should().BeNull(
            "a pinned exact version has no channel update to offer");
    }

    [Fact]
    public void ManagedSdkStatus_UsesChannelInFixAction()
    {
        // The managed branch offers the tracked channel, not an exact version, so applying the
        // fix installs whatever that channel resolves to — the same thing the SDK Manager does.
        var dep = new DependencyStatus(
            ".NET SDK", DependencyCategory.DotNetSdk,
            null, "11.0.100-preview.7.26400.1", "11.0.100-preview.6.26359.118",
            DependencyStatusType.Warning,
            "Update available: 11.0.100-preview.7.26400.1 (dotnetup channel 11.0.1xx)",
            IsFixable: true,
            FixAction: "dotnetup-update-sdk:11.0.1xx");

        dep.FixAction!["dotnetup-update-sdk:".Length..].Should().Be("11.0.1xx");
    }

    private static DoctorReport MakeReport(params DependencyStatus[] deps) =>
        new(
            new DoctorContext("/test", "/dotnet", null, null, null, "10.0.100"),
            InstalledSdks: [],
            AvailableSdkVersions: null,
            InstalledWorkloadSetVersion: null,
            AvailableWorkloadSetVersions: null,
            Manifests: [],
            Dependencies: deps,
            DateTime.UtcNow);
}
