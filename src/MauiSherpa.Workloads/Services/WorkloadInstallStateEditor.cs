using System.Text.Json;
using System.Text.Json.Nodes;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Makes targeted recovery edits to the per-feature-band workload install state.
/// </summary>
public static class WorkloadInstallStateEditor
{
    private const string WorkloadVersionProperty = "workloadVersion";

    /// <summary>
    /// Removes a recorded workload version whose workload set is missing from disk, keeping a
    /// backup of the install state and leaving every other value in place.
    /// </summary>
    /// <returns>True when the install state was changed.</returns>
    public static bool ClearMissingWorkloadVersion(
        DotnetWorkloadTarget target,
        string workloadVersion)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadVersion);

        if (!string.Equals(
                WorkloadInstallStateReader.ReadWorkloadVersion(target),
                workloadVersion,
                StringComparison.OrdinalIgnoreCase) ||
            WorkloadInstallStateReader.IsWorkloadSetInstalled(target, workloadVersion))
        {
            return false;
        }

        var path = WorkloadInstallStateReader.GetPath(target);
        var state = JsonNode.Parse(
            File.ReadAllText(path),
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) as JsonObject
            ?? throw new FormatException($"The workload install state in '{path}' is not an object.");
        state.Remove(WorkloadVersionProperty);

        File.Copy(path, $"{path}.bak-missing-{workloadVersion}", overwrite: true);

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, state.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, path, overwrite: true);
        return true;
    }
}
