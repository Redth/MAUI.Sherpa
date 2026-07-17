using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

public static class WorkloadGraphResolver
{
    public static IReadOnlyList<ResolvedWorkloadDefinition> Resolve(
        IEnumerable<WorkloadManifest> manifests,
        string runtimeIdentifier)
    {
        var workloads = new Dictionary<string, WorkloadDefinition>(StringComparer.OrdinalIgnoreCase);
        var packs = new Dictionary<string, PackDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifest in manifests)
        {
            foreach (var workload in manifest.Workloads)
                workloads[workload.Key] = workload.Value;
            foreach (var pack in manifest.Packs)
                packs[pack.Key] = pack.Value;
        }

        return workloads.Values
            .Select(workload => ResolveWorkload(workload, workloads, packs, runtimeIdentifier))
            .OrderBy(workload => workload.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ResolvedWorkloadDefinition ResolveWorkload(
        WorkloadDefinition workload,
        IReadOnlyDictionary<string, WorkloadDefinition> workloads,
        IReadOnlyDictionary<string, PackDefinition> packs,
        string runtimeIdentifier)
    {
        var includes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packIds = new HashSet<string>(workload.Packs, StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ResolveIncludes(workload.Id, workloads, includes, packIds, visiting);
        includes.Remove(workload.Id);

        return new ResolvedWorkloadDefinition
        {
            Id = workload.Id,
            Description = workload.Description,
            IsAbstract = workload.IsAbstract,
            Kind = workload.Kind,
            Platforms = workload.Platforms,
            DirectExtends = workload.Extends,
            TransitiveIncludes = includes.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
            Packs = packIds
                .Select(id => packs.TryGetValue(id, out var pack)
                    ? ResolvePack(pack, runtimeIdentifier)
                    : new ResolvedPackDefinition { Id = id, Version = string.Empty, Kind = "Unknown" })
                .OrderBy(pack => pack.Id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            RedirectTarget = workload.RedirectTo
        };
    }

    private static void ResolveIncludes(
        string id,
        IReadOnlyDictionary<string, WorkloadDefinition> workloads,
        ISet<string> includes,
        ISet<string> packIds,
        ISet<string> visiting)
    {
        if (!workloads.TryGetValue(id, out var workload))
            return;
        if (!visiting.Add(id))
            throw new InvalidDataException($"Workload manifest contains an extends cycle at '{id}'.");

        foreach (var pack in workload.Packs)
            packIds.Add(pack);
        foreach (var extended in workload.Extends)
        {
            includes.Add(extended);
            ResolveIncludes(extended, workloads, includes, packIds, visiting);
        }

        visiting.Remove(id);
    }

    private static ResolvedPackDefinition ResolvePack(PackDefinition pack, string runtimeIdentifier)
    {
        string? resolved = null;
        if (pack.AliasToByPlatform != null)
        {
            pack.AliasToByPlatform.TryGetValue(runtimeIdentifier, out resolved);
            if (resolved == null)
            {
                var os = runtimeIdentifier.Split('-', 2)[0];
                pack.AliasToByPlatform.TryGetValue(os, out resolved);
            }
            if (resolved == null)
                pack.AliasToByPlatform.TryGetValue("any", out resolved);
        }

        return new ResolvedPackDefinition
        {
            Id = pack.Id,
            Version = pack.Version,
            Kind = pack.Kind,
            ResolvedPackageId = resolved ?? pack.AliasTo
        };
    }
}
