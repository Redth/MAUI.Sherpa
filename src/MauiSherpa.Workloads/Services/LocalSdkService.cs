using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using MauiSherpa.Workloads.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;

namespace MauiSherpa.Workloads.Services;

/// <summary>
/// Service for inspecting the local .NET SDK installation.
/// </summary>
public class LocalSdkService : ILocalSdkService
{
    private readonly ILogger<LocalSdkService> _logger;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public LocalSdkService() : this(NullLogger<LocalSdkService>.Instance) { }
    
    public LocalSdkService(ILogger<LocalSdkService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string? GetDotNetSdkPath()
    {
        // Try common installation paths
        var possiblePaths = GetPossibleDotNetPaths();

        foreach (var path in possiblePaths)
        {
            if (Directory.Exists(path) && Directory.Exists(Path.Combine(path, "sdk")))
                return path;
        }

        // Fallback: try to find dotnet executable and get its location
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which",
                Arguments = "dotnet",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadLine();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(output))
                {
                    var dotnetDir = Path.GetDirectoryName(output);
                    if (dotnetDir != null && Directory.Exists(Path.Combine(dotnetDir, "sdk")))
                        return dotnetDir;
                }
            }
        }
        catch
        {
            // Ignore errors finding dotnet
        }

        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<SdkVersion> GetInstalledSdkVersions()
    {
        var dotnetPath = GetDotNetSdkPath();
        if (dotnetPath == null)
            return [];

        var sdkPath = Path.Combine(dotnetPath, "sdk");
        if (!Directory.Exists(sdkPath))
            return [];

        var versions = new List<SdkVersion>();

        foreach (var dir in Directory.GetDirectories(sdkPath))
        {
            var versionDir = Path.GetFileName(dir);
            try
            {
                var sdkVersion = SdkVersion.Parse(versionDir);
                versions.Add(sdkVersion);
            }
            catch
            {
                // Skip directories that aren't valid SDK versions
            }
        }

        return versions
            .OrderByDescending(v => v.Major)
            .ThenByDescending(v => v.Minor)
            .ThenByDescending(v => v.Patch)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetInstalledWorkloadManifests(string featureBand)
    {
        var manifestsPath = GetManifestsPath(featureBand);
        if (manifestsPath == null || !Directory.Exists(manifestsPath))
            return [];

        return Directory.GetDirectories(manifestsPath)
            .Select(Path.GetFileName)
            .Where(name => name != null)
            .Cast<string>()
            .ToList();
    }

    /// <inheritdoc />
    public async Task<WorkloadManifest?> GetInstalledManifestAsync(string featureBand, string manifestId, CancellationToken cancellationToken = default)
    {
        var manifestsPath = GetManifestsPath(featureBand);
        if (manifestsPath == null)
            return null;

        // Manifest directories can have version subdirectories
        var manifestDir = Path.Combine(manifestsPath, manifestId);
        if (!Directory.Exists(manifestDir))
        {
            // Try lowercase
            manifestDir = Path.Combine(manifestsPath, manifestId.ToLowerInvariant());
            if (!Directory.Exists(manifestDir))
                return null;
        }

        // Look for WorkloadManifest.json, possibly in a version subdirectory
        var manifestFile = FindManifestFile(manifestDir);
        if (manifestFile == null)
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(manifestFile, cancellationToken);
            return ParseManifest(json);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<WorkloadSet?> GetInstalledWorkloadSetAsync(string featureBand, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("GetInstalledWorkloadSetAsync called with featureBand: {FeatureBand}", featureBand);
        
        var dotnetPath = GetDotNetSdkPath();
        if (dotnetPath == null)
        {
            _logger.LogDebug("dotnetPath is null");
            return null;
        }

        // Workload sets are stored in sdk-manifests/{band}/workloadsets/{version}/
        var workloadSetsPath = Path.Combine(dotnetPath, "sdk-manifests", featureBand, "workloadsets");
        _logger.LogDebug("Looking in: {Path}", workloadSetsPath);
        
        if (!Directory.Exists(workloadSetsPath))
        {
            _logger.LogDebug("Directory does not exist");
            return null;
        }

        // Get the latest version directory using proper version comparison
        var allDirs = Directory.GetDirectories(workloadSetsPath);
        _logger.LogDebug("Found dirs: {Dirs}", string.Join(", ", allDirs.Select(Path.GetFileName)));
        
        var versionDirs = allDirs
            .Select(d => new { Path = d, Name = Path.GetFileName(d) })
            .Where(d => NuGetVersion.TryParse(d.Name, out _))
            .OrderByDescending(d => NuGetVersion.Parse(d.Name))
            .Select(d => d.Path)
            .ToList();
        
        _logger.LogDebug("Sorted dirs: {Dirs}", string.Join(", ", versionDirs.Select(Path.GetFileName)));

        if (versionDirs.Count == 0)
            return null;

        // Find the workload set file - it can have different names
        var workloadSetFile = Path.Combine(versionDirs[0], "WorkloadSet.json");
        if (!File.Exists(workloadSetFile))
        {
            workloadSetFile = Path.Combine(versionDirs[0], "workloadset.json");
            if (!File.Exists(workloadSetFile))
            {
                // Also check for microsoft.net.workloads.workloadset.json (newer format)
                workloadSetFile = Path.Combine(versionDirs[0], "microsoft.net.workloads.workloadset.json");
                if (!File.Exists(workloadSetFile))
                {
                    _logger.LogDebug("No workload set file found in {Dir}", versionDirs[0]);
                    return null;
                }
            }
        }

        try
        {
            var json = await File.ReadAllTextAsync(workloadSetFile, cancellationToken);
            var workloads = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            if (workloads == null)
                return null;

            var entries = new Dictionary<string, WorkloadSetEntry>();
            foreach (var (workloadId, value) in workloads)
            {
                var parts = value.Split('/');
                if (parts.Length >= 2)
                {
                    entries[workloadId] = new WorkloadSetEntry
                    {
                        ManifestId = parts[0],
                        ManifestVersion = parts[1],
                        ManifestFeatureBand = parts.Length >= 3 ? parts[2] : null
                    };
                }
            }

            var version = Path.GetFileName(versionDirs[0]);
            _logger.LogInformation("Returning WorkloadSet version: {Version}", version);
            
            return new WorkloadSet
            {
                Version = version,
                FeatureBand = featureBand,
                Workloads = entries
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception reading workload set");
            return null;
        }
    }

    private static IEnumerable<string> GetPossibleDotNetPaths()
    {
        // Check environment variables for explicit dotnet root path (highest priority)
        foreach (var envPath in GetDotNetRootFromEnvironment())
        {
            yield return envPath;
        }
        
        // Check current working directory for local .dotnet installation (all platforms)
        yield return Path.Combine(Environment.CurrentDirectory, ".dotnet");
        
        // Check user's home directory for .dotnet installation (all platforms)
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
            yield return Path.Combine(home, ".dotnet");
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles))
                yield return Path.Combine(programFiles, "dotnet");
            
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFilesX86))
                yield return Path.Combine(programFilesX86, "dotnet");
            
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(localAppData, "Microsoft", "dotnet");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/usr/local/share/dotnet";
            yield return "/opt/homebrew/opt/dotnet/libexec";
        }
        else // Linux
        {
            yield return "/usr/share/dotnet";
            yield return "/usr/local/share/dotnet";
        }
    }

    private static IEnumerable<string> GetDotNetRootFromEnvironment()
    {
        // Check architecture-specific environment variables first
        var arch = RuntimeInformation.ProcessArchitecture;
        
        // DOTNET_ROOT_<ARCH> - architecture-specific root
        var archSpecificVar = arch switch
        {
            Architecture.X64 => "DOTNET_ROOT_X64",
            Architecture.X86 => "DOTNET_ROOT_X86",
            Architecture.Arm64 => "DOTNET_ROOT_ARM64",
            Architecture.Arm => "DOTNET_ROOT_ARM",
            _ => null
        };
        
        if (archSpecificVar != null)
        {
            var archPath = Environment.GetEnvironmentVariable(archSpecificVar);
            if (!string.IsNullOrEmpty(archPath))
                yield return archPath;
        }
        
        // DOTNET_ROOT(x86) - for 32-bit processes on 64-bit Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && arch == Architecture.X86)
        {
            var x86Path = Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)");
            if (!string.IsNullOrEmpty(x86Path))
                yield return x86Path;
        }
        
        // DOTNET_ROOT - the primary/fallback environment variable
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
            yield return dotnetRoot;
    }

    private string? GetManifestsPath(string featureBand)
    {
        var dotnetPath = GetDotNetSdkPath();
        if (dotnetPath == null)
            return null;

        return Path.Combine(dotnetPath, "sdk-manifests", featureBand);
    }

    private static string? FindManifestFile(string manifestDir)
    {
        // Check directly in manifest directory
        var directFile = Path.Combine(manifestDir, "WorkloadManifest.json");
        if (File.Exists(directFile))
            return directFile;

        directFile = Path.Combine(manifestDir, "workloadmanifest.json");
        if (File.Exists(directFile))
            return directFile;

        // Check in version subdirectories
        foreach (var subDir in Directory.GetDirectories(manifestDir).OrderByDescending(d => d))
        {
            var subFile = Path.Combine(subDir, "WorkloadManifest.json");
            if (File.Exists(subFile))
                return subFile;

            subFile = Path.Combine(subDir, "workloadmanifest.json");
            if (File.Exists(subFile))
                return subFile;
        }

        return null;
    }

    private static WorkloadManifest? ParseManifest(string json)
    {
        var manifestJson = JsonSerializer.Deserialize<WorkloadManifestJson>(json, JsonOptions);
        if (manifestJson == null)
            return null;

        var workloads = manifestJson.Workloads?
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToModel(kvp.Key))
            ?? new Dictionary<string, WorkloadDefinition>();

        var packs = manifestJson.Packs?
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToModel(kvp.Key))
            ?? new Dictionary<string, PackDefinition>();

        return new WorkloadManifest
        {
            Version = manifestJson.Version ?? "",
            Description = manifestJson.Description,
            DependsOn = manifestJson.DependsOn ?? new Dictionary<string, string>(),
            Workloads = workloads,
            Packs = packs
        };
    }

    /// <inheritdoc />
    public async Task<JsonDocument> GetInstalledSdkInfoAsJsonAsync(bool includeManifestDetails = true, CancellationToken cancellationToken = default)
    {
        var jsonString = await GetInstalledSdkInfoAsJsonStringAsync(includeManifestDetails, indented: false, cancellationToken);
        return JsonDocument.Parse(jsonString);
    }

    /// <inheritdoc />
    public async Task<string> GetInstalledSdkInfoAsJsonStringAsync(bool includeManifestDetails = true, bool indented = false, CancellationToken cancellationToken = default)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var info = await BuildSdkInfoAsync(includeManifestDetails, cancellationToken);
        return JsonSerializer.Serialize(info, options);
    }

    private async Task<object> BuildSdkInfoAsync(bool includeManifestDetails, CancellationToken cancellationToken)
    {
        var dotnetPath = GetDotNetSdkPath();
        var installedSdks = GetInstalledSdkVersions();

        // Group SDKs by major version and get newest feature band for each
        var featureBandsByMajor = installedSdks
            .GroupBy(sdk => sdk.Major)
            .Select(g => g
                .OrderByDescending(sdk => sdk.Minor)
                .ThenByDescending(sdk => sdk.Patch)
                .First())
            .OrderByDescending(sdk => sdk.Major)
            .ToList();

        var sdkInfoList = new List<object>();

        foreach (var sdk in featureBandsByMajor)
        {
            var manifestIds = GetInstalledWorkloadManifests(sdk.FeatureBand);
            var workloadSet = await GetInstalledWorkloadSetAsync(sdk.FeatureBand, cancellationToken);

            var manifests = new List<object>();
            foreach (var manifestId in manifestIds)
            {
                if (manifestId.Equals("workloadsets", StringComparison.OrdinalIgnoreCase))
                    continue;

                var manifest = await GetInstalledManifestAsync(sdk.FeatureBand, manifestId, cancellationToken);
                if (manifest == null)
                {
                    manifests.Add(new { id = manifestId, error = "Could not parse manifest" });
                    continue;
                }

                if (includeManifestDetails)
                {
                    var workloads = manifest.Workloads.Select(w => new
                    {
                        id = w.Key,
                        description = w.Value.Description,
                        isAbstract = w.Value.IsAbstract,
                        kind = w.Value.Kind,
                        packs = w.Value.Packs,
                        extends = w.Value.Extends,
                        platforms = w.Value.Platforms.Count > 0 ? w.Value.Platforms : null,
                        redirectTo = w.Value.RedirectTo
                    }).ToList();

                    var packs = manifest.Packs.Select(p => new
                    {
                        id = p.Key,
                        version = p.Value.Version,
                        kind = p.Value.Kind,
                        aliasTo = p.Value.AliasTo
                    }).ToList();

                    manifests.Add(new
                    {
                        id = manifestId,
                        version = manifest.Version,
                        description = manifest.Description,
                        dependsOn = manifest.DependsOn.Count > 0 ? manifest.DependsOn : null,
                        workloads,
                        packs
                    });
                }
                else
                {
                    var concreteWorkloads = manifest.Workloads
                        .Where(w => !w.Value.IsAbstract)
                        .Select(w => w.Key)
                        .ToList();

                    manifests.Add(new
                    {
                        id = manifestId,
                        version = manifest.Version,
                        workloadCount = manifest.Workloads.Count,
                        concreteWorkloads,
                        packCount = manifest.Packs.Count
                    });
                }
            }

            object? workloadSetInfo = null;
            if (workloadSet != null)
            {
                workloadSetInfo = new
                {
                    version = workloadSet.Version,
                    workloads = workloadSet.Workloads.ToDictionary(
                        w => w.Key,
                        w => new
                        {
                            manifestId = w.Value.ManifestId,
                            manifestVersion = w.Value.ManifestVersion,
                            manifestFeatureBand = w.Value.ManifestFeatureBand
                        })
                };
            }

            sdkInfoList.Add(new
            {
                majorVersion = sdk.Major,
                featureBand = sdk.FeatureBand,
                latestInstalledVersion = sdk.Version,
                runtimeVersion = sdk.RuntimeVersion,
                isPreview = sdk.IsPreview,
                workloadSet = workloadSetInfo,
                manifests
            });
        }

        return new
        {
            dotnetPath,
            timestamp = DateTime.UtcNow.ToString("O"),
            totalInstalledSdks = installedSdks.Count,
            allInstalledVersions = installedSdks.Select(s => s.Version).ToList(),
            sdksByMajorVersion = sdkInfoList
        };
    }
}
