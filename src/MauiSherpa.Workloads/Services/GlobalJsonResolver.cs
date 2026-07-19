using System.Text.Json;
using MauiSherpa.Workloads.Models;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Default <see cref="IGlobalJsonResolver"/>. Walks up from a folder to the nearest
/// <c>global.json</c> and derives the dotnetup channel from <c>sdk.version</c> +
/// <c>sdk.rollForward</c> using the documented mapping. No network or process calls.
/// </summary>
public class GlobalJsonResolver : IGlobalJsonResolver
{
    /// <inheritdoc />
    public GlobalJsonResolution Resolve(string folderPath)
    {
        var start = NormalizeStartDirectory(folderPath);

        var globalJsonPath = FindGlobalJson(start);
        if (globalJsonPath == null)
        {
            return new GlobalJsonResolution
            {
                FolderPath = start,
                Status = GlobalJsonStatus.NoGlobalJson,
            };
        }

        string? version = null;
        string? rollForward = null;
        string? workloadVersion = null;
        bool? allowPrerelease = null;
        var usesLegacyWorkloadSetProperty = false;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(globalJsonPath), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("sdk", out var sdk) &&
                sdk.ValueKind == JsonValueKind.Object)
            {
                if (sdk.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                    version = v.GetString();
                if (sdk.TryGetProperty("rollForward", out var rf) && rf.ValueKind == JsonValueKind.String)
                    rollForward = rf.GetString();
                if (sdk.TryGetProperty("allowPrerelease", out var ap) &&
                    (ap.ValueKind == JsonValueKind.True || ap.ValueKind == JsonValueKind.False))
                    allowPrerelease = ap.GetBoolean();
                if (sdk.TryGetProperty("workloadVersion", out var workload) &&
                    workload.ValueKind == JsonValueKind.String)
                    workloadVersion = workload.GetString();
            }
            if (workloadVersion == null &&
                doc.RootElement.TryGetProperty("workloadSet", out var legacy) &&
                legacy.ValueKind == JsonValueKind.Object &&
                legacy.TryGetProperty("version", out var legacyVersion) &&
                legacyVersion.ValueKind == JsonValueKind.String)
            {
                workloadVersion = legacyVersion.GetString();
                usesLegacyWorkloadSetProperty = workloadVersion != null;
            }
        }
        catch
        {
            // Malformed global.json — treat as present-but-unusable.
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            return new GlobalJsonResolution
            {
                FolderPath = start,
                GlobalJsonPath = globalJsonPath,
                RollForward = rollForward,
                AllowPrerelease = allowPrerelease,
                WorkloadVersion = workloadVersion,
                UsesLegacyWorkloadSetProperty = usesLegacyWorkloadSetProperty,
                Status = GlobalJsonStatus.NoSdkVersion,
            };
        }

        var (channel, pinned) = DeriveChannel(version!, rollForward);

        return new GlobalJsonResolution
        {
            FolderPath = start,
            GlobalJsonPath = globalJsonPath,
            RequestedVersion = version,
            RollForward = string.IsNullOrWhiteSpace(rollForward) ? "latestPatch" : rollForward,
            AllowPrerelease = allowPrerelease,
            WorkloadVersion = workloadVersion,
            UsesLegacyWorkloadSetProperty = usesLegacyWorkloadSetProperty,
            Channel = channel,
            IsPinned = pinned,
            // Status is provisional; the caller upgrades it to Resolved/Unresolved after enrichment.
            Status = GlobalJsonStatus.Resolved,
        };
    }

    /// <summary>
    /// Maps a global.json <c>sdk.version</c> + <c>rollForward</c> to a dotnetup channel.
    /// Returns the channel string and whether it is pinned (never auto-updates).
    /// </summary>
    public static (string Channel, bool IsPinned) DeriveChannel(string version, string? rollForward)
    {
        var policy = (rollForward ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(policy))
            policy = "latestpatch";

        switch (policy)
        {
            case "latestpatch":
                return (FeatureBandChannel(version), false);
            case "latestfeature":
                return (MajorMinor(version), false);
            case "latestminor":
                return (Major(version), false);
            case "latestmajor":
                return ("latest", false);
            default:
                // disable / patch / feature / minor / major → pinned to the exact version.
                return (version.Trim(), true);
        }
    }

    private static string FeatureBandChannel(string version)
    {
        var (major, minor, third) = SplitVersion(version);
        // The feature band is the hundreds digit of the patch field (100 -> 1xx, 203 -> 2xx).
        var bandDigit = third / 100;
        return $"{major}.{minor}.{bandDigit}xx";
    }

    private static string MajorMinor(string version)
    {
        var (major, minor, _) = SplitVersion(version);
        return $"{major}.{minor}";
    }

    private static string Major(string version)
    {
        var (major, _, _) = SplitVersion(version);
        return major.ToString();
    }

    private static (int Major, int Minor, int Third) SplitVersion(string version)
    {
        var core = version.Trim();
        var dash = core.IndexOf('-');
        if (dash >= 0) core = core[..dash];

        var parts = core.Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        int third = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;
        return (major, minor, third);
    }

    private static string NormalizeStartDirectory(string folderPath)
    {
        var full = Path.GetFullPath(folderPath);
        if (File.Exists(full))
            return Path.GetDirectoryName(full) ?? full;
        return full;
    }

    private static string? FindGlobalJson(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "global.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
