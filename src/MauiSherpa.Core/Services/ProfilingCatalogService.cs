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
                "Startup",
                "Capture from process start until the first MAUI UI is ready.",
                [ProfilingCaptureKind.Startup],
                TimeSpan.FromMinutes(2)),
            [ProfilingScenarioKind.Interaction] = new(
                ProfilingScenarioKind.Interaction,
                "Interaction",
                "Launch the app, navigate to a starting point, then explicitly begin and stop recording.",
                [ProfilingCaptureKind.Interaction],
                TimeSpan.FromMinutes(5),
                SupportsContinuousCapture: true)
        };

    private static readonly IReadOnlyDictionary<ProfilingTargetPlatform, ProfilingPlatformCapabilities> BuiltInCapabilities =
        new Dictionary<ProfilingTargetPlatform, ProfilingPlatformCapabilities>
        {
            [ProfilingTargetPlatform.Android] = new(
                ProfilingTargetPlatform.Android,
                "Android",
                [ProfilingTargetKind.PhysicalDevice, ProfilingTargetKind.Emulator],
                [ProfilingCaptureKind.Startup, ProfilingCaptureKind.Interaction],
                [ProfilingArtifactKind.Trace, ProfilingArtifactKind.Mibc, ProfilingArtifactKind.Export],
                BuiltInScenarios.Keys.ToArray(),
                SupportsLaunchProfiling: true,
                SupportsAttachToProcess: false,
                SupportsLiveMetrics: false,
                SupportsSymbolication: false,
                Notes: "Capture uses the global maui CLI with a connected Android device or running emulator."),
            [ProfilingTargetPlatform.iOS] = new(
                ProfilingTargetPlatform.iOS,
                "iOS Simulator",
                [ProfilingTargetKind.Simulator],
                [ProfilingCaptureKind.Startup, ProfilingCaptureKind.Interaction],
                [ProfilingArtifactKind.Trace, ProfilingArtifactKind.Mibc, ProfilingArtifactKind.Export],
                BuiltInScenarios.Keys.ToArray(),
                SupportsLaunchProfiling: true,
                SupportsAttachToProcess: false,
                SupportsLiveMetrics: false,
                SupportsSymbolication: true,
                Notes: "Capture uses the global maui CLI with a booted iOS simulator.")
        };

    public Task<ProfilingCatalog> GetCatalogAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new ProfilingCatalog(
            BuiltInCapabilities.Values.ToArray(),
            BuiltInScenarios.Values.ToArray()));
    }

    public Task<ProfilingPlatformCapabilities> GetCapabilitiesAsync(
        ProfilingTargetPlatform platform,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!BuiltInCapabilities.TryGetValue(platform, out var builtInCapabilities))
            throw new ArgumentOutOfRangeException(
                nameof(platform),
                platform,
                "MAUI CLI profiling currently supports Android devices/emulators and iOS simulators.");

        return Task.FromResult(builtInCapabilities);
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
