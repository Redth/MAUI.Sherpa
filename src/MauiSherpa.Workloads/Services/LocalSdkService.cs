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
        _logger.LogDebug("Starting SDK path detection");
        
        // Try common installation paths
        var possiblePaths = GetPossibleDotNetPaths().ToList();
        _logger.LogDebug("Checking {Count} possible paths", possiblePaths.Count);

        foreach (var path in possiblePaths)
        {
            _logger.LogDebug("Checking path: {Path}", path);
            
            if (IsValidSdkPath(path))
            {
                _logger.LogInformation("Found valid SDK at: {Path}", path);
                return path;
            }
        }

        // Fallback: try to find dotnet executable and get its location
        try
        {
            _logger.LogDebug("Attempting fallback: locating dotnet executable");
            
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
                    _logger.LogDebug("Found dotnet executable at: {Path}", output);
                    
                    var dotnetDir = Path.GetDirectoryName(output);
                    if (dotnetDir != null && IsValidSdkPath(dotnetDir))
                    {
                        _logger.LogInformation("Found valid SDK via dotnet executable at: {Path}", dotnetDir);
                        return dotnetDir;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding dotnet executable");
        }

        _logger.LogWarning("No valid SDK path found");
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

    /// <summary>
    /// Validates that a path contains a valid .NET SDK installation.
    /// </summary>
    /// <param name="path">The path to validate.</param>
    /// <returns>True if the path contains a valid SDK installation, false otherwise.</returns>
    private bool IsValidSdkPath(string path)
    {
        if (!Directory.Exists(path))
        {
            _logger.LogDebug("Path does not exist: {Path}", path);
            return false;
        }

        var sdkPath = Path.Combine(path, "sdk");
        if (!Directory.Exists(sdkPath))
        {
            _logger.LogDebug("SDK directory does not exist: {Path}", sdkPath);
            return false;
        }

        // Check if sdk directory contains any valid SDK version directories
        try
        {
            var sdkDirs = Directory.GetDirectories(sdkPath);
            if (sdkDirs.Length == 0)
            {
                _logger.LogDebug("SDK directory is empty: {Path}", sdkPath);
                return false;
            }

            // Verify at least one directory is a valid SDK version
            var hasValidSdk = false;
            foreach (var dir in sdkDirs)
            {
                var versionDir = Path.GetFileName(dir);
                
                // Try to parse as an SDK version
                if (SdkVersion.TryParse(versionDir, out _))
                {
                    // Additional validation: check for key SDK files
                    var dotnetDll = Path.Combine(dir, "dotnet.dll");
                    if (File.Exists(dotnetDll))
                    {
                        _logger.LogDebug("Found valid SDK version: {Version} at {Path}", versionDir, dir);
                        hasValidSdk = true;
                        break;
                    }
                }
            }

            if (!hasValidSdk)
            {
                _logger.LogDebug("No valid SDK versions found in: {Path}", sdkPath);
            }

            return hasValidSdk;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error validating SDK path: {Path}", path);
            return false;
        }
    }

    /// <summary>
    /// Returns possible .NET SDK install paths in priority order based on the official
    /// dotnet/designs install-locations specification:
    /// https://github.com/dotnet/designs/blob/main/accepted/2020/install-locations.md
    /// </summary>
    private static IEnumerable<string> GetPossibleDotNetPaths()
    {
        // 1. DOTNET_ROOT env vars — highest priority (explicit user override)
        foreach (var envPath in GetDotNetRootFromEnvironment())
        {
            yield return envPath;
        }
        
        // 2. Registered install locations (macOS/Linux: /etc/dotnet/install_location files,
        //    Windows: registry) — the official mechanism for custom global installs
        foreach (var registeredPath in GetRegisteredInstallLocations())
        {
            yield return registeredPath;
        }
        
        // 3. Default global install locations (platform-specific)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles))
                yield return Path.Combine(programFiles, "dotnet");
            
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFilesX86))
                yield return Path.Combine(programFilesX86, "dotnet");
            
            // VS private install location
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                yield return Path.Combine(localAppData, "Microsoft", "dotnet");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || OperatingSystem.IsMacCatalyst())
        {
            yield return "/usr/local/share/dotnet";
            // x64-on-arm64 via Rosetta (the .pkg installer registers this in install_location)
            yield return "/usr/local/share/dotnet/x64";
            yield return "/opt/homebrew/opt/dotnet/libexec";
        }
        else // Linux
        {
            yield return "/usr/share/dotnet";
            yield return "/usr/local/share/dotnet";
            yield return "/usr/lib/dotnet"; // Fedora/RHEL
        }
        
        // 4. Project-local .dotnet installation (repo-local SDK pinning)
        yield return Path.Combine(Environment.CurrentDirectory, ".dotnet");
        
        // 5. User home .dotnet — lowest priority; typically just cache/settings,
        //    only a real SDK if installed via dotnet-install.sh --install-dir ~/.dotnet
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
            yield return Path.Combine(home, ".dotnet");
    }

    /// <summary>
    /// Reads registered install locations from platform-specific configuration.
    /// macOS/Linux: /etc/dotnet/install_location_&lt;arch&gt; and /etc/dotnet/install_location
    /// Windows: HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\&lt;arch&gt;\InstallLocation
    /// </summary>
    private static IEnumerable<string> GetRegisteredInstallLocations()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: read from registry (32-bit view)
            foreach (var path in GetWindowsRegistryInstallLocation())
                yield return path;
        }
        else
        {
            // macOS/Linux: read from /etc/dotnet/install_location files
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                Architecture.X86 => "x86",
                _ => null
            };

            // Per-architecture file first (higher priority)
            if (arch != null)
            {
                var archFile = $"/etc/dotnet/install_location_{arch}";
                var archPath = ReadInstallLocationFile(archFile);
                if (archPath != null)
                    yield return archPath;
            }

            // Generic install_location file
            var genericPath = ReadInstallLocationFile("/etc/dotnet/install_location");
            if (genericPath != null)
                yield return genericPath;
        }
    }

    /// <summary>
    /// Reads the first line of an install_location file, which contains the absolute
    /// path to the .NET install root.
    /// </summary>
    private static string? ReadInstallLocationFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;
            
            var line = File.ReadLines(filePath).FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(line) ? null : line;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the .NET install location from the Windows registry.
    /// </summary>
    private static IEnumerable<string> GetWindowsRegistryInstallLocation()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return [];

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => null
        };

        if (arch == null)
            return [];

        try
        {
            // HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\<arch>\InstallLocation
            // Must use 32-bit registry view per the design spec
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\dotnet\Setup\InstalledVersions\{arch}");
            var value = key?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrEmpty(value))
                return [value];
        }
        catch
        {
            // Registry access may fail on non-Windows or without permissions
        }

        return [];
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
