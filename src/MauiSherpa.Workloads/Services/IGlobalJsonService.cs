namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Information parsed from a global.json file.
/// </summary>
public record GlobalJsonInfo(
    /// <summary>Full path to the global.json file.</summary>
    string Path,
    /// <summary>Pinned SDK version (sdk.version).</summary>
    string? SdkVersion,
    /// <summary>Roll-forward policy (sdk.rollForward).</summary>
    string? RollForward,
    /// <summary>Pinned workload set version (workloadSet.version).</summary>
    string? WorkloadSetVersion,
    /// <summary>MSBuild SDK versions (msbuild-sdks).</summary>
    IReadOnlyDictionary<string, string>? MsBuildSdks
);

/// <summary>
/// Service for finding and parsing global.json files.
/// </summary>
public interface IGlobalJsonService
{
    /// <summary>
    /// Finds a global.json file starting from the given directory and searching up to the root.
    /// </summary>
    /// <param name="startDirectory">Directory to start searching from. Defaults to current directory.</param>
    /// <returns>Full path to global.json if found, null otherwise.</returns>
    string? FindGlobalJson(string? startDirectory = null);

    /// <summary>
    /// Parses a global.json file at the given path.
    /// </summary>
    /// <param name="path">Path to the global.json file.</param>
    /// <returns>Parsed information, or null if file doesn't exist or is invalid.</returns>
    GlobalJsonInfo? ParseGlobalJson(string path);

    /// <summary>
    /// Finds and parses a global.json file starting from the given directory.
    /// </summary>
    /// <param name="startDirectory">Directory to start searching from. Defaults to current directory.</param>
    /// <returns>Parsed information, or null if no global.json found.</returns>
    GlobalJsonInfo? GetGlobalJson(string? startDirectory = null);

    /// <summary>
    /// Checks if a workload set version is pinned in global.json.
    /// </summary>
    /// <param name="startDirectory">Directory to start searching from. Defaults to current directory.</param>
    /// <returns>True if workloadSet.version is specified in global.json.</returns>
    bool IsWorkloadSetPinned(string? startDirectory = null);

    /// <summary>
    /// Gets the pinned workload set version from global.json if present.
    /// </summary>
    /// <param name="startDirectory">Directory to start searching from. Defaults to current directory.</param>
    /// <returns>The pinned version, or null if not pinned.</returns>
    string? GetPinnedWorkloadSetVersion(string? startDirectory = null);

    /// <summary>
    /// Checks if an SDK version is pinned in global.json.
    /// </summary>
    /// <param name="startDirectory">Directory to start searching from. Defaults to current directory.</param>
    /// <returns>True if sdk.version is specified in global.json.</returns>
    bool IsSdkVersionPinned(string? startDirectory = null);

    /// <summary>
    /// Gets the pinned SDK version from global.json if present.
    /// </summary>
    /// <param name="startDirectory">Directory to start searching from. Defaults to current directory.</param>
    /// <returns>The pinned version, or null if not pinned.</returns>
    string? GetPinnedSdkVersion(string? startDirectory = null);
}
