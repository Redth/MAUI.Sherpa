using System.Text.Json;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Service for finding and parsing global.json files.
/// </summary>
public class GlobalJsonService : IGlobalJsonService
{
    private const string GlobalJsonFileName = "global.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <inheritdoc />
    public string? FindGlobalJson(string? startDirectory = null)
    {
        var directory = startDirectory ?? Environment.CurrentDirectory;

        // Search up the directory tree
        var current = new DirectoryInfo(directory);
        while (current != null)
        {
            var globalJsonPath = Path.Combine(current.FullName, GlobalJsonFileName);
            if (File.Exists(globalJsonPath))
            {
                return globalJsonPath;
            }
            current = current.Parent;
        }

        return null;
    }

    /// <inheritdoc />
    public GlobalJsonInfo? ParseGlobalJson(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = document.RootElement;

            // Parse sdk section
            string? sdkVersion = null;
            string? rollForward = null;
            if (root.TryGetProperty("sdk", out var sdkElement))
            {
                if (sdkElement.TryGetProperty("version", out var versionElement))
                {
                    sdkVersion = versionElement.GetString();
                }
                if (sdkElement.TryGetProperty("rollForward", out var rollForwardElement))
                {
                    rollForward = rollForwardElement.GetString();
                }
            }

            // Parse workloadSet section (.NET 9+)
            string? workloadSetVersion = null;
            if (root.TryGetProperty("workloadSet", out var workloadSetElement))
            {
                if (workloadSetElement.TryGetProperty("version", out var wsVersionElement))
                {
                    workloadSetVersion = wsVersionElement.GetString();
                }
            }

            // Parse msbuild-sdks section
            Dictionary<string, string>? msbuildSdks = null;
            if (root.TryGetProperty("msbuild-sdks", out var msbuildSdksElement) &&
                msbuildSdksElement.ValueKind == JsonValueKind.Object)
            {
                msbuildSdks = new Dictionary<string, string>();
                foreach (var property in msbuildSdksElement.EnumerateObject())
                {
                    var value = property.Value.GetString();
                    if (value != null)
                    {
                        msbuildSdks[property.Name] = value;
                    }
                }
            }

            return new GlobalJsonInfo(
                Path: path,
                SdkVersion: sdkVersion,
                RollForward: rollForward,
                WorkloadSetVersion: workloadSetVersion,
                MsBuildSdks: msbuildSdks
            );
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public GlobalJsonInfo? GetGlobalJson(string? startDirectory = null)
    {
        var path = FindGlobalJson(startDirectory);
        if (path == null)
            return null;

        return ParseGlobalJson(path);
    }

    /// <inheritdoc />
    public bool IsWorkloadSetPinned(string? startDirectory = null)
    {
        var info = GetGlobalJson(startDirectory);
        return info?.WorkloadSetVersion != null;
    }

    /// <inheritdoc />
    public string? GetPinnedWorkloadSetVersion(string? startDirectory = null)
    {
        var info = GetGlobalJson(startDirectory);
        return info?.WorkloadSetVersion;
    }

    /// <inheritdoc />
    public bool IsSdkVersionPinned(string? startDirectory = null)
    {
        var info = GetGlobalJson(startDirectory);
        return info?.SdkVersion != null;
    }

    /// <inheritdoc />
    public string? GetPinnedSdkVersion(string? startDirectory = null)
    {
        var info = GetGlobalJson(startDirectory);
        return info?.SdkVersion;
    }
}
