# MauiSherpa.Workloads Usage Guide

This library provides services for querying .NET SDK workloads, manifests, and workload sets - both locally installed and available from NuGet.

## Key Services

### LocalSdkService
Query locally installed SDKs, workloads, and manifests.

```csharp
var localSdkService = new LocalSdkService();

// Get .NET SDK installation path
var dotnetPath = localSdkService.GetDotNetSdkPath();

// Get all installed SDK versions
var installedSdks = localSdkService.GetInstalledSdkVersions();

// Group by major version and get newest feature band for each
var newestFeatureBandsByMajor = installedSdks
    .GroupBy(sdk => sdk.Major)
    .Select(g => g
        .OrderByDescending(sdk => sdk.Minor)
        .ThenByDescending(sdk => sdk.Patch)
        .First())
    .OrderByDescending(sdk => sdk.Major)
    .ToList();

// Get installed workload manifests for a feature band
var manifests = localSdkService.GetInstalledWorkloadManifests(sdk.FeatureBand);

// Get details of a specific manifest
var manifest = await localSdkService.GetInstalledManifestAsync(sdk.FeatureBand, manifestId);
if (manifest != null)
{
    Console.WriteLine($"{manifestId} v{manifest.Version}");
    foreach (var (workloadId, workload) in manifest.Workloads.Where(w => !w.Value.IsAbstract))
    {
        Console.WriteLine($"  - {workloadId}: {workload.Description}");
    }
}

// Get installed workload set for a feature band
var workloadSet = await localSdkService.GetInstalledWorkloadSetAsync(sdk.FeatureBand);
if (workloadSet != null)
{
    Console.WriteLine($"Workload Set: v{workloadSet.Version}");
}

// Export all SDK info as JSON
var json = await localSdkService.GetInstalledSdkInfoAsJsonStringAsync(
    includeManifestDetails: true, 
    indented: true
);
```

### SdkVersionService
Query available .NET SDK versions from Microsoft.

```csharp
var sdkVersionService = new SdkVersionService();

// Get available SDK versions (excludes previews by default)
var sdkVersions = await sdkVersionService.GetAvailableSdkVersionsAsync(includePreview: false);

// Get latest SDK
var latestSdk = sdkVersions
    .OrderByDescending(v => v.Major)
    .ThenByDescending(v => v.Minor)
    .ThenByDescending(v => v.Patch)
    .First();

Console.WriteLine($"Latest SDK: {latestSdk.Version} (Feature Band: {latestSdk.FeatureBand})");
```

### WorkloadSetService
Query available workload sets from NuGet.

```csharp
using var nugetClient = new NuGetClient();
var workloadSetService = new WorkloadSetService(nugetClient);

// Get available workload set versions for a feature band
var workloadSetVersions = await workloadSetService.GetAvailableWorkloadSetVersionsAsync(
    featureBand: "10.0.100", 
    includePrerelease: false
);

// Get a specific workload set
var workloadSet = await workloadSetService.GetWorkloadSetAsync(
    featureBand: "10.0.100", 
    version: workloadSetVersions[0]
);

if (workloadSet != null)
{
    Console.WriteLine($"Workloads in set ({workloadSet.Workloads.Count}):");
    foreach (var (manifestId, entry) in workloadSet.Workloads)
    {
        Console.WriteLine($"  {manifestId}: v{entry.ManifestVersion}");
    }
}
```

### WorkloadManifestService
Fetch workload manifest details from NuGet.

```csharp
using var nugetClient = new NuGetClient();
var manifestService = new WorkloadManifestService(nugetClient);

// Get manifest details
var manifestVersion = NuGet.Versioning.NuGetVersion.Parse("18.0.10873");
var manifest = await manifestService.GetManifestAsync(
    manifestId: "microsoft.net.sdk.maui", 
    featureBand: "10.0.100", 
    version: manifestVersion
);

if (manifest != null)
{
    // Concrete vs abstract workloads
    var concreteWorkloads = manifest.Workloads.Where(w => !w.Value.IsAbstract).ToList();
    Console.WriteLine($"Workloads: {concreteWorkloads.Count} concrete, {manifest.Workloads.Count - concreteWorkloads.Count} abstract");

    // Workload details
    foreach (var (workloadId, workload) in concreteWorkloads)
    {
        Console.WriteLine($"  {workloadId}");
        if (workload.Extends.Count > 0)
            Console.WriteLine($"    extends: {string.Join(", ", workload.Extends)}");
    }

    // Manifest dependencies
    if (manifest.DependsOn.Count > 0)
    {
        Console.WriteLine("Dependencies:");
        foreach (var (depId, depVersion) in manifest.DependsOn)
        {
            Console.WriteLine($"  {depId}: {depVersion}");
        }
    }

    // Packs
    Console.WriteLine($"Packs: {manifest.Packs.Count} total");
}

// Get external dependencies (Xcode, JDK, Android SDK, etc.)
var deps = await manifestService.GetDependenciesAsync(
    manifestId: "microsoft.net.sdk.maui", 
    featureBand: "10.0.100", 
    version: manifestVersion
);

if (deps != null)
{
    foreach (var (workloadId, entry) in deps.Entries)
    {
        if (entry.Xcode != null)
            Console.WriteLine($"Xcode: {entry.Xcode.Version} (recommended: {entry.Xcode.RecommendedVersion})");

        if (entry.Jdk != null)
            Console.WriteLine($"JDK: {entry.Jdk.Version} (recommended: {entry.Jdk.RecommendedVersion})");

        if (entry.AndroidSdk != null)
        {
            Console.WriteLine($"Android SDK Packages ({entry.AndroidSdk.Packages.Count}):");
            foreach (var pkg in entry.AndroidSdk.Packages.Where(p => !p.IsOptional))
            {
                Console.WriteLine($"  - {pkg.Description ?? pkg.Id}");
            }
        }

        if (entry.WindowsAppSdk != null)
            Console.WriteLine($"Windows App SDK: {entry.WindowsAppSdk.Version}");

        if (entry.WebView2 != null)
            Console.WriteLine($"WebView2: {entry.WebView2.Version}");

        if (entry.Appium != null)
        {
            Console.WriteLine($"Appium: {entry.Appium.Version}");
            Console.WriteLine($"  Drivers: {string.Join(", ", entry.Appium.Drivers.Select(d => d.Name))}");
        }
    }
}
```

## Key Concepts

### Feature Bands
SDKs are organized by feature bands (e.g., "10.0.100", "9.0.100"). Workload manifests and sets are published per feature band.

### Workload Sets
A workload set defines a consistent set of workload manifest versions that work together. It maps manifest IDs to specific versions.

### Workload Manifests
Each manifest (e.g., `microsoft.net.sdk.maui`, `microsoft.net.sdk.android`) defines:
- **Workloads**: Named workloads that can be installed (some are abstract/internal)
- **Packs**: NuGet packages that comprise each workload
- **Dependencies**: Other manifests this one depends on
- **External Dependencies**: Tools like Xcode, JDK, Android SDK packages required

### Concrete vs Abstract Workloads
- **Concrete workloads**: Can be directly installed by users (e.g., `maui`, `maui-ios`)
- **Abstract workloads**: Internal implementation details, not directly installable
