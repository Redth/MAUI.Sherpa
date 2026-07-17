using FluentAssertions;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Workloads.Tests.Services;

public class WorkloadGraphResolverTests
{
    [Fact]
    public void ResolvesAggregateIncludesAndRidPackAliases()
    {
        const string json = """
        {
          "version": "10.0.20",
          "workloads": {
            "maui": { "extends": [ "maui-mobile" ] },
            "maui-mobile": { "extends": [ "maui-android" ] },
            "maui-android": { "packs": [ "Maui.Sdk" ], "extends": [ "android" ] },
            "android": { "packs": [ "Android.Sdk" ] }
          },
          "packs": {
            "Maui.Sdk": {
              "version": "10.0.20",
              "kind": "sdk",
              "alias-to": { "osx-arm64": "Maui.Sdk.Mac", "any": "Maui.Sdk.Any" }
            },
            "Android.Sdk": { "version": "36.1.53", "kind": "sdk" }
          }
        }
        """;
        var manifest = WorkloadManifestService.ParseManifest(json)!;

        var result = WorkloadGraphResolver.Resolve([manifest], "osx-arm64");
        var maui = result.Single(item => item.Id == "maui");

        maui.TransitiveIncludes.Should().BeEquivalentTo("maui-mobile", "maui-android", "android");
        maui.Packs.Should().HaveCount(2);
        maui.Packs.Single(pack => pack.Id == "Maui.Sdk").ResolvedPackageId.Should().Be("Maui.Sdk.Mac");
    }

    [Fact]
    public void RejectsExtendsCycles()
    {
        var manifest = new WorkloadManifest
        {
            Version = "1",
            Workloads = new Dictionary<string, WorkloadDefinition>
            {
                ["a"] = new() { Id = "a", Extends = ["b"] },
                ["b"] = new() { Id = "b", Extends = ["a"] }
            }
        };

        var action = () => WorkloadGraphResolver.Resolve([manifest], "any");

        action.Should().Throw<InvalidDataException>().WithMessage("*cycle*");
    }

    [Fact]
    public void RedirectsUseTheReplacementWorkloadGraph()
    {
        var manifest = new WorkloadManifest
        {
            Version = "1",
            Workloads = new Dictionary<string, WorkloadDefinition>
            {
                ["legacy"] = new() { Id = "legacy", RedirectTo = "current" },
                ["current"] = new() { Id = "current", Extends = ["base"], Packs = ["Current.Sdk"] },
                ["base"] = new() { Id = "base", Packs = ["Base.Sdk"] }
            },
            Packs = new Dictionary<string, PackDefinition>
            {
                ["Current.Sdk"] = new() { Id = "Current.Sdk", Version = "1", Kind = "sdk" },
                ["Base.Sdk"] = new() { Id = "Base.Sdk", Version = "1", Kind = "sdk" }
            }
        };

        var result = WorkloadGraphResolver.Resolve([manifest], "any");
        var legacy = result.Single(workload => workload.Id == "legacy");

        legacy.RedirectTarget.Should().Be("current");
        legacy.TransitiveIncludes.Should().BeEquivalentTo("current", "base");
        legacy.Packs.Select(pack => pack.Id).Should().BeEquivalentTo("Current.Sdk", "Base.Sdk");
    }

    [Fact]
    public void RejectsRedirectCycles()
    {
        var manifest = new WorkloadManifest
        {
            Version = "1",
            Workloads = new Dictionary<string, WorkloadDefinition>
            {
                ["a"] = new() { Id = "a", RedirectTo = "b" },
                ["b"] = new() { Id = "b", RedirectTo = "a" }
            }
        };

        var action = () => WorkloadGraphResolver.Resolve([manifest], "any");

        action.Should().Throw<InvalidDataException>().WithMessage("*redirect cycle*");
    }

    [Fact]
    public void RejectsMissingRedirectTargets()
    {
        var manifest = new WorkloadManifest
        {
            Version = "1",
            Workloads = new Dictionary<string, WorkloadDefinition>
            {
                ["legacy"] = new() { Id = "legacy", RedirectTo = "missing" }
            }
        };

        var action = () => WorkloadGraphResolver.Resolve([manifest], "any");

        action.Should().Throw<InvalidDataException>().WithMessage("*missing workload*");
    }
}
