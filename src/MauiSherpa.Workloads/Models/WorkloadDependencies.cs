using System.Text.Json;
using System.Text.Json.Serialization;

namespace MauiSherpa.Workloads.Models;

/// <summary>
/// Represents the parsed WorkloadDependencies.json file from a workload manifest package.
/// This file contains external dependencies required by workloads (JDK, Android SDK, Windows SDK, etc.).
/// </summary>
public record WorkloadDependencies
{
    /// <summary>
    /// The workload entries keyed by workload ID.
    /// </summary>
    public IReadOnlyDictionary<string, WorkloadDependencyEntry> Entries { get; init; } = new Dictionary<string, WorkloadDependencyEntry>();

    /// <summary>
    /// The raw JSON document for accessing non-standard dependency properties.
    /// </summary>
    public JsonDocument? RawJson { get; init; }
}

/// <summary>
/// Represents a single workload's dependencies.
/// </summary>
public record WorkloadDependencyEntry
{
    /// <summary>
    /// The workload ID this entry describes.
    /// </summary>
    public required string WorkloadId { get; init; }

    /// <summary>
    /// Workload metadata (alias, version).
    /// </summary>
    public WorkloadInfo? Workload { get; init; }

    /// <summary>
    /// Xcode dependency information (for iOS/macOS/tvOS/MacCatalyst workloads).
    /// </summary>
    public VersionDependency? Xcode { get; init; }

    /// <summary>
    /// SDK version dependency (for Apple platform workloads).
    /// </summary>
    public VersionDependency? Sdk { get; init; }

    /// <summary>
    /// JDK dependency information (for Android workloads).
    /// </summary>
    public VersionDependency? Jdk { get; init; }

    /// <summary>
    /// Android SDK packages dependency information.
    /// </summary>
    public AndroidSdkDependency? AndroidSdk { get; init; }

    /// <summary>
    /// Windows App SDK dependency information (for MAUI workloads).
    /// </summary>
    public VersionDependency? WindowsAppSdk { get; init; }

    /// <summary>
    /// Windows SDK Build Tools dependency information.
    /// </summary>
    public VersionDependency? WindowsSdkBuildTools { get; init; }

    /// <summary>
    /// Win2D dependency information.
    /// </summary>
    public VersionDependency? Win2D { get; init; }

    /// <summary>
    /// WebView2 dependency information.
    /// </summary>
    public VersionDependency? WebView2 { get; init; }

    /// <summary>
    /// Appium dependency information (for UI testing).
    /// </summary>
    public AppiumDependency? Appium { get; init; }

    /// <summary>
    /// Raw JSON element for accessing additional/custom properties.
    /// </summary>
    public JsonElement? RawElement { get; init; }
}

/// <summary>
/// Basic workload metadata from dependencies file.
/// </summary>
public record WorkloadInfo
{
    /// <summary>
    /// Workload aliases.
    /// </summary>
    public IReadOnlyList<string> Alias { get; init; } = [];

    /// <summary>
    /// Workload version.
    /// </summary>
    public string? Version { get; init; }
}

/// <summary>
/// A dependency with version constraints.
/// </summary>
public record VersionDependency
{
    /// <summary>
    /// Version constraint (NuGet version range format, e.g., "[17.0,22.0)").
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Recommended version to install.
    /// </summary>
    public string? RecommendedVersion { get; init; }
}

/// <summary>
/// Android SDK dependency with packages list.
/// </summary>
public record AndroidSdkDependency
{
    /// <summary>
    /// List of Android SDK packages required.
    /// </summary>
    public IReadOnlyList<AndroidSdkPackage> Packages { get; init; } = [];
}

/// <summary>
/// An individual Android SDK package requirement.
/// </summary>
public record AndroidSdkPackage
{
    /// <summary>
    /// Human-readable description of the package.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The SDK package identifier (e.g., "build-tools;35.0.0").
    /// Can be a single ID or platform-specific IDs.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Platform-specific package IDs (RID → package ID).
    /// </summary>
    public IReadOnlyDictionary<string, string>? PlatformIds { get; init; }

    /// <summary>
    /// Recommended version of the package.
    /// </summary>
    public string? RecommendedVersion { get; init; }

    /// <summary>
    /// Whether this package is optional.
    /// </summary>
    public bool IsOptional { get; init; }
}

/// <summary>
/// Appium testing framework dependency.
/// </summary>
public record AppiumDependency
{
    /// <summary>
    /// Appium version constraint.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Recommended Appium version.
    /// </summary>
    public string? RecommendedVersion { get; init; }

    /// <summary>
    /// Appium drivers required.
    /// </summary>
    public IReadOnlyList<AppiumDriver> Drivers { get; init; } = [];
}

/// <summary>
/// An Appium driver dependency.
/// </summary>
public record AppiumDriver
{
    /// <summary>
    /// Driver name (e.g., "windows", "xcuitest", "uiautomator2").
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Version constraint.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Recommended version.
    /// </summary>
    public string? RecommendedVersion { get; init; }
}

/// <summary>
/// Extension methods for WorkloadDependencies.
/// </summary>
public static class WorkloadDependenciesExtensions
{
    /// <summary>
    /// Creates a summary object suitable for JSON serialization.
    /// </summary>
    public static object ToSummary(this WorkloadDependencies deps)
    {
        return new
        {
            workloads = deps.Entries.Select(e => new
            {
                workloadId = e.Key,
                xcode = e.Value.Xcode != null ? new { e.Value.Xcode.Version, e.Value.Xcode.RecommendedVersion } : null,
                sdk = e.Value.Sdk != null ? new { e.Value.Sdk.Version, e.Value.Sdk.RecommendedVersion } : null,
                jdk = e.Value.Jdk != null ? new { e.Value.Jdk.Version, e.Value.Jdk.RecommendedVersion } : null,
                androidSdk = e.Value.AndroidSdk?.Packages.Select(p => new
                {
                    description = p.Description,
                    id = p.Id,
                    platformIds = p.PlatformIds,
                    recommendedVersion = p.RecommendedVersion,
                    optional = p.IsOptional
                }).ToList(),
                windowsAppSdk = e.Value.WindowsAppSdk != null ? new { e.Value.WindowsAppSdk.Version, e.Value.WindowsAppSdk.RecommendedVersion } : null,
                webView2 = e.Value.WebView2 != null ? new { e.Value.WebView2.Version, e.Value.WebView2.RecommendedVersion } : null,
                appium = e.Value.Appium != null ? new
                {
                    e.Value.Appium.Version,
                    e.Value.Appium.RecommendedVersion,
                    drivers = e.Value.Appium.Drivers.Select(d => new { d.Name, d.Version, d.RecommendedVersion }).ToList()
                } : null
            }).ToList()
        };
    }
}

/// <summary>
/// JSON deserialization helpers for WorkloadDependencies.
/// </summary>
internal static class WorkloadDependenciesParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static WorkloadDependencies Parse(string json)
    {
        var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var entries = new Dictionary<string, WorkloadDependencyEntry>();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var workloadId = prop.Name;
            var entry = ParseEntry(workloadId, prop.Value);
            entries[workloadId] = entry;
        }

        return new WorkloadDependencies
        {
            Entries = entries,
            RawJson = doc
        };
    }

    private static WorkloadDependencyEntry ParseEntry(string workloadId, JsonElement element)
    {
        WorkloadInfo? workload = null;
        VersionDependency? xcode = null;
        VersionDependency? sdk = null;
        VersionDependency? jdk = null;
        AndroidSdkDependency? androidSdk = null;
        VersionDependency? windowsAppSdk = null;
        VersionDependency? windowsSdkBuildTools = null;
        VersionDependency? win2d = null;
        VersionDependency? webView2 = null;
        AppiumDependency? appium = null;

        foreach (var prop in element.EnumerateObject())
        {
            switch (prop.Name.ToLowerInvariant())
            {
                case "workload":
                    workload = ParseWorkloadInfo(prop.Value);
                    break;
                case "xcode":
                    xcode = ParseVersionDependency(prop.Value);
                    break;
                case "sdk":
                    sdk = ParseVersionDependency(prop.Value);
                    break;
                case "jdk":
                    jdk = ParseVersionDependency(prop.Value);
                    break;
                case "androidsdk":
                    androidSdk = ParseAndroidSdk(prop.Value);
                    break;
                case "windowsappsdk":
                    windowsAppSdk = ParseVersionDependency(prop.Value);
                    break;
                case "windowssdkbuildtools":
                    windowsSdkBuildTools = ParseVersionDependency(prop.Value);
                    break;
                case "win2d":
                    win2d = ParseVersionDependency(prop.Value);
                    break;
                case "webview2":
                    webView2 = ParseVersionDependency(prop.Value);
                    break;
                case "appium":
                    appium = ParseAppium(prop.Value);
                    break;
            }
        }

        return new WorkloadDependencyEntry
        {
            WorkloadId = workloadId,
            Workload = workload,
            Xcode = xcode,
            Sdk = sdk,
            Jdk = jdk,
            AndroidSdk = androidSdk,
            WindowsAppSdk = windowsAppSdk,
            WindowsSdkBuildTools = windowsSdkBuildTools,
            Win2D = win2d,
            WebView2 = webView2,
            Appium = appium,
            RawElement = element.Clone()
        };
    }

    private static WorkloadInfo ParseWorkloadInfo(JsonElement element)
    {
        var alias = new List<string>();
        string? version = null;

        if (element.TryGetProperty("alias", out var aliasEl) && aliasEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in aliasEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    alias.Add(item.GetString()!);
            }
        }

        if (element.TryGetProperty("version", out var versionEl))
            version = versionEl.GetString();

        return new WorkloadInfo { Alias = alias, Version = version };
    }

    private static VersionDependency ParseVersionDependency(JsonElement element)
    {
        string? version = null;
        string? recommendedVersion = null;

        if (element.TryGetProperty("version", out var versionEl))
            version = versionEl.GetString();

        if (element.TryGetProperty("recommendedVersion", out var recEl))
            recommendedVersion = recEl.GetString();

        return new VersionDependency { Version = version, RecommendedVersion = recommendedVersion };
    }

    private static AndroidSdkDependency ParseAndroidSdk(JsonElement element)
    {
        var packages = new List<AndroidSdkPackage>();

        if (element.TryGetProperty("packages", out var packagesEl) && packagesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var pkgEl in packagesEl.EnumerateArray())
            {
                packages.Add(ParseAndroidSdkPackage(pkgEl));
            }
        }

        return new AndroidSdkDependency { Packages = packages };
    }

    private static AndroidSdkPackage ParseAndroidSdkPackage(JsonElement element)
    {
        string? description = null;
        string? id = null;
        Dictionary<string, string>? platformIds = null;
        string? recommendedVersion = null;
        bool isOptional = false;

        if (element.TryGetProperty("desc", out var descEl))
            description = descEl.GetString();

        if (element.TryGetProperty("optional", out var optEl))
        {
            if (optEl.ValueKind == JsonValueKind.True)
                isOptional = true;
            else if (optEl.ValueKind == JsonValueKind.String)
                isOptional = optEl.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
        }

        if (element.TryGetProperty("sdkPackage", out var sdkPkgEl))
        {
            if (sdkPkgEl.TryGetProperty("id", out var idEl))
            {
                if (idEl.ValueKind == JsonValueKind.String)
                {
                    id = idEl.GetString();
                }
                else if (idEl.ValueKind == JsonValueKind.Object)
                {
                    // Platform-specific IDs
                    platformIds = new Dictionary<string, string>();
                    foreach (var prop in idEl.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                            platformIds[prop.Name] = prop.Value.GetString()!;
                    }
                }
            }

            if (sdkPkgEl.TryGetProperty("recommendedVersion", out var recEl))
                recommendedVersion = recEl.GetString();
        }

        return new AndroidSdkPackage
        {
            Description = description,
            Id = id,
            PlatformIds = platformIds,
            RecommendedVersion = recommendedVersion,
            IsOptional = isOptional
        };
    }

    private static AppiumDependency ParseAppium(JsonElement element)
    {
        string? version = null;
        string? recommendedVersion = null;
        var drivers = new List<AppiumDriver>();

        if (element.TryGetProperty("version", out var versionEl))
            version = versionEl.GetString();

        if (element.TryGetProperty("recommendedVersion", out var recEl))
            recommendedVersion = recEl.GetString();

        if (element.TryGetProperty("drivers", out var driversEl) && driversEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var driverEl in driversEl.EnumerateArray())
            {
                string? name = null;
                string? driverVersion = null;
                string? driverRecVersion = null;

                if (driverEl.TryGetProperty("name", out var nameEl))
                    name = nameEl.GetString();
                if (driverEl.TryGetProperty("version", out var dvEl))
                    driverVersion = dvEl.GetString();
                if (driverEl.TryGetProperty("recommendedVersion", out var drEl))
                    driverRecVersion = drEl.GetString();

                drivers.Add(new AppiumDriver
                {
                    Name = name,
                    Version = driverVersion,
                    RecommendedVersion = driverRecVersion
                });
            }
        }

        return new AppiumDependency
        {
            Version = version,
            RecommendedVersion = recommendedVersion,
            Drivers = drivers
        };
    }
}
