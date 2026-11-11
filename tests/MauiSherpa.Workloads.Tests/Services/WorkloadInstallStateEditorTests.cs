using System.Text.Json;
using FluentAssertions;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class WorkloadInstallStateEditorTests : IDisposable
{
    private const string MissingVersion = "10.0.403";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"maui-sherpa-workload-state-editor-{Guid.NewGuid():N}");

    private string StatePath => Path.Combine(
        _root,
        "metadata",
        "workloads",
        "Arm64",
        "10.0.400",
        "InstallState",
        "default.json");

    [Fact]
    public void ClearMissingWorkloadVersion_RemovesOnlyTheVersionAndKeepsBackup()
    {
        var original = """
        {
          // written by the SDK
          "useWorkloadSets": true,
          "workloadVersion": "10.0.403",
          "manifests": { "microsoft.net.sdk.maui": "10.0.0/10.0.100" },
        }
        """;
        WriteState(original);

        WorkloadInstallStateEditor.ClearMissingWorkloadVersion(CreateTarget(), MissingVersion)
            .Should().BeTrue();

        using var document = JsonDocument.Parse(File.ReadAllText(StatePath));
        document.RootElement.TryGetProperty("workloadVersion", out _).Should().BeFalse();
        document.RootElement.GetProperty("useWorkloadSets").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("manifests").GetProperty("microsoft.net.sdk.maui").GetString()
            .Should().Be("10.0.0/10.0.100");
        File.ReadAllText($"{StatePath}.bak-missing-{MissingVersion}").Should().Be(original);
        Directory.GetFiles(Path.GetDirectoryName(StatePath)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void ClearMissingWorkloadVersion_LeavesStateAloneWhenVersionChanged()
    {
        var original = """{"workloadVersion":"10.0.404"}""";
        WriteState(original);

        WorkloadInstallStateEditor.ClearMissingWorkloadVersion(CreateTarget(), MissingVersion)
            .Should().BeFalse();

        File.ReadAllText(StatePath).Should().Be(original);
        File.Exists($"{StatePath}.bak-missing-{MissingVersion}").Should().BeFalse();
    }

    [Fact]
    public void ClearMissingWorkloadVersion_LeavesStateAloneWhenSetIsInstalled()
    {
        var original = """{"workloadVersion":"10.0.403"}""";
        WriteState(original);
        var workloadSet = Path.Combine(
            _root,
            "sdk-manifests",
            "10.0.400",
            "workloadsets",
            MissingVersion,
            "microsoft.net.workloads.workloadset.json");
        Directory.CreateDirectory(Path.GetDirectoryName(workloadSet)!);
        File.WriteAllText(workloadSet, "{}");

        WorkloadInstallStateEditor.ClearMissingWorkloadVersion(CreateTarget(), MissingVersion)
            .Should().BeFalse();

        File.ReadAllText(StatePath).Should().Be(original);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void WriteState(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        File.WriteAllText(StatePath, json);
    }

    private DotnetWorkloadTarget CreateTarget() => new()
    {
        InstallRoot = _root,
        DotnetPath = Path.Combine(_root, "dotnet"),
        Architecture = "arm64",
        FeatureBand = new SdkFeatureBand("10.0.404"),
        RepresentativeSdkVersion = "10.0.404",
        IsManagedByDotnetUp = true,
        CanWrite = true
    };
}
