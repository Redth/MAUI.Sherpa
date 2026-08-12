using FluentAssertions;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

/// <summary>
/// The resolver decides which .NET install root a surface such as Doctor should trust. Once a user
/// opts into dotnetup, the managed root wins outright — mixing it with a machine-wide install
/// produces an SDK/feature-band pair that belongs to neither root.
/// </summary>
public class DotnetSdkSourceResolverTests
{
    [Fact]
    public void Resolve_WithNoDotnetUpList_UsesMachineInstall()
    {
        var local = new[] { SdkVersion.Parse("10.0.302"), SdkVersion.Parse("10.0.204") };

        var source = DotnetSdkSourceResolver.Resolve(local, "/usr/local/share/dotnet", null, "arm64");

        source.IsDotnetUpManaged.Should().BeFalse();
        source.InstallRoot.Should().Be("/usr/local/share/dotnet");
        source.Architecture.Should().Be("arm64");
        source.Sdks.Select(s => s.Version).Should().Equal("10.0.302", "10.0.204");
    }

    [Fact]
    public void Resolve_WithManagedSdks_IgnoresMachineInstallEntirely()
    {
        var local = new[]
        {
            SdkVersion.Parse("11.0.100-preview.5.26302.115"),
            SdkVersion.Parse("10.0.300")
        };
        var list = Parse("""
        { "installations": [
          { "component": "SDK", "version": "11.0.100-preview.6.26359.118", "installRoot": "/managed", "architecture": "arm64", "isValid": true }
        ] }
        """);

        var source = DotnetSdkSourceResolver.Resolve(local, "/usr/local/share/dotnet", list, "arm64");

        source.IsDotnetUpManaged.Should().BeTrue();
        source.InstallRoot.Should().Be("/managed");
        source.Sdks.Select(s => s.Version).Should().Equal("11.0.100-preview.6.26359.118");
    }

    [Fact]
    public void Resolve_DeduplicatesRepeatedManagedVersions()
    {
        var list = Parse("""
        { "installations": [
          { "component": "SDK", "version": "10.0.302", "installRoot": "/managed", "architecture": "arm64", "isValid": true },
          { "component": "SDK", "version": "10.0.302", "installRoot": "/managed", "architecture": "arm64", "isValid": true }
        ] }
        """);

        var source = DotnetSdkSourceResolver.Resolve([], null, list, "arm64");

        source.Sdks.Should().ContainSingle().Which.Version.Should().Be("10.0.302");
    }

    [Fact]
    public void Resolve_SkipsRuntimesAndInvalidInstallations()
    {
        var list = Parse("""
        { "installations": [
          { "component": "Runtime", "version": "10.0.10", "installRoot": "/managed", "architecture": "arm64", "isValid": true },
          { "component": "ASPNETCore", "version": "10.0.10", "installRoot": "/managed", "architecture": "arm64", "isValid": true },
          { "component": "SDK", "version": "10.0.302", "installRoot": "/managed", "architecture": "arm64", "isValid": false }
        ] }
        """);

        var source = DotnetSdkSourceResolver.Resolve(
            [SdkVersion.Parse("9.0.305")], "/usr/local/share/dotnet", list, "arm64");

        source.IsDotnetUpManaged.Should().BeFalse();
        source.Sdks.Should().ContainSingle().Which.Version.Should().Be("9.0.305");
    }

    [Fact]
    public void Resolve_PrefersArchitectureMatchOverNewerSdk()
    {
        var list = Parse("""
        { "installations": [
          { "component": "SDK", "version": "11.0.100-preview.6.26359.118", "installRoot": "/managed-x64", "architecture": "x64", "isValid": true },
          { "component": "SDK", "version": "10.0.302", "installRoot": "/managed-arm64", "architecture": "arm64", "isValid": true }
        ] }
        """);

        var source = DotnetSdkSourceResolver.Resolve([], null, list, "arm64");

        source.InstallRoot.Should().Be("/managed-arm64");
        source.Architecture.Should().Be("arm64");
    }

    [Fact]
    public void Resolve_WithSameArchitecture_PrefersRootWithNewestSdk()
    {
        var list = Parse("""
        { "installations": [
          { "component": "SDK", "version": "10.0.204", "installRoot": "/managed-a", "architecture": "arm64", "isValid": true },
          { "component": "SDK", "version": "11.0.100-preview.6.26359.118", "installRoot": "/managed-b", "architecture": "arm64", "isValid": true }
        ] }
        """);

        var source = DotnetSdkSourceResolver.Resolve([], null, list, "arm64");

        source.InstallRoot.Should().Be("/managed-b");
    }

    [Fact]
    public void Resolve_WithEmptyList_FallsBackToMachineInstall()
    {
        var source = DotnetSdkSourceResolver.Resolve(
            [SdkVersion.Parse("10.0.103")], "/usr/local/share/dotnet", new DotnetUpListResult(), "arm64");

        source.IsDotnetUpManaged.Should().BeFalse();
        source.InstallRoot.Should().Be("/usr/local/share/dotnet");
    }

    [Fact]
    public void Resolve_WithNothingInstalled_ReturnsEmptySource()
    {
        var source = DotnetSdkSourceResolver.Resolve([], null, null, "arm64");

        source.Sdks.Should().BeEmpty();
        source.InstallRoot.Should().BeNull();
        source.IsDotnetUpManaged.Should().BeFalse();
    }

    private static DotnetUpListResult Parse(string json) => DotnetUpParser.ParseList(json);
}
