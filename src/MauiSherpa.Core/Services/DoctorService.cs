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
    private readonly IAndroidSdkSettingsService? _androidSdkSettingsService;
    private readonly IDotnetUpService? _dotnetUpService;
    private readonly IDotnetWorkloadService? _dotnetWorkloadService;
    private readonly IDebugFlagService? _debugFlags;
    private readonly ILogger<DoctorService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    
    // MauiSherpa.Workloads services - instantiated on demand
    private LocalSdkService? _localSdkService;
    private LocalSdkService? _rootedSdkService;
    private string? _rootedSdkServiceRoot;
    private string? _authoritativeSdkRoot;
    private GlobalJsonService? _globalJsonService;
    private NuGetClient? _nugetClient;
    private WorkloadSetService? _workloadSetService;
    private SdkVersionService? _sdkVersionService;
    
    public DoctorService(
        IAndroidSdkService androidSdkService,
        ILoggingService loggingService,
        IOpenJdkSettingsService jdkSettingsService,
        ILoggerFactory? loggerFactory = null,
        IDebugFlagService? debugFlags = null,
        IAndroidSdkSettingsService? androidSdkSettingsService = null,
        IDotnetUpService? dotnetUpService = null,
        IDotnetWorkloadService? dotnetWorkloadService = null)
    {
        _androidSdkService = androidSdkService;
        _loggingService = loggingService;
        _jdkSettingsService = jdkSettingsService;
        _debugFlags = debugFlags;
        _androidSdkSettingsService = androidSdkSettingsService;
        _dotnetUpService = dotnetUpService;
        _dotnetWorkloadService = dotnetWorkloadService;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<DoctorService>();
    }
    
    private LocalSdkService GetLocalSdkService() => _localSdkService ??= new LocalSdkService(_loggerFactory.CreateLogger<LocalSdkService>());

    /// <summary>
    /// Returns a <see cref="LocalSdkService"/> pinned to <paramref name="installRoot"/> so manifest,
    /// workload-set, and dependency reads come from the same root Doctor decided is authoritative
    /// (which is the dotnetup-managed root whenever the user has opted into dotnetup).
    /// </summary>
    private LocalSdkService GetSdkServiceForRoot(string? installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
            return GetLocalSdkService();

        if (_rootedSdkService != null &&
            string.Equals(_rootedSdkServiceRoot, installRoot, StringComparison.OrdinalIgnoreCase))
            return _rootedSdkService;

        _rootedSdkServiceRoot = installRoot;
        _rootedSdkService = new LocalSdkService(
            _loggerFactory.CreateLogger<LocalSdkService>(), installRoot);
        return _rootedSdkService;
    }

    private GlobalJsonService GetGlobalJsonService() => _globalJsonService ??= new GlobalJsonService();
    private NuGetClient GetNuGetClient() => _nugetClient ??= new NuGetClient();
    private WorkloadSetService GetWorkloadSetService() => _workloadSetService ??= new WorkloadSetService(GetNuGetClient());

    /// <summary>
    /// Resolves the full path to the dotnet executable.
    /// GUI apps on macOS don't inherit the user's shell PATH, so bare "dotnet" won't resolve.
    /// Prefers the root Doctor last resolved as authoritative (the dotnetup-managed root when the
    /// user opted into dotnetup) so muxer-based commands run against the SDK Doctor reported on.
    /// </summary>
    private string ResolveDotNetExecutable()
    {
        var exeName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        foreach (var root in new[] { _authoritativeSdkRoot, GetLocalSdkService().GetDotNetSdkPath() })
        {
            if (string.IsNullOrEmpty(root))
                continue;
            var fullPath = Path.Combine(root, exeName);
            if (File.Exists(fullPath))
                return fullPath;
        }

        // Fallback to bare name (works if dotnet is on PATH)
        return "dotnet";
    }
    private SdkVersionService GetSdkVersionService() => _sdkVersionService ??= new SdkVersionService();
    
    // Mac Catalyst doesn't return true for RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
    private static bool IsMacPlatform => OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst();
    
    public async Task<DoctorContext> GetContextAsync(string? workingDirectory = null)
    {
        var globalJsonService = GetGlobalJsonService();
        
        // Determine working directory
        var effectiveDir = workingDirectory ?? Environment.CurrentDirectory;
        
        // Check for global.json
        var globalJson = globalJsonService.GetGlobalJson(effectiveDir);
        
        bool dotnetUpInstalled = false;
        string? dotnetUpVersion = null;
        DotnetUpListResult? dotnetUpList = null;
        if (_dotnetUpService is { IsInstalled: true })
        {
            dotnetUpInstalled = true;
            var info = await TryGetDotnetUpInfoAsync();
            dotnetUpVersion = info?.Version;
            dotnetUpList = await TryGetDotnetUpListAsync();
        }

        // Pick the authoritative install root before anything else — when the user opted into
        // dotnetup, its managed root is what the shell actually resolves, so Doctor must not mix
        // in machine-wide SDKs from /usr/local/share/dotnet (or Program Files).
        var source = ResolveSdkSource(effectiveDir, dotnetUpList);
        var sdkPath = source.InstallRoot;
        var sdkArchitecture = source.Architecture
            ?? RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var sdks = source.Sdks;

        string? featureBand = null;
        bool isPreviewSdk = false;
        string? activeSdkVersion = null;
        string? resolvedSdkVersion = null;
        if (sdks.Count > 0)
        {
            SdkVersion effectiveSdk;
            if (globalJson?.SdkVersion != null)
            {
                var pinned = sdks.FirstOrDefault(s => s.Version == globalJson.SdkVersion);
                var resolved = ResolveRollForward(globalJson.SdkVersion, globalJson.RollForward, sdks);
                resolvedSdkVersion = resolved?.Version;
                effectiveSdk = resolved ?? pinned ?? sdks[0];
            }
            else
            {
                effectiveSdk = sdks[0];
            }

            featureBand = effectiveSdk.FeatureBand;
            isPreviewSdk = effectiveSdk.IsPreview;
            activeSdkVersion = effectiveSdk.Version;
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
            ResolvedSdkVersion: resolvedSdkVersion,
            DotnetUpInstalled: dotnetUpInstalled,
            DotnetUpVersion: dotnetUpVersion,
            DotnetUpManagedInstallRoot: source.IsDotnetUpManaged
                ? source.InstallRoot
                : dotnetUpList?.InstallRoots.FirstOrDefault(),
            DotNetArchitecture: sdkArchitecture,
            UsesDotnetUpManagedSdk: source.IsDotnetUpManaged
        );
    }

    /// <summary>
    /// Resolves which .NET install root Doctor should inspect, in priority order:
    /// a repo-local <c>.dotnet</c> (an explicit per-project override), then the dotnetup-managed
    /// root when the user opted into dotnetup, then the machine's discovered install.
    /// </summary>
    private DotnetSdkSource ResolveSdkSource(string effectiveDir, DotnetUpListResult? dotnetUpList)
    {
        var repoLocalRoot = Path.Combine(effectiveDir, ".dotnet");
        if (Directory.Exists(Path.Combine(repoLocalRoot, "sdk")))
        {
            var repoLocalService = GetSdkServiceForRoot(repoLocalRoot);
            _authoritativeSdkRoot = repoLocalRoot;
            return new DotnetSdkSource
            {
                Sdks = repoLocalService.GetInstalledSdkVersions(),
                InstallRoot = repoLocalRoot,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                IsDotnetUpManaged = false
            };
        }

        var machineService = GetLocalSdkService();
        var resolved = DotnetSdkSourceResolver.Resolve(
            machineService.GetInstalledSdkVersions(),
            machineService.GetDotNetSdkPath(),
            dotnetUpList);
        _authoritativeSdkRoot = resolved.InstallRoot;
        return resolved;
    }

    private async Task<MauiSherpa.Workloads.Models.DotnetUpToolInfo?> TryGetDotnetUpInfoAsync()
    {
        if (_dotnetUpService is null)
            return null;
        try
        {
            return await _dotnetUpService.GetToolInfoAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to query dotnetup --info: {ex.Message}");
            return null;
        }
    }

    private async Task<MauiSherpa.Workloads.Models.DotnetUpListResult?> TryGetDotnetUpListAsync()
    {
        if (_dotnetUpService is null)
            return null;
        try
        {
            return await _dotnetUpService.GetListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to query dotnetup list: {ex.Message}");
            return null;
        }
    }
    
    private async Task<IReadOnlyList<DotnetUpdatePreview>?> TryGetDotnetUpUpdatePreviewAsync(
        DotnetUpListResult list)
    {
        if (_dotnetUpService is null)
            return null;
        try
        {
            return await _dotnetUpService.GetUpdatePreviewAsync(list);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to resolve dotnetup update preview: {ex.Message}");
            return null;
        }
    }

    private static readonly string[] AliasChannels = ["latest", "lts", "sts", "preview"];

    /// <summary>
    /// Finds the dotnetup tracked SDK channel that currently resolves to <paramref name="activeSdk"/>,
    /// preferring a specific channel (e.g. <c>11.0.1xx</c>) over a moving alias (e.g. <c>preview</c>)
    /// so the offered fix targets the narrowest channel that owns the SDK.
    /// </summary>
    internal static DotnetUpdatePreview? FindSdkChannelPreview(
        IReadOnlyList<DotnetUpdatePreview>? previews, SdkVersion activeSdk)
    {
        if (previews == null || previews.Count == 0)
            return null;

        return previews
            .Where(preview =>
                preview.Component == DotnetUpComponent.Sdk &&
                !preview.IsPinned &&
                string.Equals(
                    preview.InstalledVersion,
                    activeSdk.Version,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(preview => AliasChannels.Contains(
                preview.Channel, StringComparer.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(preview => preview.UpdateAvailable)
            .ThenBy(preview => preview.Channel, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>
    /// Finds the newest available SDK for the same major version that is strictly newer
    /// (by semantic version) than <paramref name="installed"/>. Returns <c>null</c> when no
    /// available version is newer.
    /// </summary>
    /// <remarks>
    /// The releases feed may not yet list a preview the user has installed (e.g. a nightly
    /// <c>10.0.400-preview</c> build). In that case the newest *published* release for the major
    /// can be an older feature band (e.g. stable <c>10.0.302</c>). Comparing by string equality
    /// would flag that lower version as an "update", suggesting a downgrade. Comparing by
    /// <see cref="SdkVersion.SemanticVersion"/> (backed by NuGet version ordering) prevents that.
    /// </remarks>
    internal static SdkVersionInfo? FindNewerAvailableSdk(
        IReadOnlyList<SdkVersionInfo>? available, SdkVersion installed)
    {
        if (available == null || available.Count == 0)
            return null;

        return available
            .Where(s => s.Major == installed.Major)
            .Select(s => (Info: s,
                Parsed: SdkVersion.TryParse(s.Version, out var v) ? v : null))
            .Where(x => x.Parsed != null && x.Parsed.CompareTo(installed) > 0)
            .OrderByDescending(x => x.Parsed!.SemanticVersion)
            .Select(x => x.Info)
            .FirstOrDefault();
    }

    /// <summary>
    /// Resolves the effective SDK version based on the rollForward policy from global.json.</summary>
    /// <remarks>
    /// See: https://learn.microsoft.com/dotnet/core/tools/global-json#rollforward
    /// </remarks>
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
        
        // Read everything from the root the context settled on. When dotnetup manages the active
        // SDK that is the dotnetup root, so Doctor and the .NET SDK Manager inspect the same files.
        var localSdkService = GetSdkServiceForRoot(context.DotNetSdkPath);
        var dependencies = new List<DependencyStatus>();
        
        // Get installed SDKs
        var sdkVersions = localSdkService.GetInstalledSdkVersions();

        var dotnetUpList = await TryGetDotnetUpListAsync();
        var dotnetUpInstalled = _dotnetUpService is { IsInstalled: true };

        // When dotnetup owns the active SDK, reuse the same tracked-channel update preview the
        // .NET SDK Manager renders so the two pages cannot report different versions.
        IReadOnlyList<DotnetUpdatePreview>? updatePreviews = null;
        if (context.UsesDotnetUpManagedSdk && dotnetUpList != null)
        {
            progress?.Report("Checking dotnetup tracked channels...");
            updatePreviews = await TryGetDotnetUpUpdatePreviewAsync(dotnetUpList);
        }

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
            var managedChannel = FindSdkChannelPreview(updatePreviews, latestSdk);

            if (managedChannel != null)
            {
                // dotnetup owns this SDK — report exactly what its tracked channel resolves to.
                var hasUpdate = managedChannel.UpdateAvailable;
                var available = managedChannel.AvailableVersion;

                dependencies.Add(new DependencyStatus(
                    ".NET SDK",
                    DependencyCategory.DotNetSdk,
                    null,
                    hasUpdate ? available : null,
                    latestSdk.Version,
                    hasUpdate
                        ? DependencyStatusType.Warning
                        : latestSdk.IsPreview ? DependencyStatusType.Info : DependencyStatusType.Ok,
                    hasUpdate
                        ? $"Update available: {available} (dotnetup channel {managedChannel.Channel})"
                        : latestSdk.IsPreview
                            ? $"Preview SDK ({latestSdk.Version}) — managed by dotnetup"
                            : $"{sdkVersions.Count} SDK(s) managed by dotnetup, using {latestSdk.Version}",
                    IsFixable: hasUpdate,
                    FixAction: hasUpdate ? $"dotnetup-update-sdk:{managedChannel.Channel}" : null
                ));
            }
            else if (latestSdk.IsPreview)
            {
                // Active SDK is a preview — only offer an update when an available version for
                // the same major is *semantically newer* than the installed preview. A preview
                // can sit on a higher feature band (e.g. 10.0.400-preview) than the newest
                // published release (e.g. stable 10.0.302), so never suggest a downgrade.
                var newerAvailable = FindNewerAvailableSdk(availableSdkVersions, latestSdk);
                var isLatestForMajor = newerAvailable == null;

                // dotnetup can fix an out-of-date preview by installing the recommended version.
                var canFix = !isLatestForMajor && _dotnetUpService != null;

                // Add an informational status about being on a preview SDK
                dependencies.Add(new DependencyStatus(
                    ".NET SDK",
                    DependencyCategory.DotNetSdk,
                    null,
                    newerAvailable?.Version,
                    latestSdk.Version,
                    isLatestForMajor ? DependencyStatusType.Info : DependencyStatusType.Warning,
                    isLatestForMajor
                        ? $"Preview SDK ({latestSdk.Version})"
                        : $"Update available: {newerAvailable?.Version}",
                    IsFixable: canFix,
                    FixAction: canFix ? $"dotnetup-update-sdk:{newerAvailable!.Version}" : null
                ));
            }
            else
            {
                var newerAvailable = FindNewerAvailableSdk(
                    availableSdkVersions?.Where(s => !s.IsPreview).ToList(), latestSdk);
                var isLatest = newerAvailable == null;

                // dotnetup can fix an out-of-date stable SDK by installing the latest.
                var canFix = !isLatest && _dotnetUpService != null;

                dependencies.Add(new DependencyStatus(
                    ".NET SDK",
                    DependencyCategory.DotNetSdk,
                    null,
                    newerAvailable?.Version,
                    latestSdk.Version,
                    isLatest ? DependencyStatusType.Ok : DependencyStatusType.Warning,
                    isLatest 
                        ? $"{sdkVersions.Count} SDK(s) installed, using {latestSdk.Version}"
                        : $"Update available: {newerAvailable?.Version}",
                    IsFixable: canFix,
                    FixAction: canFix ? $"dotnetup-update-sdk:{newerAvailable!.Version}" : null
                ));
            }
        }

        // dotnetup presence check — informational, with an install action when missing.
        if (_dotnetUpService != null)
        {
            if (dotnetUpInstalled)
            {
                var version = context.DotnetUpVersion;
                dependencies.Add(new DependencyStatus(
                    "dotnetup",
                    DependencyCategory.DotNetSdk,
                    null, null,
                    version,
                    DependencyStatusType.Info,
                    version != null
                        ? $"Installed ({version}) — manages .NET SDKs & runtimes"
                        : "Installed — manages .NET SDKs & runtimes",
                    IsFixable: false
                ));
            }
            else
            {
                dependencies.Add(new DependencyStatus(
                    "dotnetup",
                    DependencyCategory.DotNetSdk,
                    null, null, null,
                    DependencyStatusType.Info,
                    "Not installed — install to manage .NET SDKs & runtimes",
                    IsFixable: true,
                    FixAction: "install-dotnetup"
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

            var workloadInventory = await TryGetWorkloadInventoryAsync(
                context.EffectiveFeatureBand,
                context.WorkingDirectory,
                dotnetUpList,
                context.DotNetSdkPath,
                context.DotNetArchitecture,
                context.ActiveSdkVersion);
            if (workloadInventory != null)
            {
                workloadSetVersion = workloadInventory.UpdateMode == DotnetWorkloadUpdateMode.WorkloadSet
                    ? workloadInventory.ActiveWorkloadVersion
                    : null;
                availableWorkloadSets = workloadInventory.AvailableSetVersions
                    .Select(version => version.Version)
                    .ToList();

                var latestAvailable = workloadInventory.LatestAvailableSetVersion;
                var hasUpdate = workloadInventory.UpdateAvailable || workloadInventory.WorkloadUpdates.Count > 0;
                var isLoose = workloadInventory.UpdateMode == DotnetWorkloadUpdateMode.Manifests;
                var isUnknownMode = workloadInventory.UpdateMode == DotnetWorkloadUpdateMode.Unknown;
                var isProjectPinned = workloadInventory.VersionSource == DotnetWorkloadVersionSource.GlobalJson;
                var message = isProjectPinned && hasUpdate
                    ? $"Project pins {workloadInventory.ActiveWorkloadVersion}; change the pin in .NET SDK Manager"
                    : isUnknownMode
                        ? "Workload update mode could not be determined"
                    : isLoose
                    ? "Using loose manifest mode"
                    : hasUpdate
                        ? $"Update available: {latestAvailable ?? "new workload manifests"}"
                        : "Up to date";
                dependencies.Add(new DependencyStatus(
                    "Workload Set",
                    DependencyCategory.Workload,
                    null,
                    latestAvailable,
                    workloadSetVersion,
                    hasUpdate || isLoose || isUnknownMode ? DependencyStatusType.Warning : DependencyStatusType.Ok,
                    message,
                    IsFixable: !isProjectPinned && latestAvailable != null && (hasUpdate || isLoose),
                    FixAction: !isProjectPinned && latestAvailable != null && (hasUpdate || isLoose)
                        ? (isLoose ? "install-workloads" : "update-workloads")
                        : null));
            }
            else
            {
                var workloadSet = await localSdkService.GetInstalledWorkloadSetAsync(context.EffectiveFeatureBand);
                workloadSetVersion = workloadSet?.Version;
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

                var latestAvailable = availableWorkloadSets?.FirstOrDefault();
                var isLatest = workloadSetVersion != null &&
                    string.Equals(latestAvailable, workloadSetVersion, StringComparison.OrdinalIgnoreCase);
                dependencies.Add(new DependencyStatus(
                    "Workload Set",
                    DependencyCategory.Workload,
                    null,
                    latestAvailable,
                    workloadSetVersion,
                    isLatest ? DependencyStatusType.Ok : DependencyStatusType.Warning,
                    workloadSetVersion == null
                        ? "No workload set installed (loose manifest mode)"
                        : isLatest ? "Up to date" : $"Update available: {latestAvailable}",
                    IsFixable: !isLatest && latestAvailable != null,
                    FixAction: !isLatest && latestAvailable != null
                        ? (workloadSetVersion == null ? "install-workloads" : "update-workloads")
                        : null));
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
        
        var localSdkService = GetSdkServiceForRoot(context.DotNetSdkPath);
        // Collect all dependencies from installed manifests
        var manifestIds = localSdkService.GetInstalledWorkloadManifests(context.EffectiveFeatureBand);
        
        // Collect dependencies from ALL matching manifests (MAUI, Android, iOS each have their own)
        var allEntries = new Dictionary<string, WorkloadDependencyEntry>();
        
        foreach (var manifestId in manifestIds)
        {
            if (!manifestId.Contains("maui", StringComparison.OrdinalIgnoreCase) &&
                !manifestId.Contains("android", StringComparison.OrdinalIgnoreCase) &&
                !manifestId.Contains("ios", StringComparison.OrdinalIgnoreCase))
                continue;
                
            var manifest = await localSdkService.GetInstalledManifestAsync(context.EffectiveFeatureBand, manifestId);
            if (manifest == null) continue;
            
            var deps = await localSdkService.GetInstalledDependenciesAsync(context.EffectiveFeatureBand, manifestId);
            if (deps == null || deps.Entries.Count == 0)
                continue;

            foreach (var (workloadId, entry) in deps.Entries)
            {
                if (!allEntries.ContainsKey(workloadId))
                    allEntries[workloadId] = entry;
            }
        }
        
        if (allEntries.Count == 0)
        {
            _logger.LogDebug("No workload dependencies found");
            return;
        }
        
        // Process each dependency entry
        foreach (var (workloadId, entry) in allEntries)
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

    public async Task<ProcessRequest?> GetWorkloadUpdateRequestAsync(
        string featureBand,
        string? workingDirectory,
        string workloadSetVersion,
        string? installRoot = null,
        string? architecture = null,
        string? sdkVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureBand);
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadSetVersion);
        var inventory = await TryGetWorkloadInventoryAsync(
            featureBand,
            workingDirectory,
            await TryGetDotnetUpListAsync().ConfigureAwait(false),
            installRoot,
            architecture,
            sdkVersion,
            cancellationToken).ConfigureAwait(false);
        return inventory == null
            ? null
            : _dotnetWorkloadService!.CreateUpdateSetRequest(inventory.Target, workloadSetVersion);
    }

    private async Task<DotnetWorkloadInventory?> TryGetWorkloadInventoryAsync(
        string featureBand,
        string? workingDirectory,
        DotnetUpListResult? list,
        string? preferredInstallRoot = null,
        string? preferredArchitecture = null,
        string? preferredSdkVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (_dotnetWorkloadService == null || list == null ||
            !SdkFeatureBand.TryParse(featureBand, out var parsedBand))
            return null;

        try
        {
            var targets = await _dotnetWorkloadService.GetTargetsAsync(list, cancellationToken)
                .ConfigureAwait(false);
            var candidates = targets
                .Where(candidate => candidate.FeatureBand.Equals(parsedBand))
                .ToList();

            var matchingInstallation = string.IsNullOrWhiteSpace(preferredSdkVersion)
                ? null
                : list.Installations.FirstOrDefault(installation =>
                    installation.Component == DotnetUpComponent.Sdk &&
                    installation.IsValid &&
                    string.Equals(
                        installation.Version,
                        preferredSdkVersion,
                        StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(preferredInstallRoot) ||
                     string.Equals(
                         installation.InstallRoot,
                         preferredInstallRoot,
                         StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(preferredArchitecture) ||
                     string.Equals(
                         installation.Architecture,
                         preferredArchitecture,
                         StringComparison.OrdinalIgnoreCase)));
            preferredInstallRoot ??= matchingInstallation?.InstallRoot;
            preferredArchitecture ??= matchingInstallation?.Architecture;

            if (!string.IsNullOrWhiteSpace(preferredInstallRoot))
                candidates = candidates
                    .Where(candidate => string.Equals(
                        candidate.InstallRoot,
                        preferredInstallRoot,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            if (!string.IsNullOrWhiteSpace(preferredArchitecture))
                candidates = candidates
                    .Where(candidate => string.Equals(
                        candidate.Architecture,
                        preferredArchitecture,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();

            var target = candidates.Count == 1 ? candidates[0] : null;
            if (target == null)
                return null;
            return await _dotnetWorkloadService.GetInventoryAsync(
                target,
                workingDirectory,
                forceRefresh: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Failed to query workload inventory for {FeatureBand}: {Message}",
                featureBand,
                ex.Message);
            return null;
        }
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
                
                // Debug flag: simulate the bug where package name is truncated
                // (e.g. "build-tools" instead of "build-tools;36.1.0")
                if (_debugFlags?.FailBuildToolsInstall == true && packageId.StartsWith("build-tools;"))
                {
                    var truncated = packageId.Split(';').First();
                    _logger.LogWarning("DEBUG: Truncating package name from '{Full}' to '{Truncated}' to simulate install failure", packageId, truncated);
                    progress?.Report($"Installing Android package: {truncated}");
                    packageId = truncated;
                }
                
                return await _androidSdkService.InstallPackageAsync(packageId, progress);
            }
            
            if (dependency.FixAction == "install-android-sdk")
            {
                progress?.Report("Acquiring Android SDK...");
                var acquired = await _androidSdkService.AcquireSdkAsync(progress: progress);
                
                if (acquired && _androidSdkSettingsService != null && !string.IsNullOrEmpty(_androidSdkService.SdkPath))
                {
                    await _androidSdkSettingsService.SetCustomSdkPathAsync(_androidSdkService.SdkPath);
                    progress?.Report($"SDK path saved: {_androidSdkService.SdkPath}");
                }
                
                return acquired;
            }

            if (dependency.FixAction == "install-dotnetup")
            {
                if (_dotnetUpService == null)
                {
                    progress?.Report("dotnetup service unavailable");
                    return false;
                }
                progress?.Report("Downloading and verifying dotnetup...");
                return await _dotnetUpService.EnsureInstalledAsync(progress: progress);
            }

            if (dependency.FixAction.StartsWith("dotnetup-update-sdk"))
            {
                if (_dotnetUpService == null)
                {
                    progress?.Report("dotnetup service unavailable");
                    return false;
                }

                if (!_dotnetUpService.IsInstalled)
                {
                    progress?.Report("Downloading and verifying dotnetup...");
                    if (!await _dotnetUpService.EnsureInstalledAsync(progress: progress))
                        return false;
                }

                string? channel = null;
                var colon = dependency.FixAction.IndexOf(':');
                if (colon >= 0 && colon < dependency.FixAction.Length - 1)
                    channel = dependency.FixAction[(colon + 1)..];

                progress?.Report($"Installing .NET SDK ({channel ?? "latest"}) via dotnetup...");
                var request = _dotnetUpService.InstallSdkRequest(channel);
                return await RunProcessRequestAsync(request, progress);
            }
            
            if (dependency.FixAction is "install-workloads" or "update-workloads")
            {
                if (string.IsNullOrEmpty(dependency.RecommendedVersion))
                {
                    _logger.LogWarning("No recommended workload set version available");
                    progress?.Report("No recommended workload version available");
                    return false;
                }

                var context = await GetContextAsync().ConfigureAwait(false);
                if (context.EffectiveFeatureBand != null)
                {
                    var request = await GetWorkloadUpdateRequestAsync(
                        context.EffectiveFeatureBand,
                        context.WorkingDirectory,
                        dependency.RecommendedVersion,
                        context.DotNetSdkPath,
                        context.DotNetArchitecture,
                        context.ActiveSdkVersion).ConfigureAwait(false);
                    if (request != null)
                    {
                        progress?.Report($"Updating feature band {context.EffectiveFeatureBand} to workload set {dependency.RecommendedVersion}...");
                        var success = await RunProcessRequestAsync(request, progress).ConfigureAwait(false);
                        if (success && request.Environment?.GetValueOrDefault("DOTNET_ROOT") is { } root)
                            _dotnetWorkloadService?.Invalidate(root);
                        return success;
                    }
                }

                progress?.Report($"Updating to workload set version {dependency.RecommendedVersion}...");
                return await UpdateWorkloadsAsync(dependency.RecommendedVersion, progress);
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


    public string GetDotNetExecutablePath() => ResolveDotNetExecutable();

    private async Task<bool> SwitchToWorkloadSetModeAsync(IProgress<string>? progress = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ResolveDotNetExecutable(),
                Arguments = "workload config --update-mode workload-set",
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
                _logger.LogError($"Failed to switch to workload-set mode: {error}");
                progress?.Report($"Error: {error}");
                return false;
            }
            
            _logger.LogInformation("Successfully switched to workload-set mode");
            progress?.Report("Switched to workload set mode");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to switch workload mode: {ex.Message}", ex);
            progress?.Report($"Error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Runs a <see cref="ProcessRequest"/> (used for dotnetup commands), streaming combined
    /// stdout/stderr lines to <paramref name="progress"/>. Returns true on a zero exit code.
    /// </summary>
    private async Task<bool> RunProcessRequestAsync(ProcessRequest request, IProgress<string>? progress = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = request.Command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = request.WorkingDirectory ?? string.Empty
            };
            foreach (var arg in request.Arguments)
                psi.ArgumentList.Add(arg);
            if (request.Environment != null)
            {
                foreach (var kvp in request.Environment)
                    psi.Environment[kvp.Key] = kvp.Value;
            }

            using var process = Process.Start(psi);
            if (process == null)
            {
                progress?.Report("Failed to start process.");
                return false;
            }

            process.OutputDataReceived += (_, e) => { if (e.Data != null) progress?.Report(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) progress?.Report(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning($"Process exited with code {process.ExitCode}: {request.CommandLine}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to run process: {ex.Message}", ex);
            progress?.Report($"Error: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> UpdateWorkloadsAsync(string workloadSetVersion, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report($"Updating workloads to version {workloadSetVersion}...");
            
            var psi = new ProcessStartInfo
            {
                FileName = ResolveDotNetExecutable(),
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
