using FluentAssertions;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class DotnetUpParserTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Services", "DotnetUpFixtures", name);

    private static string ReadFixture(string name) => File.ReadAllText(FixturePath(name));

    [Fact]
    public void ParseList_RealFixture_ParsesInstallSpecsAndInstallations()
    {
        var json = ReadFixture("list.json");

        var result = DotnetUpParser.ParseList(json);

        result.InstallSpecs.Should().HaveCount(9);
        result.Installations.Should().HaveCount(9);
    }

    [Fact]
    public void ParseList_MapsComponentsAndSources()
    {
        var json = ReadFixture("list.json");

        var result = DotnetUpParser.ParseList(json);

        result.InstallSpecs.Should().Contain(s =>
            s.Component == DotnetUpComponent.Sdk &&
            s.VersionOrChannel == "10.0.1xx" &&
            s.Source == DotnetUpInstallSource.Explicit);

        result.InstallSpecs.Should().Contain(s =>
            s.Component == DotnetUpComponent.AspNetCore &&
            s.VersionOrChannel == "10.0");

        result.InstallSpecs.Should().Contain(s =>
            s.Component == DotnetUpComponent.Runtime &&
            s.VersionOrChannel == "9.0");
    }

    [Fact]
    public void ParseList_MapsInstallationFields()
    {
        var json = ReadFixture("list.json");

        var result = DotnetUpParser.ParseList(json);

        var preview = result.Installations.Single(i => i.Version == "11.0.100-preview.5.26302.115");
        preview.Component.Should().Be(DotnetUpComponent.Sdk);
        preview.ComponentRaw.Should().Be("SDK");
        preview.InstallRoot.Should().Be("/Users/redth/Library/Application Support/dotnet");
        preview.Architecture.Should().Be("arm64");
        preview.IsValid.Should().BeTrue();
        preview.FrameworkName.Should().Be(".NET SDK");

        var runtime = result.Installations.Single(i =>
            i.Component == DotnetUpComponent.Runtime && i.Version == "10.0.8");
        runtime.FrameworkName.Should().Be("Microsoft.NETCore.App");
    }

    [Fact]
    public void ParseList_InstallRoots_Deduplicates()
    {
        var json = ReadFixture("list.json");

        var result = DotnetUpParser.ParseList(json);

        result.InstallRoots.Should().ContainSingle()
            .Which.Should().Be("/Users/redth/Library/Application Support/dotnet");
    }

    [Fact]
    public void GetManagedSdkVersions_ReturnsOnlySdks()
    {
        var json = ReadFixture("list.json");
        var result = DotnetUpParser.ParseList(json);

        var versions = DotnetUpParser.GetManagedSdkVersions(result);

        versions.Should().BeEquivalentTo(new[]
        {
            "11.0.100-preview.5.26302.115",
            "10.0.103",
            "10.0.203",
            "9.0.305",
            "10.0.300"
        });
    }

    [Fact]
    public void ParseList_EmptyOrWhitespace_ReturnsEmpty()
    {
        DotnetUpParser.ParseList("").InstallSpecs.Should().BeEmpty();
        DotnetUpParser.ParseList("   ").Installations.Should().BeEmpty();
    }

    [Fact]
    public void ParseList_MissingArrays_DoesNotThrow()
    {
        var result = DotnetUpParser.ParseList("{}");

        result.InstallSpecs.Should().BeEmpty();
        result.Installations.Should().BeEmpty();
    }

    [Fact]
    public void ParseList_UnknownComponent_MapsToUnknownButKeepsRaw()
    {
        var json = """
        { "installations": [
          { "component": "Mystery", "version": "1.2.3", "installRoot": "/tmp", "architecture": "arm64", "isValid": true }
        ] }
        """;

        var result = DotnetUpParser.ParseList(json);

        var only = result.Installations.Single();
        only.Component.Should().Be(DotnetUpComponent.Unknown);
        only.ComponentRaw.Should().Be("Mystery");
    }

    [Fact]
    public void ParseInfo_RealFixture_ExtractsToolDetails()
    {
        var text = ReadFixture("info.txt");

        var info = DotnetUpParser.ParseInfo(text);

        info.Should().NotBeNull();
        info!.Version.Should().Be("0.1.4-preview.6.26323.4");
        info.Commit.Should().Be("f555e30");
        info.Architecture.Should().Be("arm64");
        info.Rid.Should().Be("osx-arm64");
    }

    [Fact]
    public void ParseInfo_EmptyOrUnrecognized_ReturnsNull()
    {
        DotnetUpParser.ParseInfo("").Should().BeNull();
        DotnetUpParser.ParseInfo("no version here").Should().BeNull();
    }
}
