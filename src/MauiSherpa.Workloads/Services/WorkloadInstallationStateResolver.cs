using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

public static class WorkloadInstallationStateResolver
{
    public static DotnetWorkloadInstallationResolution Resolve(
        IEnumerable<DotnetInstalledWorkload> installedWorkloads,
        IEnumerable<ResolvedWorkloadDefinition> availableWorkloads)
    {
        ArgumentNullException.ThrowIfNull(installedWorkloads);
        ArgumentNullException.ThrowIfNull(availableWorkloads);

        var definitions = availableWorkloads
            .Where(workload => !string.IsNullOrWhiteSpace(workload.Id))
            .GroupBy(workload => workload.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.OrdinalIgnoreCase);
        var records = installedWorkloads
            .Where(workload => !string.IsNullOrWhiteSpace(workload.Id))
            .GroupBy(workload => workload.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var includedBy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new HashSet<string>(StringComparer.Ordinal);

        foreach (var record in records.Values)
        {
            if (!definitions.TryGetValue(record.Id, out var definition))
            {
                diagnostics.Add(
                    $"Installed workload '{record.Id}' is not defined by the active workload manifests.");
                continue;
            }

            foreach (var includedId in definition.TransitiveIncludes)
            {
                if (string.Equals(includedId, record.Id, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!includedBy.TryGetValue(includedId, out var parents))
                {
                    parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    includedBy[includedId] = parents;
                }
                parents.Add(record.Id);

                if (!definitions.ContainsKey(includedId))
                {
                    diagnostics.Add(
                        $"Workload '{includedId}', included by '{record.Id}', is not defined by the active workload manifests.");
                }
            }
        }

        var stateIds = records.Keys
            .Concat(includedBy.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);
        var states = stateIds
            .Select(id =>
            {
                records.TryGetValue(id, out var record);
                definitions.TryGetValue(id, out var definition);
                includedBy.TryGetValue(id, out var parents);

                return new DotnetWorkloadInstallationState
                {
                    Id = id,
                    Kind = record == null
                        ? DotnetWorkloadInstallKind.Included
                        : DotnetWorkloadInstallKind.Explicit,
                    InstallationRecord = record,
                    Definition = definition,
                    IncludedBy = parents?
                        .OrderBy(parent => parent, StringComparer.OrdinalIgnoreCase)
                        .ToList() ?? []
                };
            })
            .ToList();

        return new DotnetWorkloadInstallationResolution
        {
            States = states,
            Diagnostics = diagnostics
                .OrderBy(message => message, StringComparer.Ordinal)
                .ToList()
        };
    }
}
