using FluentAssertions;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class DotnetWorkloadParserTests
{
    [Fact]
    public void ParsesMachineReadableListWithPreamble()
    {
        const string output = """
        Skipping NuGet package signature verification.
        {"installed":["wasm-tools","android"],"updateAvailable":[{"existingManifestVersion":"36.1.53","availableUpdateManifestVersion":"36.1.69","description":"Android workload","workloadId":"android"}]}
        """;

        var result = DotnetWorkloadParser.ParseMachineReadableList(output);

        result.UsedMachineReadableOutput.Should().BeTrue();
        result.Installed.Select(item => item.Id).Should().Equal("android", "wasm-tools");
        result.Updates.Should().ContainSingle()
            .Which.AvailableManifestVersion.Should().Be("36.1.69");
    }

    [Fact]
    public void ParsesPlainListFallback()
    {
        const string output = """

        Workload version: 10.0.300.1

        Installed Workload Id      Manifest Version       Installation Source
        ---------------------------------------------------------------------
        android                    36.1.53/10.0.100       SDK 10.0.300
        wasm-tools                 10.0.108/10.0.100      SDK 10.0.300

        Use `dotnet workload search` to find additional workloads to install.
        """;

        var result = DotnetWorkloadParser.ParsePlainList(output);

        result.UsedMachineReadableOutput.Should().BeFalse();
        result.Installed.Should().HaveCount(2);
        result.Installed[0].ManifestVersion.Should().Be("36.1.53/10.0.100");
        result.Installed[1].InstallationSource.Should().Be("SDK 10.0.300");
    }

    [Fact]
    public void ParsesAvailableSetVersionsInSemanticOrder()
    {
        const string output = """
        [{"workloadVersion":"10.0.300.3"},{"workloadVersion":"10.0.302"},{"workloadVersion":"10.0.301.1"}]
        """;

        DotnetWorkloadParser.ParseAvailableSetVersions(output)
            .Select(item => item.Version)
            .Should().Equal("10.0.302", "10.0.301.1", "10.0.300.3");
    }

    [Fact]
    public void ParsesWorkloadSetManifestMap()
    {
        const string output = """
        {
          "manifestVersions": {
            "microsoft.net.sdk.android": "36.1.53/10.0.100",
            "microsoft.net.sdk.maui": "10.0.20/10.0.100"
          }
        }
        """;

        var result = DotnetWorkloadParser.ParseManifestVersions(output);

        result.Should().HaveCount(2);
        result[0].ManifestId.Should().Be("microsoft.net.sdk.android");
        result[0].Version.Should().Be("36.1.53");
        result[0].FeatureBand.Should().Be("10.0.100");
    }

    [Theory]
    [InlineData("10.0.300.1", DotnetWorkloadUpdateMode.WorkloadSet)]
    [InlineData("11.0.100-manifests.d0c9cb64", DotnetWorkloadUpdateMode.Unknown)]
    public void InfersModeFromResolvedVersion(string version, DotnetWorkloadUpdateMode expected)
    {
        DotnetWorkloadParser.InferUpdateMode(version).Should().Be(expected);
    }

    [Theory]
    [InlineData("The workload update mode is workload-set.", DotnetWorkloadUpdateMode.WorkloadSet)]
    [InlineData("manifests", DotnetWorkloadUpdateMode.Manifests)]
    public void ParsesConfiguredUpdateMode(string output, DotnetWorkloadUpdateMode expected)
    {
        DotnetWorkloadParser.ParseConfiguredUpdateMode(output).Should().Be(expected);
    }
}
