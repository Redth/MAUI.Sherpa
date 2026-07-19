using FluentAssertions;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class WorkloadInstallStateReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"maui-sherpa-workload-state-{Guid.NewGuid():N}");

    [Fact]
    public void ReadUpdateMode_MissingState_DefaultsToWorkloadSet()
    {
        WorkloadInstallStateReader.ReadUpdateMode(CreateTarget())
            .Should().Be(DotnetWorkloadUpdateMode.WorkloadSet);
    }

    [Theory]
    [InlineData("{}", DotnetWorkloadUpdateMode.WorkloadSet)]
    [InlineData("""{"useWorkloadSets":null}""", DotnetWorkloadUpdateMode.WorkloadSet)]
    [InlineData("""{"useWorkloadSets":true}""", DotnetWorkloadUpdateMode.WorkloadSet)]
    [InlineData("""{"useWorkloadSets":false}""", DotnetWorkloadUpdateMode.Manifests)]
    public void ReadUpdateMode_UsesSdkInstallState(
        string json,
        DotnetWorkloadUpdateMode expected)
    {
        var path = Path.Combine(
            _root,
            "metadata",
            "workloads",
            "Arm64",
            "10.0.300",
            "InstallState",
            "default.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);

        WorkloadInstallStateReader.ReadUpdateMode(CreateTarget())
            .Should().Be(expected);
    }

    [Fact]
    public void ReadManifestVersions_UsesRecordedVersionsAndFeatureBands()
    {
        WriteState("""
        {
          "useWorkloadSets": false,
          "manifests": {
            "microsoft.net.sdk.ios": "18.5.9219/10.0.100",
            "microsoft.net.sdk.android": "35.0.50"
          }
        }
        """);

        var manifests = WorkloadInstallStateReader.ReadManifestVersions(CreateTarget());

        manifests.Should().BeEquivalentTo(
        [
            new DotnetManifestVersion
            {
                ManifestId = "microsoft.net.sdk.ios",
                Version = "18.5.9219",
                FeatureBand = "10.0.100"
            },
            new DotnetManifestVersion
            {
                ManifestId = "microsoft.net.sdk.android",
                Version = "35.0.50",
                FeatureBand = "10.0.300"
            }
        ]);
    }

    private void WriteState(string json)
    {
        var path = Path.Combine(
            _root,
            "metadata",
            "workloads",
            "Arm64",
            "10.0.300",
            "InstallState",
            "default.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private DotnetWorkloadTarget CreateTarget() => new()
    {
        InstallRoot = _root,
        DotnetPath = Path.Combine(_root, "dotnet"),
        Architecture = "arm64",
        FeatureBand = new SdkFeatureBand("10.0.302"),
        RepresentativeSdkVersion = "10.0.302"
    };

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
