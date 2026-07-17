using FluentAssertions;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class WorkloadInstallationStateResolverTests
{
    [Fact]
    public void AggregateRecordsResolveEffectiveInstalledWorkloads()
    {
        var definitions = ResolveDefinitions(
        [
            Workload("maui", extends: ["maui-mobile", "maui-desktop"]),
            Workload("maui-mobile", extends: ["maui-android", "maui-ios"]),
            Workload("maui-desktop", extends: ["maui-maccatalyst"]),
            Workload("maui-android", extends: ["maui-core", "android"]),
            Workload("maui-ios", extends: ["maui-core", "ios"]),
            Workload("maui-maccatalyst", extends: ["maui-core", "maccatalyst"]),
            Workload("maui-core", isAbstract: true, packs: ["Maui.Sdk"]),
            Workload("android", packs: ["Android.Sdk"]),
            Workload("ios", packs: ["iOS.Sdk"]),
            Workload("maccatalyst", packs: ["MacCatalyst.Sdk"]),
            Workload("macos", packs: ["MacOS.Sdk"]),
            Workload("tvos", packs: ["tvOS.Sdk"]),
            Workload("wasm-tools", packs: ["Wasm.Sdk"]),
            Workload("optional-tools", packs: ["Optional.Sdk"])
        ]);
        var records = new[]
        {
            Installed("maui"),
            Installed("macos"),
            Installed("tvos"),
            Installed("wasm-tools")
        };

        var result = WorkloadInstallationStateResolver.Resolve(records, definitions);

        result.States.Where(state => state.IsExplicit).Select(state => state.Id)
            .Should().BeEquivalentTo("maui", "macos", "tvos", "wasm-tools");
        result.States.Single(state => state.Id == "android").Should().Match<DotnetWorkloadInstallationState>(
            state =>
                state.Kind == DotnetWorkloadInstallKind.Included &&
                !state.CanUninstall &&
                state.IsUserVisible &&
                state.IncludedBy.SequenceEqual(new[] { "maui" }));
        result.States.Single(state => state.Id == "ios").Kind
            .Should().Be(DotnetWorkloadInstallKind.Included);
        result.States.Single(state => state.Id == "maccatalyst").Kind
            .Should().Be(DotnetWorkloadInstallKind.Included);
        result.States.Single(state => state.Id == "maui-core").IsUserVisible.Should().BeFalse();
        result.States.Single(state => state.Id == "macos").IncludedBy.Should().BeEmpty();
        result.States.Single(state => state.Id == "tvos").IncludedBy.Should().BeEmpty();
        result.States.Single(state => state.Id == "wasm-tools").IncludedBy.Should().BeEmpty();
        records.Select(record => record.Id).Should().Equal("maui", "macos", "tvos", "wasm-tools");
        result.Diagnostics.Should().BeEmpty();

        var inventory = new DotnetWorkloadInventory
        {
            Target = Target(),
            InstalledWorkloads = records,
            EffectiveInstalledWorkloads = result.States,
            AvailableWorkloads = definitions
        };
        inventory.InstallableWorkloads.Select(workload => workload.Id)
            .Should().Equal("optional-tools");
    }

    [Fact]
    public void ExplicitStateWinsWhenAnAggregateAlsoIncludesTheWorkload()
    {
        var definitions = ResolveDefinitions(
        [
            Workload("maui", extends: ["android"]),
            Workload("android", packs: ["Android.Sdk"])
        ]);

        var result = WorkloadInstallationStateResolver.Resolve(
            [Installed("maui"), Installed("android")],
            definitions);

        var android = result.States.Single(state => state.Id == "android");
        android.Kind.Should().Be(DotnetWorkloadInstallKind.Explicit);
        android.CanUninstall.Should().BeTrue();
        android.IncludedBy.Should().Equal("maui");
    }

    [Fact]
    public void IncludedWorkloadRetainsAllExplicitParents()
    {
        var definitions = ResolveDefinitions(
        [
            Workload("aggregate-b", extends: ["child"]),
            Workload("aggregate-a", extends: ["child"]),
            Workload("child", packs: ["Child.Sdk"])
        ]);

        var result = WorkloadInstallationStateResolver.Resolve(
            [Installed("aggregate-b"), Installed("aggregate-a")],
            definitions);

        result.States.Single(state => state.Id == "child").IncludedBy
            .Should().Equal("aggregate-a", "aggregate-b");
    }

    [Fact]
    public void MissingDefinitionsRemainExplicitAndProduceDiagnostics()
    {
        var root = new ResolvedWorkloadDefinition
        {
            Id = "aggregate",
            TransitiveIncludes = ["missing-child"],
            Packs = [new ResolvedPackDefinition { Id = "Root.Pack", Version = "1", Kind = "sdk" }]
        };

        var result = WorkloadInstallationStateResolver.Resolve(
            [Installed("aggregate"), Installed("stale-record")],
            [root]);

        result.States.Single(state => state.Id == "stale-record").Should().Match<DotnetWorkloadInstallationState>(
            state => state.IsExplicit && state.CanUninstall && state.IsUserVisible);
        result.States.Single(state => state.Id == "missing-child").IsUserVisible.Should().BeFalse();
        result.Diagnostics.Should().Contain(message => message.Contains("stale-record", StringComparison.Ordinal));
        result.Diagnostics.Should().Contain(message => message.Contains("missing-child", StringComparison.Ordinal));
    }

    private static IReadOnlyList<ResolvedWorkloadDefinition> ResolveDefinitions(
        IReadOnlyList<WorkloadDefinition> workloads)
    {
        var manifest = new WorkloadManifest
        {
            Version = "1",
            Workloads = workloads.ToDictionary(workload => workload.Id),
            Packs = workloads
                .SelectMany(workload => workload.Packs)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    id => id,
                    id => new PackDefinition { Id = id, Version = "1", Kind = "sdk" })
        };
        return WorkloadGraphResolver.Resolve([manifest], "osx-arm64");
    }

    private static WorkloadDefinition Workload(
        string id,
        IReadOnlyList<string>? extends = null,
        IReadOnlyList<string>? packs = null,
        bool isAbstract = false) =>
        new()
        {
            Id = id,
            Extends = extends ?? [],
            Packs = packs ?? [],
            IsAbstract = isAbstract
        };

    private static DotnetInstalledWorkload Installed(string id) => new() { Id = id };

    private static DotnetWorkloadTarget Target() => new()
    {
        InstallRoot = "/tmp/dotnet",
        DotnetPath = "/tmp/dotnet/dotnet",
        Architecture = "arm64",
        FeatureBand = new SdkFeatureBand("10.0.302"),
        RepresentativeSdkVersion = "10.0.302"
    };
}
