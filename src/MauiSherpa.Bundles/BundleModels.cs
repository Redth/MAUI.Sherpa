using System.Text.Json;
using System.Text.Json.Serialization;

namespace MauiSherpa.Bundles;

[JsonConverter(typeof(JsonStringEnumConverter<BundlePlatform>))]
public enum BundlePlatform
{
    Android,
    Ios,
    MacOS,
    MacCatalyst,
    Windows
}

[JsonConverter(typeof(JsonStringEnumConverter<BundlePhase>))]
public enum BundlePhase
{
    Install,
    Build,
    Deploy
}

[JsonConverter(typeof(JsonStringEnumConverter<BundleAssetKind>))]
public enum BundleAssetKind
{
    AndroidKeystore,
    AppleCertificate,
    AppleProvisioningProfile,
    AppleApiKey,
    GoogleServiceAccount,
    WindowsCertificate,
    Generic
}

[JsonConverter(typeof(JsonStringEnumConverter<BundleDeploymentProvider>))]
public enum BundleDeploymentProvider
{
    TestFlight,
    GooglePlay,
    FirebaseAppDistribution,
    AmazonAppstore
}

public sealed record SherpaBundle
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public required string Name { get; init; }
    public string? Description { get; init; }
    public Dictionary<string, string> Variables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SherpaBundleEnvironment> Environments { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public BundleToolchainRequirements Toolchain { get; init; } = new();
    public Dictionary<string, BundleEmbeddedAsset> Assets { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SecretVariables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonElement> Extensions { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record SherpaBundleEnvironment
{
    public Dictionary<string, string> Variables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<BundlePlatform, BundlePlatformConfiguration> Platforms { get; init; } = [];
}

public sealed record BundlePlatformConfiguration
{
    public Dictionary<string, string> Variables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public BundleInstallConfiguration Install { get; init; } = new();
    public BundleBuildConfiguration Build { get; init; } = new();
    public List<BundleDeploymentTarget> Deploy { get; init; } = [];
}

public sealed record BundleToolchainRequirements
{
    public string? DotnetSdkVersion { get; init; }
    public string? WorkloadSetVersion { get; init; }
    public List<string> Workloads { get; init; } = [];
    public List<string> AndroidSdkPackages { get; init; } = [];
    public string? JdkVersion { get; init; }
    public string? XcodeVersion { get; init; }
}

public sealed record BundleInstallConfiguration
{
    public List<string> AssetIds { get; init; } = [];
    public Dictionary<string, string> Variables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record BundleBuildConfiguration
{
    public string? Project { get; init; }
    public string Configuration { get; init; } = "Release";
    public string? TargetFramework { get; init; }
    public string? RuntimeIdentifier { get; init; }
    public Dictionary<string, string> Variables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<BundleReplacement> Replacements { get; init; } = [];
    public List<string> ArtifactGlobs { get; init; } = [];
}

public sealed record BundleReplacement
{
    public required string Path { get; init; }
}

public sealed record BundleDeploymentTarget
{
    public required BundleDeploymentProvider Provider { get; init; }
    public string? Artifact { get; init; }
    public Dictionary<string, string> Variables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonElement> Settings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record BundleEmbeddedAsset
{
    public required BundleAssetKind Kind { get; init; }
    public required string FileName { get; init; }
    public required string ContentBase64 { get; init; }
    public string? PasswordVariable { get; init; }
    public string? OutputVariable { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record BundleRunRequest
{
    public required string Environment { get; init; }
    public IReadOnlyList<BundlePlatform> Platforms { get; init; } = [];
    public IReadOnlyList<BundlePhase> Phases { get; init; } = [BundlePhase.Install, BundlePhase.Build, BundlePhase.Deploy];
    public string SourceDirectory { get; init; } = Directory.GetCurrentDirectory();
    public string? OutputDirectory { get; init; }
    public string? Project { get; init; }
    public string? ArtifactPath { get; init; }
    public IReadOnlyDictionary<string, string> VariableOverrides { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public bool DryRun { get; init; }
    public bool Parallel { get; init; }
}

public sealed record BundleRunResult
{
    public required string BundleName { get; init; }
    public required string Environment { get; init; }
    public bool Succeeded => Platforms.All(platform => platform.Succeeded);
    public List<BundlePlatformResult> Platforms { get; init; } = [];
}

public sealed record BundlePlatformResult
{
    public required BundlePlatform Platform { get; init; }
    public bool Succeeded => Phases.All(phase => phase.Succeeded);
    public List<BundlePhaseResult> Phases { get; init; } = [];
    public List<BundleArtifact> Artifacts { get; init; } = [];
}

public sealed record BundlePhaseResult
{
    public required BundlePhase Phase { get; init; }
    public required bool Succeeded { get; init; }
    public bool Skipped { get; init; }
    public string? Message { get; init; }
    public List<string> Diagnostics { get; init; } = [];
}

public sealed record BundleArtifact
{
    public required string Path { get; init; }
    public required BundlePlatform Platform { get; init; }
    public required string Kind { get; init; }
    public string? ApplicationId { get; init; }
    public string? Version { get; init; }
    public string? BuildNumber { get; init; }
    public string? Sha256 { get; init; }
}

public sealed record BundleDeploymentResult
{
    public required BundleDeploymentProvider Provider { get; init; }
    public required bool Succeeded { get; init; }
    public string? ReleaseId { get; init; }
    public string? Url { get; init; }
    public string? Message { get; init; }
}
