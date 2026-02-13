using System.Diagnostics;
using System.Runtime.InteropServices;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.NuGet;
using MauiSherpa.Workloads.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Service for checking MAUI development environment health.
/// Uses MauiSherpa.Workloads library for SDK/workload discovery.
/// </summary>
public class DoctorService : IDoctorService
{
    private readonly IAndroidSdkService _androidSdkService;
    private readonly ILoggingService _loggingService;
    private readonly IOpenJdkSettingsService _jdkSettingsService;
    private readonly ILogger<DoctorService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    
    // MauiSherpa.Workloads services - instantiated on demand
    private LocalSdkService? _localSdkService;
    private GlobalJsonService? _globalJsonService;
    private NuGetClient? _nugetClient;
    private WorkloadSetService? _workloadSetService;
    private WorkloadManifestService? _manifestService;
    private SdkVersionService? _sdkVersionService;
    
    public DoctorService(IAndroidSdkService androidSdkService, ILoggingService loggingService, IOpenJdkSettingsService jdkSettingsService, ILoggerFactory? loggerFactory = null)
    {
        _androidSdkService = androidSdkService;
        _loggingService = loggingService;
        _jdkSettingsService = jdkSettingsService;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<DoctorService>();
    }
    
    private LocalSdkService GetLocalSdkService() => _localSdkService ??= new LocalSdkService(_loggerFactory.CreateLogger<LocalSdkService>());
    private GlobalJsonService GetGlobalJsonService() => _globalJsonService ??= new GlobalJsonService();
    private NuGetClient GetNuGetClient() => _nugetClient ??= new NuGetClient();
    private WorkloadSetService GetWorkloadSetService() => _workloadSetService ??= new WorkloadSetService(GetNuGetClient());
    private WorkloadManifestService GetManifestService() => _manifestService ??= new WorkloadManifestService(GetNuGetClient());
    private SdkVersionService GetSdkVersionService() => _sdkVersionService ??= new SdkVersionService();
    
    // Mac Catalyst doesn't return true for RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
    private static bool IsMacPlatform => OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst();
    
    public async Task<DoctorContext> GetContextAsync(string? workingDirectory = null)
    {
        var globalJsonService = GetGlobalJsonService();
        var localSdkService = GetLocalSdkService();
        
        // Determine working directory
        var effectiveDir = workingDirectory ?? Environment.CurrentDirectory;
        
        // Check for global.json
        var globalJson = globalJsonService.GetGlobalJson(effectiveDir);
        
        // Get SDK path - LocalSdkService already checks for .dotnet/, DOTNET_ROOT, etc.
        // But we may want to look relative to workingDirectory first
        string? sdkPath = null;
        
        // Check for local .dotnet in working directory
        var localDotnet = Path.Combine(effectiveDir, ".dotnet");
        if (Directory.Exists(localDotnet) && Directory.Exists(Path.Combine(localDotnet, "sdk")))
        {
            sdkPath = localDotnet;
        }
        else
        {
            sdkPath = localSdkService.GetDotNetSdkPath();
        }
        
        // Determine effective feature band
        string? featureBand = null;
        bool isPreviewSdk = false;
        string? activeSdkVersion = null;
        string? resolvedSdkVersion = null;
        if (sdkPath != null)
        {
            var sdks = localSdkService.GetInstalledSdkVersions();
            if (sdks.Count > 0)
            {
                // If SDK is pinned, try to match that version's feature band
                if (globalJson?.SdkVersion != null)
                {
                    var pinned = sdks.FirstOrDefault(s => s.Version == globalJson.SdkVersion);
                    
                    // Resolve the effective SDK based on rollForward policy
                    var resolved = ResolveRollForward(globalJson.SdkVersion, globalJson.RollForward, sdks);
                    resolvedSdkVersion = resolved?.Version;
                    
                    var effectiveSdk = resolved ?? pinned ?? sdks[0];
                    featureBand = effectiveSdk.FeatureBand;
                    isPreviewSdk = effectiveSdk.IsPreview;
                    activeSdkVersion = effectiveSdk.Version;
                }
                else
                {
                    // Use the newest SDK's feature band
                    featureBand = sdks[0].FeatureBand;
                    isPreviewSdk = sdks[0].IsPreview;
                    activeSdkVersion = sdks[0].Version;
                }
            }
        }
        
        return new DoctorContext(
            WorkingDirectory: effectiveDir,
            DotNetSdkPath: sdkPath,
            GlobalJsonPath: globalJson?.Path,
            PinnedSdkVersion: globalJson?.SdkVersion,
            PinnedWorkloadSetVersion: globalJson?.WorkloadSetVersion,
            EffectiveFeatureBand: featureBand,
            IsPreviewSdk: isPreviewSdk,
            ActiveSdkVersion: activeSdkVersion,
            RollForwardPolicy: globalJson?.RollForward,
            ResolvedSdkVersion: resolvedSdkVersion
        );
    }
    
    /// <summary>
    /// Resolves the effective SDK version based on the rollForward policy from global.json.
    /// See: https://learn.microsoft.com/dotnet/core/tools/global-json#rollforward
    /// </summary>
    private static SdkVersion? ResolveRollForward(
        string pinnedVersion, string? rollForward, IReadOnlyList<SdkVersion> installedSdks)
    {
        if (!SdkVersion.TryParse(pinnedVersion, out var pinned) || pinned == null)
            return null;
        
        // If exact version is installed, that's always the answer
        var exact = installedSdks.FirstOrDefault(s => s.Version == pinnedVersion);
        if (exact != null)
            return exact;
        
        var policy = rollForward?.ToLowerInvariant() ?? "latestpatch"; // default is latestPatch
        
        // Filter candidates based on policy (sdks are sorted descending by version)
        var candidates = policy switch
        {
            "disable" => Enumerable.Empty<SdkVersion>(),
            
            "patch" or "latestpatch" =>
                // Same major.minor.featureband, latest patch
                installedSdks.Where(s =>
                    s.Major == pinned.Major && s.Minor == pinned.Minor
                    && s.FeatureBand == pinned.FeatureBand
                    && s.Patch >= pinned.Patch),
            
            "feature" or "latestfeature" =>
                // Same major.minor, any feature band >= pinned
                installedSdks.Where(s =>
                    s.Major == pinned.Major && s.Minor == pinned.Minor
                    && s.Patch >= pinned.Patch),
            
            "minor" or "latestminor" =>
                // Same major, any minor >= pinned
                installedSdks.Where(s =>
                    s.Major == pinned.Major
                    && (s.Minor > pinned.Minor
                        || (s.Minor == pinned.Minor && s.Patch >= pinned.Patch))),
            
            "major" or "latestmajor" =>
                // Any version >= pinned
                installedSdks.Where(s =>
                    s.Major > pinned.Major
                    || (s.Major == pinned.Major && s.Minor > pinned.Minor)
                    || (s.Major == pinned.Major && s.Minor == pinned.Minor && s.Patch >= pinned.Patch)),
            
            _ => Enumerable.Empty<SdkVersion>()
        };
        
        // First in the list is the best match (sorted descending)
        return candidates.FirstOrDefault();
    }
    
    public async Task<DoctorReport> RunDoctorAsync(DoctorContext? context = null, IProgress<string>? progress = null)
    {
        context ??= await GetContextAsync();
        
        progress?.Report("Checking .NET SDK installation...");
        
        var localSdkService = GetLocalSdkService();
        var dependencies = new List<DependencyStatus>();
        
        // Get installed SDKs
        var sdkVersions = localSdkService.GetInstalledSdkVersions();
        var sdkInfos = sdkVersions.Select(s => new SdkVersionInfo(
            s.Version, s.FeatureBand, s.Major, s.Minor, s.IsPreview
        )).ToList();
        
        // Get available SDK versions from releases feed
        List<SdkVersionInfo>? availableSdkVersions = null;
        try
        {
            progress?.Report("Checking available SDK versions...");
            var sdkVersionService = GetSdkVersionService();
            
            // Determine which major versions have preview SDKs installed
            var previewMajorVersions = new HashSet<int>(
                sdkVersions.Where(s => s.IsPreview).Select(s => s.Major));
            
            // Fetch all versions including previews, then filter:
            // - Always include stable versions
            // - Include preview versions only for major versions where user has a preview installed
            var available = await sdkVersionService.GetAvailableSdkVersionsAsync(
                includePreview: previewMajorVersions.Count > 0);
            
            availableSdkVersions = available
                .Where(s => !s.IsPreview || previewMajorVersions.Contains(s.Major))
                .Take(10)
                .Select(s => new SdkVersionInfo(s.Version, s.FeatureBand, s.Major, s.Minor, s.IsPreview))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to get available SDK versions: {ex.Message}");
        }
        
        // Check SDK status
        if (sdkVersions.Count == 0)
        {
            dependencies.Add(new DependencyStatus(
                ".NET SDK",
                DependencyCategory.DotNetSdk,
                null, null, null,
                DependencyStatusType.Error,
                "No .NET SDK found",
                IsFixable: false
            ));
        }
        else
        {
            var latestSdk = sdkVersions[0];
            
            if (latestSdk.IsPreview)
            {
                // Active SDK is a preview — find the latest available for the SAME major version
                var latestAvailableForMajor = availableSdkVersions?
                    .FirstOrDefault(s => s.Major == latestSdk.Major);
                var isLatestForMajor = latestAvailableForMajor == null 
                    || latestSdk.Version == latestAvailableForMajor.Version;
                
                // Add an informational status about being on a preview SDK
                dependencies.Add(new DependencyStatus(
                    ".NET SDK",
                    DependencyCategory.DotNetSdk,
                    null,
                    isLatestForMajor ? null : latestAvailableForMajor?.Version,
                    latestSdk.Version,
                    isLatestForMajor ? DependencyStatusType.Info : DependencyStatusType.Warning,
                    isLatestForMajor
                        ? $"Preview SDK ({latestSdk.Version})"
                        : $"Update available: {latestAvailableForMajor?.Version}",
                    IsFixable: false
                ));
            }
            else
            {
                var latestAvailable = availableSdkVersions?
                    .FirstOrDefault(s => !s.IsPreview);
                var isLatest = latestAvailable == null || latestSdk.Version == latestAvailable.Version;
                
                dependencies.Add(new DependencyStatus(
                    ".NET SDK",
                    DependencyCategory.DotNetSdk,
                    null,
                    latestAvailable?.Version,
                    latestSdk.Version,
                    isLatest ? DependencyStatusType.Ok : DependencyStatusType.Warning,
                    isLatest 
                        ? $"{sdkVersions.Count} SDK(s) installed, using {latestSdk.Version}"
                        : $"Update available: {latestAvailable?.Version}",
                    IsFixable: false
                ));
            }
        }
        
        // Get workload set and manifests
        string? workloadSetVersion = null;
        var manifests = new List<WorkloadManifestInfo>();
        IReadOnlyList<string>? availableWorkloadSets = null;
        
        if (context.EffectiveFeatureBand != null)
        {
            progress?.Report("Checking workload set...");
            _logger.LogInformation("Checking workload set for feature band: {FeatureBand}", context.EffectiveFeatureBand);
            
            var workloadSet = await localSdkService.GetInstalledWorkloadSetAsync(context.EffectiveFeatureBand);
            workloadSetVersion = workloadSet?.Version;
            _logger.LogInformation("Got workload set version: {Version}", workloadSetVersion ?? "NULL");
            
            // Get available workload set versions
            // Auto-enable prerelease when active SDK is a preview
            try
            {
                progress?.Report("Checking available workload updates...");
                availableWorkloadSets = await GetAvailableWorkloadSetVersionsAsync(
                    context.EffectiveFeatureBand, context.IsPreviewSdk);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to get available workload sets: {Message}", ex.Message);
            }
            
            // Check workload set status
            if (workloadSetVersion == null)
            {
                dependencies.Add(new DependencyStatus(
                    "Workload Set",
                    DependencyCategory.Workload,
                    null, null, null,
                    DependencyStatusType.Warning,
                    "No workload set installed (loose manifest mode)",
                    IsFixable: true,
                    FixAction: "install-workloads"
                ));
            }
            else
            {
                var isLatest = availableWorkloadSets?.Count > 0 && availableWorkloadSets[0] == workloadSetVersion;
                var latestAvailable = availableWorkloadSets?.FirstOrDefault();
                
                dependencies.Add(new DependencyStatus(
                    "Workload Set",
                    DependencyCategory.Workload,
                    null,
                    latestAvailable,
                    workloadSetVersion,
                    isLatest ? DependencyStatusType.Ok : DependencyStatusType.Warning,
                    isLatest ? "Up to date" : $"Update available: {latestAvailable}",
                    IsFixable: !isLatest,
                    FixAction: isLatest ? null : "update-workloads"
                ));
            }
            
            // Get installed manifests
            progress?.Report("Checking workload manifests...");
            var manifestIds = localSdkService.GetInstalledWorkloadManifests(context.EffectiveFeatureBand);
            foreach (var manifestId in manifestIds)
            {
                if (manifestId.Equals("workloadsets", StringComparison.OrdinalIgnoreCase))
                    continue;
                    
                var manifest = await localSdkService.GetInstalledManifestAsync(context.EffectiveFeatureBand, manifestId);
                if (manifest != null)
                {
                    manifests.Add(new WorkloadManifestInfo(
                        manifestId,
                        manifest.Version,
                        manifest.Description,
                        manifest.Workloads.Count,
                        manifest.Packs.Count
                    ));
                }
            }
            
            // Check workload dependencies
            await CheckWorkloadDependenciesAsync(context, dependencies, progress);
        }
        
        // Always check Xcode on macOS/Mac Catalyst (outside the feature band check)
        if (IsMacPlatform && !dependencies.Any(d => d.Category == DependencyCategory.Xcode))
        {
            progress?.Report("Checking Xcode...");
            await CheckXcodeAsync(null, dependencies);
        }
        
        return new DoctorReport(
            context,
            sdkInfos,
            availableSdkVersions,
            workloadSetVersion,
            availableWorkloadSets,
            manifests,
            dependencies,
            DateTime.UtcNow
        );
    }
    
    private async Task CheckWorkloadDependenciesAsync(
        DoctorContext context, 
        List<DependencyStatus> dependencies,
        IProgress<string>? progress)
    {
        if (context.EffectiveFeatureBand == null) return;
        
        var localSdkService = GetLocalSdkService();
        var manifestService = GetManifestService();
        
        // Collect all dependencies from installed manifests
        var manifestIds = localSdkService.GetInstalledWorkloadManifests(context.EffectiveFeatureBand);
        
        WorkloadDependencies? mauiDeps = null;
        
        foreach (var manifestId in manifestIds)
        {
            if (!manifestId.Contains("maui", StringComparison.OrdinalIgnoreCase) &&
                !manifestId.Contains("android", StringComparison.OrdinalIgnoreCase) &&
                !manifestId.Contains("ios", StringComparison.OrdinalIgnoreCase))
                continue;
                
            var manifest = await localSdkService.GetInstalledManifestAsync(context.EffectiveFeatureBand, manifestId);
            if (manifest == null) continue;
            
            try
            {
                // Try to get dependencies from NuGet package
                var version = NuGet.Versioning.NuGetVersion.Parse(manifest.Version);
                var deps = await manifestService.GetDependenciesAsync(manifestId, context.EffectiveFeatureBand, version);
                if (deps != null && deps.Entries.Count > 0)
                {
                    mauiDeps = deps;
                    break; // Found MAUI dependencies
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Could not get dependencies for {manifestId}: {ex.Message}");
            }
        }
        
        if (mauiDeps == null)
        {
            _logger.LogDebug("No workload dependencies found");
            return;
        }
        
        // Process each dependency entry
        foreach (var (workloadId, entry) in mauiDeps.Entries)
        {
            // JDK check
            if (entry.Jdk != null)
            {
                progress?.Report("Checking JDK...");
                await CheckJdkAsync(entry.Jdk, dependencies);
            }
            
            // Android SDK check
            if (entry.AndroidSdk != null)
            {
                progress?.Report("Checking Android SDK...");
                await CheckAndroidSdkAsync(entry.AndroidSdk, dependencies);
            }
            
            // Xcode check (macOS only) - always check on macOS even if not in manifest
            if (IsMacPlatform)
            {
                progress?.Report("Checking Xcode...");
                await CheckXcodeAsync(entry.Xcode, dependencies);
            }
            
            // Windows SDK checks (Windows only)
            if (OperatingSystem.IsWindows())
            {
                if (entry.WindowsAppSdk != null)
                {
                    progress?.Report("Checking Windows App SDK...");
                    CheckWindowsAppSdk(entry.WindowsAppSdk, dependencies);
                }
                
                if (entry.WebView2 != null)
                {
                    progress?.Report("Checking WebView2...");
                    CheckWebView2(entry.WebView2, dependencies);
                }
            }
        }
        
        // Always check Xcode on macOS even if no MAUI deps found
        if (IsMacPlatform && !dependencies.Any(d => d.Category == DependencyCategory.Xcode))
        {
            progress?.Report("Checking Xcode...");
            await CheckXcodeAsync(null, dependencies);
        }
    }
    
    private async Task CheckJdkAsync(VersionDependency jdkDep, List<DependencyStatus> dependencies)
    {
        // Check if JDK is already in the list
        if (dependencies.Any(d => d.Category == DependencyCategory.Jdk)) return;
        
        string? installedVersion = null;
        
        // Delegate JDK discovery to OpenJdkSettingsService (single source of truth)
        var jdkPath = await _jdkSettingsService.GetEffectiveJdkPathAsync();
        if (!string.IsNullOrEmpty(jdkPath))
        {
            installedVersion = await GetJdkVersionAsync(jdkPath);
        }
        
        var status = installedVersion != null ? DependencyStatusType.Ok : DependencyStatusType.Error;
        var message = installedVersion != null 
            ? $"JDK {installedVersion} found"
            : "JDK not found. Required for Android development.";
        
        dependencies.Add(new DependencyStatus(
            "JDK",
            DependencyCategory.Jdk,
            jdkDep.Version,
            jdkDep.RecommendedVersion,
            installedVersion,
            status,
            message,
            IsFixable: false // Would need to download/install JDK
        ));
    }
    
    private async Task<string?> GetJdkVersionAsync(string jdkPath)
    {
        try
        {
            var javaExe = OperatingSystem.IsWindows() 
                ? Path.Combine(jdkPath, "bin", "java.exe")
                : Path.Combine(jdkPath, "bin", "java");
                
            if (!File.Exists(javaExe)) return null;
            
            var psi = new ProcessStartInfo
            {
                FileName = javaExe,
                Arguments = "-version",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return null;
            
            var output = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            // Parse version from output like: openjdk version "17.0.1" 2021-10-19
            var match = System.Text.RegularExpressions.Regex.Match(output, @"version ""(\d+)");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        catch
        {
            // Ignore errors
        }
        
        return null;
    }
    
    private async Task CheckAndroidSdkAsync(AndroidSdkDependency androidDep, List<DependencyStatus> dependencies)
    {
        // Check if Android SDK already in list
        if (dependencies.Any(d => d.Category == DependencyCategory.AndroidSdk && d.Name == "Android SDK")) return;
        
        // Make sure SDK is detected first
        if (!_androidSdkService.IsSdkInstalled)
        {
            await _androidSdkService.DetectSdkAsync();
        }
        
        var isSdkInstalled = _androidSdkService.IsSdkInstalled;
        
        if (!isSdkInstalled)
        {
            dependencies.Add(new DependencyStatus(
                "Android SDK",
                DependencyCategory.AndroidSdk,
                null, null, null,
                DependencyStatusType.Error,
                "Android SDK not found",
                IsFixable: true,
                FixAction: "install-android-sdk"
            ));
            return;
        }
        
        dependencies.Add(new DependencyStatus(
            "Android SDK",
            DependencyCategory.AndroidSdk,
            null, null, _androidSdkService.SdkPath,
            DependencyStatusType.Ok,
            $"Found at {_androidSdkService.SdkPath}",
            IsFixable: false
        ));
        
        // Check for required Android SDK components
        await CheckAndroidSdkComponentsAsync(androidDep, dependencies);
        
        // Check for Android emulator
        await CheckAndroidEmulatorAsync(dependencies);
    }
    
    private async Task CheckAndroidSdkComponentsAsync(AndroidSdkDependency androidDep, List<DependencyStatus> dependencies)
    {
        try
        {
            // Get installed packages
            var installedPackages = await _androidSdkService.GetInstalledPackagesAsync();
            
            // Check for platform-tools
            var hasPlatformTools = installedPackages.Any(p => p.Path?.Contains("platform-tools") == true);
            dependencies.Add(new DependencyStatus(
                "Platform Tools",
                DependencyCategory.AndroidSdk,
                null, null,
                hasPlatformTools ? "Installed" : null,
                hasPlatformTools ? DependencyStatusType.Ok : DependencyStatusType.Warning,
                hasPlatformTools ? "adb and fastboot available" : "Platform tools not installed",
                IsFixable: !hasPlatformTools,
                FixAction: hasPlatformTools ? null : "install-android-package:platform-tools"
            ));
            
            // Check for build-tools (need at least one version)
            var buildTools = installedPackages.Where(p => p.Path?.StartsWith("build-tools") == true).ToList();
            var hasBuildTools = buildTools.Count > 0;
            var latestBuildTools = buildTools.OrderByDescending(p => p.Version).FirstOrDefault();
            dependencies.Add(new DependencyStatus(
                "Build Tools",
                DependencyCategory.AndroidSdk,
                null, null,
                latestBuildTools?.Version,
                hasBuildTools ? DependencyStatusType.Ok : DependencyStatusType.Warning,
                hasBuildTools ? $"Version {latestBuildTools?.Version}" : "No build tools installed",
                IsFixable: !hasBuildTools,
                FixAction: hasBuildTools ? null : "install-android-package:build-tools"
            ));
            
            // Check for at least one platform (android-XX)
            var platforms = installedPackages.Where(p => p.Path?.StartsWith("platforms;android-") == true).ToList();
            var hasPlatforms = platforms.Count > 0;
            var latestPlatform = platforms.OrderByDescending(p => 
            {
                var parts = p.Path?.Split('-');
                return parts?.Length > 1 && int.TryParse(parts[1], out var api) ? api : 0;
            }).FirstOrDefault();
            dependencies.Add(new DependencyStatus(
                "Android Platform",
                DependencyCategory.AndroidSdk,
                null, null,
                latestPlatform?.Path?.Replace("platforms;", ""),
                hasPlatforms ? DependencyStatusType.Ok : DependencyStatusType.Error,
                hasPlatforms ? $"API {latestPlatform?.Path?.Split('-').LastOrDefault()}" : "No Android platforms installed",
                IsFixable: !hasPlatforms,
                FixAction: hasPlatforms ? null : "install-android-package:platforms;android-35"
            ));
            
            // Check for command-line tools
            var hasCmdlineTools = installedPackages.Any(p => p.Path?.Contains("cmdline-tools") == true);
            dependencies.Add(new DependencyStatus(
                "Command Line Tools",
                DependencyCategory.AndroidSdk,
                null, null,
                hasCmdlineTools ? "Installed" : null,
                hasCmdlineTools ? DependencyStatusType.Ok : DependencyStatusType.Warning,
                hasCmdlineTools ? "sdkmanager available" : "Command line tools not installed",
                IsFixable: !hasCmdlineTools,
                FixAction: hasCmdlineTools ? null : "install-android-package:cmdline-tools;latest"
            ));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to check Android SDK components: {Message}", ex.Message);
        }
    }
    
    private async Task CheckAndroidEmulatorAsync(List<DependencyStatus> dependencies)
    {
        try
        {
            // Check if emulator is installed
            var installedPackages = await _androidSdkService.GetInstalledPackagesAsync();
            var hasEmulator = installedPackages.Any(p => p.Path == "emulator");
            
            if (!hasEmulator)
            {
                dependencies.Add(new DependencyStatus(
                    "Android Emulator",
                    DependencyCategory.AndroidSdk,
                    null, null, null,
                    DependencyStatusType.Warning,
                    "Emulator package not installed",
                    IsFixable: true,
                    FixAction: "install-android-package:emulator"
                ));
                return;
            }
            
            // Check for at least one AVD (Android Virtual Device)
            var avds = await _androidSdkService.GetAvdsAsync();
            var hasAvd = avds.Count > 0;

            // Check for system images
            var systemImages = installedPackages.Where(p => p.Path?.Contains("system-images") == true).ToList();
            if (systemImages.Count == 0)
            {
                dependencies.Add(new DependencyStatus(
                    "System Images",
                    DependencyCategory.AndroidSdk,
                    null, null, null,
                    DependencyStatusType.Warning,
                    "No system images installed for emulator",
                    IsFixable: true,
                    FixAction: "install-android-package:system-images"
                ));
            }

            dependencies.Add(new DependencyStatus(
                "Android Emulator",
                DependencyCategory.AndroidSdk,
                null, null,
                hasAvd ? $"{avds.Count} AVD(s)" : "No AVDs",
                hasAvd ? DependencyStatusType.Ok : DependencyStatusType.Warning,
                hasAvd ? $"{avds.Count} virtual device(s) configured" : "No Android virtual devices configured",
                IsFixable: !hasAvd,
                FixAction: hasAvd ? null : "open-emulators"
            ));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to check Android emulator: {Message}", ex.Message);
        }
    }
    
    private string? GetPlatformSpecificPackageId(AndroidSdkPackage pkg)
    {
        if (!string.IsNullOrEmpty(pkg.Id))
            return pkg.Id;
            
        if (pkg.PlatformIds == null)
            return null;
            
        var rid = OperatingSystem.IsWindows() ? "win" 
            : IsMacPlatform ? "osx" 
            : "linux";
            
        return pkg.PlatformIds.TryGetValue(rid, out var platformId) ? platformId : null;
    }
    
    private async Task CheckXcodeAsync(VersionDependency? xcodeDep, List<DependencyStatus> dependencies)
    {
        if (dependencies.Any(d => d.Category == DependencyCategory.Xcode && d.Name == "Xcode"))
            return;
        
        string? installedVersion = null;
        string? xcodePath = null;
        string? buildVersion = null;
        
        try
        {
            // Get Xcode path
            var psi = new ProcessStartInfo
            {
                FileName = "xcode-select",
                Arguments = "-p",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                xcodePath = (await process.StandardOutput.ReadToEndAsync()).Trim();
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0 && !string.IsNullOrEmpty(xcodePath))
                {
                    // Get Xcode version
                    var versionPsi = new ProcessStartInfo
                    {
                        FileName = "xcodebuild",
                        Arguments = "-version",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using var versionProcess = Process.Start(versionPsi);
                    if (versionProcess != null)
                    {
                        var versionOutput = await versionProcess.StandardOutput.ReadToEndAsync();
                        await versionProcess.WaitForExitAsync();
                        
                        // Parse: Xcode 15.0\nBuild version 15A240d
                        var versionMatch = System.Text.RegularExpressions.Regex.Match(versionOutput, @"Xcode (\d+\.\d+(?:\.\d+)?)");
                        if (versionMatch.Success)
                        {
                            installedVersion = versionMatch.Groups[1].Value;
                        }
                        
                        var buildMatch = System.Text.RegularExpressions.Regex.Match(versionOutput, @"Build version (\w+)");
                        if (buildMatch.Success)
                        {
                            buildVersion = buildMatch.Groups[1].Value;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Xcode check failed: {Message}", ex.Message);
        }
        
        var status = installedVersion != null ? DependencyStatusType.Ok : DependencyStatusType.Error;
        var message = installedVersion != null 
            ? $"Xcode {installedVersion} ({buildVersion ?? "unknown build"})"
            : "Xcode not found. Install from Mac App Store.";
        
        dependencies.Add(new DependencyStatus(
            "Xcode",
            DependencyCategory.Xcode,
            xcodeDep?.Version,
            xcodeDep?.RecommendedVersion,
            installedVersion,
            status,
            message,
            IsFixable: false // Requires App Store
        ));
        
        // If Xcode is installed, check for simulators
        if (installedVersion != null)
        {
            await CheckSimulatorsAsync(dependencies);
        }
    }
    
    private async Task CheckSimulatorsAsync(List<DependencyStatus> dependencies)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xcrun",
                Arguments = "simctl list devices available -j",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0)
                {
                    // Count available simulators
                    int iosCount = 0, tvosCount = 0, watchosCount = 0;
                    
                    // Simple parsing - count "isAvailable" : true occurrences by runtime
                    var lines = output.Split('\n');
                    string? currentRuntime = null;
                    
                    foreach (var line in lines)
                    {
                        if (line.Contains("\"com.apple.CoreSimulator.SimRuntime.iOS"))
                            currentRuntime = "iOS";
                        else if (line.Contains("\"com.apple.CoreSimulator.SimRuntime.tvOS"))
                            currentRuntime = "tvOS";
                        else if (line.Contains("\"com.apple.CoreSimulator.SimRuntime.watchOS"))
                            currentRuntime = "watchOS";
                        else if (line.Contains("\"udid\"") && currentRuntime != null)
                        {
                            if (currentRuntime == "iOS") iosCount++;
                            else if (currentRuntime == "tvOS") tvosCount++;
                            else if (currentRuntime == "watchOS") watchosCount++;
                        }
                    }
                    
                    var hasSimulators = iosCount > 0;
                    var details = new List<string>();
                    if (iosCount > 0) details.Add($"{iosCount} iOS");
                    if (tvosCount > 0) details.Add($"{tvosCount} tvOS");
                    if (watchosCount > 0) details.Add($"{watchosCount} watchOS");
                    
                    dependencies.Add(new DependencyStatus(
                        "iOS Simulators",
                        DependencyCategory.Xcode,
                        null, null,
                        hasSimulators ? $"{iosCount} available" : null,
                        hasSimulators ? DependencyStatusType.Ok : DependencyStatusType.Warning,
                        hasSimulators ? string.Join(", ", details) + " simulators" : "No iOS simulators available",
                        IsFixable: false
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to check simulators: {Message}", ex.Message);
        }
    }
    
    private void CheckWindowsAppSdk(VersionDependency dep, List<DependencyStatus> dependencies)
    {
        if (dependencies.Any(d => d.Category == DependencyCategory.WindowsAppSdk)) return;
        
        // Windows App SDK detection would require checking registry or installed packages
        // For now, add as unknown/warning
        dependencies.Add(new DependencyStatus(
            "Windows App SDK",
            DependencyCategory.WindowsAppSdk,
            dep.Version,
            dep.RecommendedVersion,
            null,
            DependencyStatusType.Unknown,
            "Windows App SDK check not yet implemented",
            IsFixable: false
        ));
    }
    
    private void CheckWebView2(VersionDependency dep, List<DependencyStatus> dependencies)
    {
        if (dependencies.Any(d => d.Category == DependencyCategory.WebView2)) return;
        
        // WebView2 detection would require checking registry
        // For now, add as unknown/warning
        dependencies.Add(new DependencyStatus(
            "WebView2",
            DependencyCategory.WebView2,
            dep.Version,
            dep.RecommendedVersion,
            null,
            DependencyStatusType.Unknown,
            "WebView2 check not yet implemented",
            IsFixable: false
        ));
    }
    
    public async Task<IReadOnlyList<string>> GetAvailableWorkloadSetVersionsAsync(string featureBand, bool includePrerelease = false)
    {
        var workloadSetService = GetWorkloadSetService();
        var versions = await workloadSetService.GetAvailableWorkloadSetVersionsAsync(featureBand, includePrerelease);
        // Convert NuGet versions (e.g., 10.102.0) to workload versions (e.g., 10.0.102)
        return versions.Select(v => ConvertNuGetToWorkloadVersion(v.ToString())).ToList();
    }
    
    /// <summary>
    /// Converts NuGet package version format to workload set version format.
    /// NuGet: major.(minor*100+patch).build -> Workload: major.minor.patch
    /// Example: 10.102.0 -> 10.0.102, 10.102.1 -> 10.0.102-servicing.1
    /// </summary>
    private static string ConvertNuGetToWorkloadVersion(string nugetVersion)
    {
        var parts = nugetVersion.Split('.');
        if (parts.Length < 2) return nugetVersion;
        
        if (!int.TryParse(parts[0], out var major)) return nugetVersion;
        if (!int.TryParse(parts[1], out var combined)) return nugetVersion;
        
        // Extract minor and patch from combined value
        // e.g., 102 means minor=1, patch=02 (but really minor=0, patch=102 for SDK 10.0.102)
        // Actually for workload sets, the pattern is: NuGet minor = SDK patch
        // So 10.102.0 means SDK 10.0.102
        var minor = 0; // SDK workload sets use 0 as minor
        var patch = combined;
        
        // Handle servicing versions (build > 0)
        if (parts.Length >= 3 && int.TryParse(parts[2], out var build) && build > 0)
        {
            return $"{major}.{minor}.{patch}-servicing.{build}";
        }
        
        return $"{major}.{minor}.{patch}";
    }
    
    public async Task<bool> FixDependencyAsync(DependencyStatus dependency, IProgress<string>? progress = null)
    {
        if (!dependency.IsFixable || string.IsNullOrEmpty(dependency.FixAction))
            return false;
            
        try
        {
            if (dependency.FixAction.StartsWith("install-android-package:"))
            {
                var packageId = dependency.FixAction.Substring("install-android-package:".Length);
                if (string.Equals(packageId, "system-images", StringComparison.OrdinalIgnoreCase))
                {
                    var resolved = await ResolveSystemImagePackageAsync(progress);
                    if (string.IsNullOrEmpty(resolved))
                    {
                        _logger.LogWarning("No system image package could be resolved for installation");
                        progress?.Report("No compatible system image package found");
                        return false;
                    }

                    packageId = resolved;
                    progress?.Report($"Resolved system image package: {packageId}");
                }
                else if (string.Equals(packageId, "build-tools", StringComparison.OrdinalIgnoreCase))
                {
                    var resolved = await ResolveBuildToolsPackageAsync(progress);
                    if (string.IsNullOrEmpty(resolved))
                    {
                        _logger.LogWarning("No build-tools package could be resolved for installation");
                        progress?.Report("No compatible build-tools package found");
                        return false;
                    }

                    packageId = resolved;
                    progress?.Report($"Resolved build-tools package: {packageId}");
                }

                progress?.Report($"Installing Android package: {packageId}");
                return await _androidSdkService.InstallPackageAsync(packageId, progress);
            }
            
            if (dependency.FixAction == "install-android-sdk")
            {
                progress?.Report("Acquiring Android SDK...");
                return await _androidSdkService.AcquireSdkAsync(progress: progress);
            }
            
            // Other fix actions would be implemented here
            _logger.LogWarning($"Unhandled fix action: {dependency.FixAction}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to fix dependency: {ex.Message}", ex);
            return false;
        }
    }

    private async Task<string?> ResolveSystemImagePackageAsync(IProgress<string>? progress)
    {
        try
        {
            progress?.Report("Finding a compatible system image...");
            var available = await _androidSdkService.GetAvailablePackagesAsync();
            var candidates = available
                .Where(p => !string.IsNullOrEmpty(p.Path) && p.Path.StartsWith("system-images;android-", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Path!)
                .ToList();

            if (candidates.Count == 0)
            {
                _logger.LogWarning("No available system image packages found");
                return null;
            }

            var preferredAbi = RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? "arm64-v8a"
                : "x86_64";

            int Score(string path)
            {
                var parts = path.Split(';');
                var apiPart = parts.FirstOrDefault(p => p.StartsWith("android-", StringComparison.OrdinalIgnoreCase));
                var api = 0;
                if (apiPart != null && int.TryParse(apiPart.Replace("android-", ""), out var parsedApi))
                {
                    api = parsedApi;
                }

                var vendor = parts.Length > 2 ? parts[2] : "";
                var abi = parts.Length > 3 ? parts[3] : "";

                var vendorScore = vendor switch
                {
                    "google_apis" => 30,
                    "google_apis_playstore" => 25,
                    "default" => 20,
                    _ => 10
                };

                var abiScore = string.Equals(abi, preferredAbi, StringComparison.OrdinalIgnoreCase) ? 15 : 0;

                return (api * 100) + vendorScore + abiScore;
            }

            var selected = candidates
                .OrderByDescending(Score)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(selected))
            {
                _logger.LogInformation("Selected system image package: {Package}", selected);
            }

            return selected;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to resolve system image package: {Message}", ex.Message);
            return null;
        }
    }
    
    private async Task<string?> ResolveBuildToolsPackageAsync(IProgress<string>? progress)
    {
        try
        {
            progress?.Report("Finding latest build-tools version...");
            var available = await _androidSdkService.GetAvailablePackagesAsync();
            var candidates = available
                .Where(p => !string.IsNullOrEmpty(p.Path) && p.Path.StartsWith("build-tools;", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Path!)
                .ToList();

            if (candidates.Count == 0)
            {
                _logger.LogWarning("No available build-tools packages found");
                return null;
            }

            // Pick the highest version
            var selected = candidates
                .OrderByDescending(p =>
                {
                    var versionStr = p.Split(';').LastOrDefault() ?? "";
                    return Version.TryParse(versionStr, out var v) ? v : new Version(0, 0);
                })
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(selected))
            {
                _logger.LogInformation("Selected build-tools package: {Package}", selected);
            }

            return selected;
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to resolve build-tools package: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<bool> UpdateWorkloadsAsync(string workloadSetVersion, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Updating workloads to version {workloadSetVersion}...");
            
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"workload update --version {workloadSetVersion}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return false;
            
            // Read output
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();
            
            var output = await outputTask;
            var error = await errorTask;
            
            if (process.ExitCode != 0)
            {
                _logger.LogError($"Workload update failed: {error}");
                return false;
            }
            
            progress?.Report("Workload update complete");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to update workloads: {ex.Message}", ex);
            return false;
        }
    }
}
