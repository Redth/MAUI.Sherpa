using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;

namespace MauiSherpa.Core.Services;

public class ProfilingCatalogService : IProfilingCatalogService
{
    private static readonly IReadOnlyDictionary<ProfilingScenarioKind, ProfilingScenarioDefinition> BuiltInScenarios =
        new Dictionary<ProfilingScenarioKind, ProfilingScenarioDefinition>
        {
            [ProfilingScenarioKind.Launch] = new(
                ProfilingScenarioKind.Launch,
                "Launch & startup",
                "Capture cold or warm start behavior with startup, CPU, and memory signals.",
                [ProfilingCaptureKind.Startup, ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Memory],
                TimeSpan.FromMinutes(2)),
            [ProfilingScenarioKind.Interaction] = new(
                ProfilingScenarioKind.Interaction,
                "Interaction trace",
                "Focus on a bounded interaction such as tapping through a flow or completing a task.",
                [ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Rendering, ProfilingCaptureKind.Memory],
                TimeSpan.FromMinutes(5),
                SupportsContinuousCapture: true),
            [ProfilingScenarioKind.Scrolling] = new(
                ProfilingScenarioKind.Scrolling,
                "Scrolling & rendering",
                "Measure rendering smoothness and CPU pressure during scrolling-heavy experiences.",
                [ProfilingCaptureKind.Rendering, ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Memory],
                TimeSpan.FromMinutes(3),
                SupportsContinuousCapture: true),
            [ProfilingScenarioKind.BackgroundWork] = new(
                ProfilingScenarioKind.BackgroundWork,
                "Background work",
                "Profile sync, notifications, or other longer-running work that happens away from the main UI.",
                [ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Network, ProfilingCaptureKind.Energy],
                TimeSpan.FromMinutes(10),
                SupportsContinuousCapture: true),
            [ProfilingScenarioKind.MemoryInvestigation] = new(
                ProfilingScenarioKind.MemoryInvestigation,
                "Memory investigation",
                "Use memory-oriented captures to investigate leaks, spikes, and long-lived allocations.",
                [ProfilingCaptureKind.Memory, ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Logs],
                TimeSpan.FromMinutes(15),
                SupportsContinuousCapture: true)
        };

    private static readonly IReadOnlyDictionary<ProfilingTargetPlatform, ProfilingPlatformCapabilities> BuiltInCapabilities =
        new Dictionary<ProfilingTargetPlatform, ProfilingPlatformCapabilities>
        {
            [ProfilingTargetPlatform.Android] = new(
                ProfilingTargetPlatform.Android,
                "Android",
                [ProfilingTargetKind.PhysicalDevice, ProfilingTargetKind.Emulator],
                [ProfilingCaptureKind.Startup, ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Memory, ProfilingCaptureKind.Network, ProfilingCaptureKind.Rendering, ProfilingCaptureKind.Energy, ProfilingCaptureKind.SystemTrace, ProfilingCaptureKind.Logs],
                [ProfilingArtifactKind.Trace, ProfilingArtifactKind.Metrics, ProfilingArtifactKind.Screenshot, ProfilingArtifactKind.Logs, ProfilingArtifactKind.Export, ProfilingArtifactKind.Report],
                BuiltInScenarios.Keys.ToArray(),
                SupportsLaunchProfiling: true,
                SupportsAttachToProcess: true,
                SupportsLiveMetrics: true,
                SupportsSymbolication: false,
                Notes: "Initial Android abstractions assume adb-backed devices and emulators."),
            [ProfilingTargetPlatform.iOS] = new(
                ProfilingTargetPlatform.iOS,
                "iOS",
                [ProfilingTargetKind.PhysicalDevice, ProfilingTargetKind.Simulator],
                [ProfilingCaptureKind.Startup, ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Memory, ProfilingCaptureKind.Network, ProfilingCaptureKind.Rendering, ProfilingCaptureKind.Energy, ProfilingCaptureKind.SystemTrace, ProfilingCaptureKind.Logs],
                [ProfilingArtifactKind.Trace, ProfilingArtifactKind.Metrics, ProfilingArtifactKind.Screenshot, ProfilingArtifactKind.Logs, ProfilingArtifactKind.Export, ProfilingArtifactKind.Report],
                BuiltInScenarios.Keys.ToArray(),
                SupportsLaunchProfiling: true,
                SupportsAttachToProcess: true,
                SupportsLiveMetrics: true,
                SupportsSymbolication: true,
                Notes: "Initial iOS abstractions cover both physical devices and simulators."),
            [ProfilingTargetPlatform.MacCatalyst] = new(
                ProfilingTargetPlatform.MacCatalyst,
                "Mac Catalyst",
                [ProfilingTargetKind.Desktop],
                [ProfilingCaptureKind.Startup, ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Memory, ProfilingCaptureKind.Network, ProfilingCaptureKind.Rendering, ProfilingCaptureKind.Energy, ProfilingCaptureKind.SystemTrace, ProfilingCaptureKind.Logs],
                [ProfilingArtifactKind.Trace, ProfilingArtifactKind.Metrics, ProfilingArtifactKind.Logs, ProfilingArtifactKind.Export, ProfilingArtifactKind.Report],
                BuiltInScenarios.Keys.ToArray(),
                SupportsLaunchProfiling: true,
                SupportsAttachToProcess: true,
                SupportsLiveMetrics: true,
                SupportsSymbolication: true,
                Notes: "Mac Catalyst is treated as a desktop target with Apple tooling semantics."),
            [ProfilingTargetPlatform.MacOS] = new(
                ProfilingTargetPlatform.MacOS,
                "macOS",
                [ProfilingTargetKind.Desktop],
                [ProfilingCaptureKind.Startup, ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Memory, ProfilingCaptureKind.Network, ProfilingCaptureKind.Rendering, ProfilingCaptureKind.Energy, ProfilingCaptureKind.SystemTrace, ProfilingCaptureKind.Logs],
                [ProfilingArtifactKind.Trace, ProfilingArtifactKind.Metrics, ProfilingArtifactKind.Logs, ProfilingArtifactKind.Export, ProfilingArtifactKind.Report],
                BuiltInScenarios.Keys.ToArray(),
                SupportsLaunchProfiling: true,
                SupportsAttachToProcess: true,
                SupportsLiveMetrics: true,
                SupportsSymbolication: true,
                Notes: "macOS profiling is modeled as a desktop target that can evolve beyond MAUI-specific flows."),
            [ProfilingTargetPlatform.Windows] = new(
                ProfilingTargetPlatform.Windows,
                "Windows",
                [ProfilingTargetKind.Desktop],
                [ProfilingCaptureKind.Startup, ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Memory, ProfilingCaptureKind.Network, ProfilingCaptureKind.Rendering, ProfilingCaptureKind.Energy, ProfilingCaptureKind.SystemTrace, ProfilingCaptureKind.Logs],
                [ProfilingArtifactKind.Trace, ProfilingArtifactKind.Metrics, ProfilingArtifactKind.Logs, ProfilingArtifactKind.Export, ProfilingArtifactKind.Report],
                BuiltInScenarios.Keys.ToArray(),
                SupportsLaunchProfiling: true,
                SupportsAttachToProcess: true,
                SupportsLiveMetrics: true,
                SupportsSymbolication: false,
                Notes: "Windows support is modeled for desktop processes and future tooling adapters.")
        };

    private readonly IReadOnlyDictionary<ProfilingTargetPlatform, IProfilingCapabilityProvider> _capabilityProviders;

    public ProfilingCatalogService(IEnumerable<IProfilingCapabilityProvider> capabilityProviders)
    {
        _capabilityProviders = capabilityProviders
            .GroupBy(provider => provider.Platform)
            .ToDictionary(group => group.Key, group => group.Last());
    }

    public async Task<ProfilingCatalog> GetCatalogAsync(CancellationToken ct = default)
    {
        var platforms = new List<ProfilingPlatformCapabilities>();

        foreach (var platform in Enum.GetValues<ProfilingTargetPlatform>())
            platforms.Add(await GetCapabilitiesAsync(platform, ct));

        return new ProfilingCatalog(platforms, BuiltInScenarios.Values.ToArray());
    }

    public async Task<ProfilingPlatformCapabilities> GetCapabilitiesAsync(ProfilingTargetPlatform platform, CancellationToken ct = default)
    {
        if (!BuiltInCapabilities.TryGetValue(platform, out var builtInCapabilities))
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported profiling platform.");

        if (_capabilityProviders.TryGetValue(platform, out var provider))
        {
            var providerCapabilities = await provider.GetCapabilitiesAsync(ct);
            if (providerCapabilities is not null)
                return providerCapabilities;
        }

        return builtInCapabilities;
    }

    public ProfilingSessionDefinition CreateSessionDefinition(
        ProfilingTarget target,
        ProfilingScenarioKind scenario,
        string? name = null,
        IReadOnlyList<ProfilingCaptureKind>? captureKinds = null,
        string? appId = null,
        TimeSpan? duration = null,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        if (!BuiltInScenarios.TryGetValue(scenario, out var scenarioDefinition))
            throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown profiling scenario.");

        var normalizedName = string.IsNullOrWhiteSpace(name)
            ? $"{target.DisplayName} - {scenarioDefinition.DisplayName}"
            : name.Trim();

        var normalizedCaptureKinds = captureKinds is { Count: > 0 }
            ? captureKinds.Distinct().ToArray()
            : scenarioDefinition.DefaultCaptureKinds.ToArray();

        return new ProfilingSessionDefinition(
            Guid.NewGuid().ToString("N"),
            normalizedName,
            target,
            scenario,
            normalizedCaptureKinds,
            appId,
            duration ?? scenarioDefinition.SuggestedDuration,
            tags is null ? new Dictionary<string, string>() : new Dictionary<string, string>(tags),
            DateTimeOffset.UtcNow);
    }

    public ProfilingSessionValidationResult ValidateSessionDefinition(
        ProfilingSessionDefinition definition,
        ProfilingPlatformCapabilities capabilities)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.Name))
            errors.Add("A profiling session name is required.");

        if (string.IsNullOrWhiteSpace(definition.Target.Identifier))
            errors.Add("A profiling target identifier is required.");

        if (definition.Target.Platform != capabilities.Platform)
            errors.Add($"Target platform {definition.Target.Platform} does not match {capabilities.DisplayName} capabilities.");

        if (!capabilities.SupportedTargetKinds.Contains(definition.Target.Kind))
            errors.Add($"{definition.Target.Kind} targets are not supported on {capabilities.DisplayName}.");

        if (!capabilities.SupportedScenarios.Contains(definition.Scenario))
            errors.Add($"{definition.Scenario} is not supported on {capabilities.DisplayName}.");

        if (definition.CaptureKinds.Count == 0)
            errors.Add("At least one capture kind is required.");

        if (definition.Duration is { } duration && duration <= TimeSpan.Zero)
            errors.Add("Duration must be greater than zero.");

        var unsupportedCaptureKinds = definition.CaptureKinds
            .Where(kind => !capabilities.SupportedCaptureKinds.Contains(kind))
            .Distinct()
            .ToArray();

        if (unsupportedCaptureKinds.Length > 0)
            errors.Add($"Unsupported capture kinds for {capabilities.DisplayName}: {string.Join(", ", unsupportedCaptureKinds)}.");

        return new ProfilingSessionValidationResult(
            errors.Count == 0,
            errors,
            unsupportedCaptureKinds);
    }
}
