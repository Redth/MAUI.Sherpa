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
    }

    [Fact]
    public void DoctorContext_DotnetUpFields_RoundTrip()
    {
        var context = new DoctorContext(
            "/test", "/dotnet", null, null, null, "10.0.100",
            DotnetUpInstalled: true,
            DotnetUpVersion: "0.1.4-preview.6.26323.4",
            DotnetUpManagedInstallRoot: "/Users/x/Library/Application Support/dotnet");

        context.DotnetUpInstalled.Should().BeTrue();
        context.DotnetUpVersion.Should().Be("0.1.4-preview.6.26323.4");
        context.DotnetUpManagedInstallRoot.Should().Be("/Users/x/Library/Application Support/dotnet");
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
    public void MergeManagedSdks_AddsDotnetUpVersionsAndSortsDescending()
    {
        var local = new List<SdkVersion>
        {
            SdkVersion.Parse("9.0.305")
        };

        var dotnetUpList = DotnetUpParser.ParseList("""
        { "installations": [
          { "component": "SDK", "version": "10.0.300", "installRoot": "/u/dotnet", "architecture": "arm64", "isValid": true },
          { "component": "SDK", "version": "9.0.305", "installRoot": "/u/dotnet", "architecture": "arm64", "isValid": true },
          { "component": "Runtime", "version": "10.0.8", "installRoot": "/u/dotnet", "architecture": "arm64", "isValid": true }
        ] }
        """);

        var merged = DoctorService.MergeManagedSdks(local, dotnetUpList);

        merged.Select(s => s.Version).Should().ContainInOrder("10.0.300", "9.0.305");
        merged.Should().HaveCount(2, "the duplicate 9.0.305 is de-duplicated and runtimes are excluded");
        merged[0].Version.Should().Be("10.0.300", "newest SDK should sort first");
    }

    [Fact]
    public void MergeManagedSdks_IgnoresInvalidManaged_AndEmptyList()
    {
        var local = new List<SdkVersion> { SdkVersion.Parse("10.0.103") };
        var empty = new DotnetUpListResult();

        var merged = DoctorService.MergeManagedSdks(local, empty);

        merged.Should().ContainSingle().Which.Version.Should().Be("10.0.103");
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
