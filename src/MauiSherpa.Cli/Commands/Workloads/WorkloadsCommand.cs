using System.CommandLine;
using MauiSherpa.Cli.Helpers;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Cli.Commands.Workloads;

public static class WorkloadsCommand
{
    public static Command Create()
    {
        var cmd = new Command("workloads", "Query installed .NET SDK workloads, manifests, and workload sets.\n\nUseful for diagnosing MAUI build issues and understanding SDK configuration.");
        cmd.Add(CreateListCommand());
        cmd.Add(CreateSetsCommand());
        cmd.Add(CreateInfoCommand());
        return cmd;
    }

    private static Command CreateListCommand()
    {
        var cmd = new Command("list", "List installed .NET workloads for the current SDK feature band.");
        var bandOpt = new Option<string?>("--band") { Description = "SDK feature band (e.g., '10.0.100'). Auto-detected if omitted." };
        cmd.Add(bandOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var band = parseResult.GetValue(bandOpt);
            var json = parseResult.GetValue(CliOptions.Json);
            var localSdk = new LocalSdkService();
            var featureBand = band ?? GetCurrentFeatureBand(localSdk);

            if (featureBand is null)
            {
                Output.WriteError("Could not detect .NET SDK feature band. Use --band to specify.");
                return;
            }

            var manifests = localSdk.GetInstalledWorkloadManifests(featureBand);

            if (json)
            {
                Output.WriteJson(new
                {
                    featureBand,
                    workloads = manifests.Select(m => new { manifestId = m }),
                });
                return;
            }

            Console.WriteLine($"Installed workloads for feature band {featureBand}:");
            Console.WriteLine();

            if (manifests.Count == 0)
            {
                Console.WriteLine("  No workloads installed.");
                return;
            }

            foreach (var m in manifests)
                Console.WriteLine($"  • {m}");
        });
        return cmd;
    }

    private static Command CreateSetsCommand()
    {
        var cmd = new Command("sets", "List available workload set versions from NuGet.");
        var bandOpt = new Option<string?>("--band") { Description = "SDK feature band (e.g., '10.0.100'). Auto-detected if omitted." };
        var previewOpt = new Option<bool>("--preview") { Description = "Include preview/prerelease versions" };
        var limitOpt = new Option<int>("--limit") { Description = "Maximum number of versions to show", DefaultValueFactory = (_) => 20 };
        cmd.Add(bandOpt);
        cmd.Add(previewOpt);
        cmd.Add(limitOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var band = parseResult.GetValue(bandOpt);
            var preview = parseResult.GetValue(previewOpt);
            var limit = parseResult.GetValue(limitOpt);
            var json = parseResult.GetValue(CliOptions.Json);
            var localSdk = new LocalSdkService();
            var featureBand = band ?? GetCurrentFeatureBand(localSdk);

            if (featureBand is null)
            {
                Output.WriteError("Could not detect .NET SDK feature band. Use --band to specify.");
                return;
            }

            var setService = new WorkloadSetService();
            var versions = await setService.GetAvailableWorkloadSetVersionsAsync(featureBand, includePrerelease: preview);

            var limited = versions.Take(limit).ToList();

            if (json)
            {
                Output.WriteJson(new
                {
                    featureBand,
                    versions = limited.Select(v => v.ToString()),
                    totalCount = versions.Count,
                });
                return;
            }

            Console.WriteLine($"Available workload sets for {featureBand} (showing {limited.Count} of {versions.Count}):");
            Console.WriteLine();

            foreach (var v in limited)
                Console.WriteLine($"  {v}");
        });
        return cmd;
    }

    private static Command CreateInfoCommand()
    {
        var cmd = new Command("info", "Show detailed .NET SDK and workload installation info as JSON.\n\nIncludes SDK versions, feature bands, installed manifests, and workload set details.");
        var detailedOpt = new Option<bool>("--detailed") { Description = "Include manifest details", DefaultValueFactory = (_) => true };
        cmd.Add(detailedOpt);
        cmd.SetAction(async (parseResult, ct) =>
        {
            var detailed = parseResult.GetValue(detailedOpt);
            var localSdk = new LocalSdkService();
            var jsonStr = await localSdk.GetInstalledSdkInfoAsJsonStringAsync(
                includeManifestDetails: detailed,
                indented: true);
            Console.WriteLine(jsonStr);
        });
        return cmd;
    }

    private static string? GetCurrentFeatureBand(LocalSdkService localSdk)
    {
        var versions = localSdk.GetInstalledSdkVersions();
        var latest = versions.OrderByDescending(v => v.Version).FirstOrDefault();
        return latest?.FeatureBand;
    }
}
