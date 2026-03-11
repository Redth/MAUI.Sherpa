using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;

namespace MauiSherpa.Core.Services;

public class ProfilingCaptureOrchestrationService : IProfilingCaptureOrchestrationService
{
    private const string ProcessIdToken = "{{PROCESS_ID}}";
    private static readonly IReadOnlySet<ProfilingCaptureKind> TraceCaptureKinds = new HashSet<ProfilingCaptureKind>
    {
        ProfilingCaptureKind.Startup,
        ProfilingCaptureKind.Cpu,
        ProfilingCaptureKind.Network,
        ProfilingCaptureKind.Rendering,
        ProfilingCaptureKind.Energy,
        ProfilingCaptureKind.SystemTrace
    };

    private readonly IProfilingCatalogService _profilingCatalogService;
    private readonly IProfilingPrerequisitesService _profilingPrerequisitesService;
    private readonly IDeviceMonitorService _deviceMonitorService;
    private readonly IPlatformService _platformService;
    private readonly IAndroidSdkSettingsService _androidSdkSettingsService;
    private readonly ILoggingService _loggingService;

    public ProfilingCaptureOrchestrationService(
        IProfilingCatalogService profilingCatalogService,
        IProfilingPrerequisitesService profilingPrerequisitesService,
        IDeviceMonitorService deviceMonitorService,
        IPlatformService platformService,
        IAndroidSdkSettingsService androidSdkSettingsService,
        ILoggingService loggingService)
    {
        _profilingCatalogService = profilingCatalogService;
        _profilingPrerequisitesService = profilingPrerequisitesService;
        _deviceMonitorService = deviceMonitorService;
        _platformService = platformService;
        _androidSdkSettingsService = androidSdkSettingsService;
        _loggingService = loggingService;
    }

    public async Task<ProfilingCapturePlan> PlanCaptureAsync(
        ProfilingSessionDefinition definition,
        ProfilingCapturePlanOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var normalizedOptions = NormalizeOptions(definition, options);
        var targetFramework = ResolveTargetFramework(definition.Target.Platform, normalizedOptions.TargetFramework);
        var workingDirectory = ResolveWorkingDirectory(normalizedOptions);
        var capabilities = await _profilingCatalogService.GetCapabilitiesAsync(definition.Target.Platform, ct);
        var definitionValidation = _profilingCatalogService.ValidateSessionDefinition(definition, capabilities);
        var prerequisites = await _profilingPrerequisitesService.GetPrerequisitesAsync(
            definition.Target.Platform,
            definition.CaptureKinds,
            workingDirectory,
            ct);

        var errors = new List<string>(definitionValidation.Errors);
        var warnings = new List<string>();
        var commands = new List<ProfilingCommandStep>();
        var runtimeBindings = new List<ProfilingRuntimeBinding>();
        var expectedArtifacts = new List<ProfilingArtifactMetadata>();
        var metadata = CreatePlanMetadata(definition, normalizedOptions, targetFramework);

        AppendPrerequisiteFindings(prerequisites, errors, warnings);

        if (normalizedOptions.LaunchMode == ProfilingCaptureLaunchMode.Launch &&
            string.IsNullOrWhiteSpace(normalizedOptions.ProjectPath))
        {
            errors.Add("A project path is required to plan build and launch steps.");
        }

        var isTargetCurrentlyAvailable = IsTargetCurrentlyAvailable(definition.Target);
        if (!isTargetCurrentlyAvailable && RequiresConnectedTarget(definition.Target))
        {
            warnings.Add($"Target '{definition.Target.Identifier}' is not currently present in the connected device snapshot.");
        }

        if (normalizedOptions.LaunchMode == ProfilingCaptureLaunchMode.Attach &&
            !capabilities.SupportsAttachToProcess)
        {
            errors.Add($"{capabilities.DisplayName} capabilities do not support attach flows.");
        }

        var diagnostics = BuildDiagnosticsConfiguration(definition.Target, normalizedOptions, _platformService.IsWindows);
        var traceArtifactPath = Path.Combine(normalizedOptions.OutputDirectory!, "trace.nettrace");
        var gcdumpArtifactPath = Path.Combine(normalizedOptions.OutputDirectory!, "memory.gcdump");
        var logsArtifactPath = Path.Combine(normalizedOptions.OutputDirectory!, "logs.txt");

        var androidSdkPath = definition.Target.Platform == ProfilingTargetPlatform.Android
            ? await TryGetAndroidSdkPathAsync()
            : null;

        // Modern dotnet-trace/dotnet-gcdump support --dsrouter natively, so we no longer
        // need a standalone dotnet-dsrouter process. However, only ONE tool can use --dsrouter
        // at a time because each starts its own dsrouter instance. When both trace and gcdump
        // are requested on a mobile target, we fall back to a standalone dsrouter process and
        // have both tools connect via --diagnostic-port using the IPC address instead.
        var hasTraceCapture = definition.CaptureKinds.Any(kind => TraceCaptureKinds.Contains(kind));
        var hasMemoryCapture = definition.CaptureKinds.Contains(ProfilingCaptureKind.Memory);
        var hasLogCapture = definition.CaptureKinds.Contains(ProfilingCaptureKind.Logs);
        var dsrouterPlatformArg = GetDsRouterPlatformArg(definition.Target);
        var isMobileTarget = dsrouterPlatformArg is not null;
        // Both trace and gcdump are now on-demand, so always use standalone dsrouter
        // on mobile when either is requested — they need to share the diagnostic port.
        var needsStandaloneDsRouter = isMobileTarget && (hasTraceCapture || hasMemoryCapture);

        // If we need standalone dsrouter, clear the inline arg so capture steps use --diagnostic-port instead
        if (needsStandaloneDsRouter)
            dsrouterPlatformArg = null;

        var preLaunchCaptureSteps = new List<ProfilingCommandStep>();
        var postLaunchCaptureSteps = new List<ProfilingCommandStep>();

        // When both trace and gcdump target a mobile platform, start a standalone dsrouter
        // and have both tools connect to it via --diagnostic-port using the IPC socket.
        if (needsStandaloneDsRouter && diagnostics is not null)
        {
            commands.Add(CreateDsRouterStep(definition, diagnostics, normalizedOptions, androidSdkPath));
        }

        if (hasTraceCapture)
        {
            // Don't add traceStep to pipeline — trace is an on-demand action
            // triggered by the user via Start Trace / Stop Trace buttons.
            // This prevents trace from auto-starting and competing with gcdump
            // for the diagnostic port.
            var (_, traceArtifact) = CreateTraceCaptureStep(
                definition,
                normalizedOptions,
                dsrouterPlatformArg,
                traceArtifactPath,
                runtimeBindings,
                needsStandaloneDsRouter ? diagnostics?.IpcAddress : null,
                androidSdkPath);

            expectedArtifacts.Add(traceArtifact);
        }

        if (normalizedOptions.LaunchMode == ProfilingCaptureLaunchMode.Launch)
        {
            commands.AddRange(preLaunchCaptureSteps);

            // Android requires adb setup steps before the app launches:
            // - Physical devices need adb reverse for port forwarding
            // - All Android targets need debug.mono.profile system property set
            if (definition.Target.Platform == ProfilingTargetPlatform.Android && diagnostics is not null)
            {
                commands.AddRange(CreateAndroidDiagnosticSetupSteps(
                    definition.Target, diagnostics, normalizedOptions, androidSdkPath));
            }

            commands.Add(CreateLaunchStep(definition, normalizedOptions, targetFramework, workingDirectory, diagnostics, androidSdkPath));
        }

        if (!isMobileTarget &&
            normalizedOptions.ProcessId is null &&
            (hasTraceCapture || hasMemoryCapture))
        {
            commands.Add(CreateProcessDiscoveryStep(definition, normalizedOptions));
            if (runtimeBindings.All(binding => binding.Token != ProcessIdToken))
            {
                runtimeBindings.Add(new ProfilingRuntimeBinding(
                    ProcessIdToken,
                    "Resolve the local desktop process id after the app is running.",
                    ExampleValue: "12345"));
            }
        }

        if (hasMemoryCapture)
        {
            var (_, memoryArtifact) = CreateMemoryCaptureStep(
                definition,
                normalizedOptions,
                dsrouterPlatformArg,
                gcdumpArtifactPath,
                runtimeBindings,
                needsStandaloneDsRouter ? diagnostics?.IpcAddress : null,
                androidSdkPath,
                hasTraceCapture);

            // Don't add memoryStep to pipeline — GC dump is a point-in-time snapshot
            // that users trigger on demand via the capture UI, not auto-run.
            expectedArtifacts.Add(memoryArtifact);
        }

        if (hasLogCapture)
        {
            var logStep = CreateLogCaptureStep(definition, normalizedOptions, logsArtifactPath, androidSdkPath);
            if (logStep is not null)
            {
                postLaunchCaptureSteps.Add(logStep);
                expectedArtifacts.Add(new ProfilingArtifactMetadata(
                    Id: $"{definition.Id}-logs",
                    SessionId: definition.Id,
                    Kind: ProfilingArtifactKind.Logs,
                    DisplayName: "Streaming logs",
                    FileName: Path.GetFileName(logsArtifactPath),
                    RelativePath: logsArtifactPath,
                    ContentType: "text/plain",
                    CreatedAt: DateTimeOffset.UtcNow,
                    Properties: CreateArtifactProperties(definition, "logs")));
            }
            else
            {
                warnings.Add($"Logs capture planning is not yet modeled for {capabilities.DisplayName} {definition.Target.Kind} targets.");
            }
        }

        commands.AddRange(postLaunchCaptureSteps);

        var validation = new ProfilingPlanValidation(
            Errors: errors
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Warnings: warnings
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());

        if (!validation.IsValid)
        {
            _loggingService.LogDebug(
                $"Profiling capture plan for session '{definition.Id}' contains validation issues: {string.Join(" | ", validation.Errors)}");
        }

        return new ProfilingCapturePlan(
            definition,
            capabilities,
            normalizedOptions,
            _platformService.PlatformName,
            targetFramework,
            normalizedOptions.OutputDirectory!,
            workingDirectory,
            isTargetCurrentlyAvailable,
            diagnostics,
            prerequisites,
            validation,
            runtimeBindings.ToArray(),
            commands.ToArray(),
            expectedArtifacts.ToArray(),
            metadata);
    }

    private static ProfilingCapturePlanOptions NormalizeOptions(
        ProfilingSessionDefinition definition,
        ProfilingCapturePlanOptions? options)
    {
        var normalized = options ?? new ProfilingCapturePlanOptions();
        var configuration = string.IsNullOrWhiteSpace(normalized.Configuration) ? "Release" : normalized.Configuration.Trim();
        var projectPath = string.IsNullOrWhiteSpace(normalized.ProjectPath) ? null : normalized.ProjectPath.Trim();
        var workingDirectory = string.IsNullOrWhiteSpace(normalized.WorkingDirectory) ? null : normalized.WorkingDirectory.Trim();
        var effectiveWorkingDirectory = workingDirectory ?? (string.IsNullOrWhiteSpace(projectPath) ? null : Path.GetDirectoryName(projectPath));
        var outputDirectory = string.IsNullOrWhiteSpace(normalized.OutputDirectory)
            ? BuildDefaultOutputDirectory(normalized.ProjectPath, definition.CreatedAt)
            : normalized.OutputDirectory.Trim();

        // Make the output directory absolute so that artifact collection in the runner
        // (which may run with a different CWD) can always find the files.
        if (!Path.IsPathRooted(outputDirectory))
        {
            var resolveBase = effectiveWorkingDirectory ?? Directory.GetCurrentDirectory();
            outputDirectory = Path.GetFullPath(Path.Combine(resolveBase, outputDirectory));
        }
        var additionalBuildProperties = normalized.AdditionalBuildProperties is null
            ? null
            : new Dictionary<string, string>(normalized.AdditionalBuildProperties, StringComparer.OrdinalIgnoreCase);

        return normalized with
        {
            ProjectPath = projectPath,
            Configuration = configuration,
            WorkingDirectory = effectiveWorkingDirectory,
            OutputDirectory = outputDirectory,
            AdditionalBuildProperties = additionalBuildProperties
        };
    }

    private static string BuildDefaultOutputDirectory(string? projectPath, DateTimeOffset createdAt)
    {
        var projectName = "session";
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            projectName = Path.GetFileNameWithoutExtension(projectPath);
        }

        var dateStr = createdAt == default
            ? DateTime.Now.ToString("yyyy-MM-dd")
            : createdAt.LocalDateTime.ToString("yyyy-MM-dd");
        var baseDir = Path.Combine("artifacts", "profiling", projectName);

        var runNumber = 1;
        if (Directory.Exists(baseDir))
        {
            var prefix = $"{dateStr}-";
            var existingRuns = Directory.GetDirectories(baseDir)
                .Select(d => Path.GetFileName(d))
                .Where(name => name!.StartsWith(prefix, StringComparison.Ordinal))
                .Select(name => {
                    var suffix = name!.Substring(prefix.Length);
                    return int.TryParse(suffix, out var n) ? n : 0;
                })
                .Where(n => n > 0)
                .ToList();

            if (existingRuns.Count > 0)
                runNumber = existingRuns.Max() + 1;
        }

        return Path.Combine(baseDir, $"{dateStr}-{runNumber}");
    }

    private static string ResolveTargetFramework(ProfilingTargetPlatform platform, string? targetFrameworkOverride) =>
        string.IsNullOrWhiteSpace(targetFrameworkOverride)
            ? platform switch
            {
                ProfilingTargetPlatform.Android => "net10.0-android",
                ProfilingTargetPlatform.iOS => "net10.0-ios",
                ProfilingTargetPlatform.MacCatalyst => "net10.0-maccatalyst",
                ProfilingTargetPlatform.MacOS => "net10.0-macos",
                ProfilingTargetPlatform.Windows => "net10.0-windows10.0.19041.0",
                _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported profiling platform.")
            }
            : targetFrameworkOverride.Trim();

    private static string? ResolveWorkingDirectory(ProfilingCapturePlanOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.WorkingDirectory))
            return options.WorkingDirectory;

        return string.IsNullOrWhiteSpace(options.ProjectPath)
            ? null
            : Path.GetDirectoryName(options.ProjectPath);
    }

    private static IReadOnlyDictionary<string, string> CreatePlanMetadata(
        ProfilingSessionDefinition definition,
        ProfilingCapturePlanOptions options,
        string targetFramework)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sessionId"] = definition.Id,
            ["targetPlatform"] = definition.Target.Platform.ToString(),
            ["targetKind"] = definition.Target.Kind.ToString(),
            ["targetIdentifier"] = definition.Target.Identifier,
            ["configuration"] = options.Configuration,
            ["launchMode"] = options.LaunchMode.ToString(),
            ["targetFramework"] = targetFramework,
            ["outputDirectory"] = options.OutputDirectory ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(options.ProjectPath))
            metadata["projectPath"] = options.ProjectPath;

        return metadata;
    }

    private static void AppendPrerequisiteFindings(
        ProfilingPrerequisiteReport prerequisites,
        List<string> errors,
        List<string> warnings)
    {
        foreach (var check in prerequisites.Checks.Where(check => check.IsRequired && check.Status == DependencyStatusType.Error))
        {
            errors.Add(check.Message ?? $"{check.Name} is required for profiling orchestration.");
        }
    }

    private bool IsTargetCurrentlyAvailable(ProfilingTarget target)
    {
        var snapshot = _deviceMonitorService.Current;

        return target.Platform switch
        {
            ProfilingTargetPlatform.Android when target.Kind == ProfilingTargetKind.PhysicalDevice
                => snapshot.AndroidDevices.Any(device => string.Equals(device.Serial, target.Identifier, StringComparison.OrdinalIgnoreCase)),
            ProfilingTargetPlatform.Android when target.Kind == ProfilingTargetKind.Emulator
                => snapshot.AndroidEmulators.Any(device => string.Equals(device.Serial, target.Identifier, StringComparison.OrdinalIgnoreCase)),
            ProfilingTargetPlatform.iOS when target.Kind == ProfilingTargetKind.PhysicalDevice
                => snapshot.ApplePhysicalDevices.Any(device => string.Equals(device.Identifier, target.Identifier, StringComparison.OrdinalIgnoreCase)),
            ProfilingTargetPlatform.iOS when target.Kind == ProfilingTargetKind.Simulator
                => snapshot.BootedSimulators.Any(device => string.Equals(device.Identifier, target.Identifier, StringComparison.OrdinalIgnoreCase)),
            _ => true
        };
    }

    private static bool RequiresConnectedTarget(ProfilingTarget target) =>
        (target.Platform == ProfilingTargetPlatform.Android &&
         target.Kind is ProfilingTargetKind.PhysicalDevice or ProfilingTargetKind.Emulator)
        || (target.Platform == ProfilingTargetPlatform.iOS &&
            target.Kind is ProfilingTargetKind.PhysicalDevice or ProfilingTargetKind.Simulator);

    private static ProfilingDiagnosticConfiguration? BuildDiagnosticsConfiguration(
        ProfilingTarget target,
        ProfilingCapturePlanOptions options,
        bool isWindowsHost)
    {
        if (target.Platform is not ProfilingTargetPlatform.Android and not ProfilingTargetPlatform.iOS)
            return null;

        var ipcAddress = isWindowsHost
            ? $@"\\.\pipe\maui-sherpa-profile-{Guid.NewGuid():N}"
            : Path.Combine(Path.GetTempPath(), $"ms-prof-{Guid.NewGuid().ToString("N")[..8]}.sock");
        var tcpEndpoint = $"127.0.0.1:{options.DiagnosticPort}";

        return target.Platform switch
        {
            ProfilingTargetPlatform.Android => new ProfilingDiagnosticConfiguration(
                Address: target.Kind == ProfilingTargetKind.Emulator ? "10.0.2.2" : "127.0.0.1",
                Port: options.DiagnosticPort,
                ListenMode: ProfilingDiagnosticListenMode.Connect,
                SuspendOnStartup: options.SuspendAtStartup,
                RequiresDsRouter: true,
                DsRouterMode: ProfilingDsRouterMode.ServerServer,
                DsRouterPortForwardPlatform: "Android",
                IpcAddress: ipcAddress,
                TcpEndpoint: tcpEndpoint),
            ProfilingTargetPlatform.iOS => new ProfilingDiagnosticConfiguration(
                Address: "127.0.0.1",
                Port: options.DiagnosticPort,
                ListenMode: ProfilingDiagnosticListenMode.Listen,
                SuspendOnStartup: options.SuspendAtStartup,
                RequiresDsRouter: true,
                DsRouterMode: ProfilingDsRouterMode.ServerClient,
                DsRouterPortForwardPlatform: target.Kind == ProfilingTargetKind.PhysicalDevice ? "iOS" : null,
                IpcAddress: ipcAddress,
                TcpEndpoint: tcpEndpoint),
            _ => null
        };
    }

    /// <summary>
    /// Creates a standalone dotnet-dsrouter process step. Kept as a fallback for environments
    /// where the integrated --dsrouter flag in dotnet-trace/dotnet-gcdump is not available.
    /// In the normal flow, --dsrouter is passed directly to the capture tools instead.
    /// </summary>
    private static ProfilingCommandStep CreateDsRouterStep(
        ProfilingSessionDefinition definition,
        ProfilingDiagnosticConfiguration diagnostics,
        ProfilingCapturePlanOptions options,
        string? androidSdkPath)
    {
        var arguments = new List<string>
        {
            diagnostics.DsRouterMode == ProfilingDsRouterMode.ServerServer ? "server-server" : "server-client",
            "-ipcs",
            diagnostics.IpcAddress,
            diagnostics.DsRouterMode == ProfilingDsRouterMode.ServerServer ? "-tcps" : "-tcpc",
            diagnostics.TcpEndpoint,
            "-rt",
            Math.Max(30, (int)Math.Ceiling((definition.Duration ?? TimeSpan.FromMinutes(5)).TotalSeconds)).ToString()
        };

        if (!string.IsNullOrWhiteSpace(diagnostics.DsRouterPortForwardPlatform))
        {
            arguments.Add("--forward-port");
            arguments.Add(diagnostics.DsRouterPortForwardPlatform);
        }

        var environment = BuildAndroidEnvironment(definition.Target, androidSdkPath);

        return new ProfilingCommandStep(
            Id: "start-dsrouter",
            Kind: ProfilingCommandStepKind.Prepare,
            DisplayName: "Start diagnostics router",
            Description: "Start dotnet-dsrouter so local diagnostic tools can talk to the remote mobile runtime.",
            Command: "dotnet-dsrouter",
            Arguments: arguments,
            WorkingDirectory: options.WorkingDirectory,
            Environment: environment,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tool"] = "dotnet-dsrouter",
                ["mode"] = diagnostics.DsRouterMode.ToString(),
                ["ipcAddress"] = diagnostics.IpcAddress,
                ["tcpEndpoint"] = diagnostics.TcpEndpoint,
                ["portForward"] = diagnostics.DsRouterPortForwardPlatform ?? string.Empty
            },
            IsLongRunning: true,
            RequiresManualStop: true,
            ReadyOutputPattern: "Starting IPC server");
    }

    private static ProfilingCommandStep CreateLaunchStep(
        ProfilingSessionDefinition definition,
        ProfilingCapturePlanOptions options,
        string targetFramework,
        string? workingDirectory,
        ProfilingDiagnosticConfiguration? diagnostics,
        string? androidSdkPath = null)
    {
        var arguments = new List<string>
        {
            "build"
        };

        if (!string.IsNullOrWhiteSpace(options.ProjectPath))
            arguments.Add(options.ProjectPath);

        arguments.Add("-t:Run");
        arguments.Add("-c");
        arguments.Add(options.Configuration);
        arguments.Add("-f");
        arguments.Add(targetFramework);

        // Android requires AndroidEnableProfiler=true to include the Mono diagnostic component.
        // The runtime diagnostic port is configured via adb system properties, not MSBuild properties.
        if (diagnostics is not null && definition.Target.Platform == ProfilingTargetPlatform.Android)
        {
            arguments.Add("-p:AndroidEnableProfiler=true");
        }

        if (options.AdditionalBuildProperties is not null)
        {
            foreach (var buildProperty in options.AdditionalBuildProperties.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                arguments.Add($"-p:{buildProperty.Key}={buildProperty.Value}");
            }
        }

        var environment = definition.Target.Platform == ProfilingTargetPlatform.Android
            ? BuildAndroidEnvironment(definition.Target, androidSdkPath)
            : null;

        return new ProfilingCommandStep(
            Id: "build-and-run",
            Kind: ProfilingCommandStepKind.Launch,
            DisplayName: "Build and run target app",
            Description: $"Build and launch {definition.Target.DisplayName} using {targetFramework}.",
            Command: "dotnet",
            Arguments: arguments,
            WorkingDirectory: workingDirectory,
            Environment: environment,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tool"] = "dotnet",
                ["targetFramework"] = targetFramework,
                ["configuration"] = options.Configuration,
                ["targetIdentifier"] = definition.Target.Identifier,
                ["launchMode"] = options.LaunchMode.ToString()
            },
            IsLongRunning: true,
            RequiresManualStop: definition.Target.Platform is ProfilingTargetPlatform.MacCatalyst or ProfilingTargetPlatform.MacOS or ProfilingTargetPlatform.Windows,
            CanRunParallel: true,
            StopTrigger: ProfilingStopTrigger.OnPipelineStop,
            ReadyOutputPattern: "Build succeeded");
    }

    /// <summary>
    /// Creates Android-specific setup steps that must run before the app launches:
    /// 1. For physical devices: adb reverse to forward the diagnostic TCP port
    /// 2. adb shell setprop to configure the Mono diagnostic port on the device/emulator
    /// </summary>
    private static List<ProfilingCommandStep> CreateAndroidDiagnosticSetupSteps(
        ProfilingTarget target,
        ProfilingDiagnosticConfiguration diagnostics,
        ProfilingCapturePlanOptions options,
        string? androidSdkPath)
    {
        var steps = new List<ProfilingCommandStep>();
        var environment = BuildAndroidEnvironment(target, androidSdkPath);
        var suspendMode = diagnostics.SuspendOnStartup ? "suspend" : "nosuspend";

        // Physical devices need adb reverse to forward the TCP port from device to host
        if (target.Kind == ProfilingTargetKind.PhysicalDevice)
        {
            steps.Add(new ProfilingCommandStep(
                Id: "setup-adb-reverse",
                Kind: ProfilingCommandStepKind.Prepare,
                DisplayName: "Forward diagnostic port",
                Description: $"Set up adb reverse port forwarding so the device can reach the host diagnostic router on port {diagnostics.Port}.",
                Command: "adb",
                Arguments: ["reverse", $"tcp:{diagnostics.Port}", $"tcp:{diagnostics.Port + 1}"],
                WorkingDirectory: options.WorkingDirectory,
                Environment: environment,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tool"] = "adb",
                    ["port"] = diagnostics.Port.ToString()
                }));
        }

        // Set the debug.mono.profile system property so the app runtime connects to the diagnostic router
        var diagnosticAddress = $"{diagnostics.Address}:{diagnostics.Port},{suspendMode},connect";
        steps.Add(new ProfilingCommandStep(
            Id: "setup-diagnostic-port",
            Kind: ProfilingCommandStepKind.Prepare,
            DisplayName: "Configure diagnostic port",
            Description: $"Set Android system property debug.mono.profile to '{diagnosticAddress}' so the app runtime connects to the diagnostic router.",
            Command: "adb",
            Arguments: ["shell", "setprop", "debug.mono.profile", diagnosticAddress],
            WorkingDirectory: options.WorkingDirectory,
            Environment: environment,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tool"] = "adb",
                ["diagnosticAddress"] = diagnosticAddress,
                ["suspendMode"] = suspendMode
            }));

        return steps;
    }

    private static ProfilingCommandStep CreateProcessDiscoveryStep(
        ProfilingSessionDefinition definition,
        ProfilingCapturePlanOptions options)
    {
        return new ProfilingCommandStep(
            Id: "discover-process-id",
            Kind: ProfilingCommandStepKind.DiscoverProcess,
            DisplayName: "Discover target process id",
            Description: $"List local .NET processes and bind {ProcessIdToken} to the running {definition.Target.DisplayName} process before attaching.",
            Command: "dotnet-trace",
            Arguments: ["ps"],
            WorkingDirectory: options.WorkingDirectory,
            RequiredRuntimeBindings: [ProcessIdToken],
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tool"] = "dotnet-trace",
                ["runtimeBinding"] = ProcessIdToken
            });
    }

    private static (ProfilingCommandStep Step, ProfilingArtifactMetadata Artifact) CreateTraceCaptureStep(
        ProfilingSessionDefinition definition,
        ProfilingCapturePlanOptions options,
        string? dsrouterPlatformArg,
        string traceArtifactPath,
        List<ProfilingRuntimeBinding> runtimeBindings,
        string? diagnosticPortAddress = null,
        string? androidSdkPath = null)
    {
        var traceKinds = definition.CaptureKinds
            .Where(kind => TraceCaptureKinds.Contains(kind))
            .Select(kind => kind.ToString())
            .ToArray();
        var arguments = new List<string>
        {
            "collect"
        };

        if (dsrouterPlatformArg is not null)
        {
            arguments.Add("--dsrouter");
            arguments.Add(dsrouterPlatformArg);
        }
        else if (diagnosticPortAddress is not null)
        {
            // Connect to a standalone dsrouter via its IPC address (connect mode, not listen)
            arguments.Add("--diagnostic-port");
            arguments.Add($"{diagnosticPortAddress},connect");
        }
        else
        {
            arguments.Add("--process-id");
            arguments.Add(options.ProcessId?.ToString() ?? ProcessIdToken);
            if (options.ProcessId is null)
            {
                runtimeBindings.Add(new ProfilingRuntimeBinding(
                    ProcessIdToken,
                    "Resolve the process id before starting dotnet-trace.",
                    ExampleValue: "12345"));
            }
        }

        arguments.Add("--output");
        arguments.Add(traceArtifactPath);

        // Map capture kinds to dotnet-trace profiles for meaningful data.
        // "dotnet-sampled-thread-time" samples managed stacks at ~100Hz (works on all platforms).
        // "cpu-sampling" and "thread-time" are Linux-only (collect-linux).
        var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in definition.CaptureKinds.Where(k => TraceCaptureKinds.Contains(k)))
        {
            switch (kind)
            {
                case ProfilingCaptureKind.Cpu:
                case ProfilingCaptureKind.Startup:
                    profiles.Add("dotnet-sampled-thread-time");
                    break;
                case ProfilingCaptureKind.Rendering:
                case ProfilingCaptureKind.Network:
                case ProfilingCaptureKind.Energy:
                case ProfilingCaptureKind.SystemTrace:
                    profiles.Add("dotnet-common");
                    break;
            }
        }
        if (profiles.Count == 0)
            profiles.Add("dotnet-sampled-thread-time");
        arguments.Add("--profile");
        arguments.Add(string.Join(",", profiles));

        // Add JIT/Loader provider flags for managed symbol resolution in speedscope.
        // 0x10000018 = JitTracing | NGenTracing | Loader keywords, Verbose level (5).
        arguments.Add("--providers");
        arguments.Add("Microsoft-Windows-DotNETRuntime:0x10000018:5");

        var dependsOn = new List<string>();
        if (diagnosticPortAddress is not null)
            dependsOn.Add("start-dsrouter");
        if (dsrouterPlatformArg is null && diagnosticPortAddress is null && options.ProcessId is null)
            dependsOn.Add("discover-process-id");

        return (
            new ProfilingCommandStep(
                Id: "capture-trace",
                Kind: ProfilingCommandStepKind.Capture,
                DisplayName: "Collect trace",
                Description: $"Collect a trace for {string.Join(", ", traceKinds)} captures.",
                Command: "dotnet-trace",
                Arguments: arguments,
                WorkingDirectory: options.WorkingDirectory,
                Environment: (dsrouterPlatformArg is not null || diagnosticPortAddress is not null)
                    ? BuildAndroidEnvironment(definition.Target, androidSdkPath) : null,
                DependsOn: dependsOn.Count > 0 ? dependsOn : null,
                RequiredRuntimeBindings: dsrouterPlatformArg is null && diagnosticPortAddress is null && options.ProcessId is null ? [ProcessIdToken] : null,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tool"] = "dotnet-trace",
                    ["captureKinds"] = string.Join(",", traceKinds),
                    ["output"] = traceArtifactPath
                },
                IsLongRunning: true,
                RequiresManualStop: true,
                CanRunParallel: true,
                StopTrigger: ProfilingStopTrigger.ManualStop,
                ReadyOutputPattern: "Process"),
            new ProfilingArtifactMetadata(
                Id: $"{definition.Id}-trace",
                SessionId: definition.Id,
                Kind: ProfilingArtifactKind.Trace,
                DisplayName: "Trace capture",
                FileName: Path.GetFileName(traceArtifactPath),
                RelativePath: traceArtifactPath,
                ContentType: "application/json",
                CreatedAt: DateTimeOffset.UtcNow,
                Properties: CreateArtifactProperties(definition, "trace")));
    }

    private static (ProfilingCommandStep Step, ProfilingArtifactMetadata Artifact) CreateMemoryCaptureStep(
        ProfilingSessionDefinition definition,
        ProfilingCapturePlanOptions options,
        string? dsrouterPlatformArg,
        string gcdumpArtifactPath,
        List<ProfilingRuntimeBinding> runtimeBindings,
        string? diagnosticPortAddress = null,
        string? androidSdkPath = null,
        bool hasTraceCapture = false)
    {
        var arguments = new List<string>
        {
            "collect"
        };

        if (dsrouterPlatformArg is not null)
        {
            arguments.Add("--dsrouter");
            arguments.Add(dsrouterPlatformArg);
        }
        else if (diagnosticPortAddress is not null)
        {
            // Connect to a standalone dsrouter via its IPC address (connect mode, not listen)
            arguments.Add("--diagnostic-port");
            arguments.Add($"{diagnosticPortAddress},connect");
        }
        else
        {
            arguments.Add("--process-id");
            arguments.Add(options.ProcessId?.ToString() ?? ProcessIdToken);
            if (options.ProcessId is null && runtimeBindings.All(binding => binding.Token != ProcessIdToken))
            {
                runtimeBindings.Add(new ProfilingRuntimeBinding(
                    ProcessIdToken,
                    "Resolve the process id before collecting a GC dump.",
                    ExampleValue: "12345"));
            }
        }

        arguments.Add("-o");
        arguments.Add(gcdumpArtifactPath);

        var dependsOn = new List<string>();
        if (options.LaunchMode == ProfilingCaptureLaunchMode.Launch)
            dependsOn.Add("build-and-run");
        if (diagnosticPortAddress is not null)
            dependsOn.Add("start-dsrouter");
        if (dsrouterPlatformArg is null && diagnosticPortAddress is null && options.ProcessId is null)
            dependsOn.Add("discover-process-id");
        // When both trace and memory are requested, wait for the trace step to
        // establish its diagnostic port connection before collecting the GC dump.
        if (hasTraceCapture)
            dependsOn.Add("capture-trace");

        return (
            new ProfilingCommandStep(
                Id: "capture-memory",
                Kind: ProfilingCommandStepKind.CollectArtifacts,
                DisplayName: "Collect GC dump",
                Description: "Collect a managed memory dump using dotnet-gcdump.",
                Command: "dotnet-gcdump",
                Arguments: arguments,
                WorkingDirectory: options.WorkingDirectory,
                Environment: (dsrouterPlatformArg is not null || diagnosticPortAddress is not null)
                    ? BuildAndroidEnvironment(definition.Target, androidSdkPath) : null,
                DependsOn: dependsOn.Count > 0 ? dependsOn : null,
                RequiredRuntimeBindings: dsrouterPlatformArg is null && diagnosticPortAddress is null && options.ProcessId is null ? [ProcessIdToken] : null,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tool"] = "dotnet-gcdump",
                    ["output"] = gcdumpArtifactPath
                }),
            new ProfilingArtifactMetadata(
                Id: $"{definition.Id}-memory",
                SessionId: definition.Id,
                Kind: ProfilingArtifactKind.Export,
                DisplayName: "GC dump",
                FileName: Path.GetFileName(gcdumpArtifactPath),
                RelativePath: gcdumpArtifactPath,
                ContentType: "application/octet-stream",
                CreatedAt: DateTimeOffset.UtcNow,
                Properties: CreateArtifactProperties(definition, "memory")));
    }

    private static ProfilingCommandStep? CreateLogCaptureStep(
        ProfilingSessionDefinition definition,
        ProfilingCapturePlanOptions options,
        string logsArtifactPath,
        string? androidSdkPath)
    {
        switch (definition.Target.Platform, definition.Target.Kind)
        {
            case (ProfilingTargetPlatform.Android, ProfilingTargetKind.PhysicalDevice):
            case (ProfilingTargetPlatform.Android, ProfilingTargetKind.Emulator):
                Dictionary<string, string>? environment = null;
                if (!string.IsNullOrWhiteSpace(androidSdkPath))
                {
                    environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ANDROID_HOME"] = androidSdkPath
                    };
                }

                return new ProfilingCommandStep(
                    Id: "capture-logs",
                    Kind: ProfilingCommandStepKind.Capture,
                    DisplayName: "Stream Android logs",
                    Description: $"Stream adb logcat output for {definition.Target.DisplayName}. Redirect output to {logsArtifactPath}.",
                    Command: "adb",
                    Arguments: ["-s", definition.Target.Identifier, "logcat", "-v", "threadtime"],
                    WorkingDirectory: options.WorkingDirectory,
                    Environment: environment,
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["tool"] = "adb",
                        ["outputHint"] = logsArtifactPath
                    },
                    IsLongRunning: true,
                    RequiresManualStop: true,
                    CanRunParallel: true,
                    StopTrigger: ProfilingStopTrigger.OnPipelineStop);

            case (ProfilingTargetPlatform.iOS, ProfilingTargetKind.Simulator):
                return new ProfilingCommandStep(
                    Id: "capture-logs",
                    Kind: ProfilingCommandStepKind.Capture,
                    DisplayName: "Stream simulator logs",
                    Description: $"Stream simulator logs for {definition.Target.DisplayName}. Redirect output to {logsArtifactPath}.",
                    Command: "xcrun",
                    Arguments: ["simctl", "spawn", definition.Target.Identifier, "log", "stream", "--style", "ndjson", "--level", "debug"],
                    WorkingDirectory: options.WorkingDirectory,
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["tool"] = "xcrun",
                        ["outputHint"] = logsArtifactPath
                    },
                    IsLongRunning: true,
                    RequiresManualStop: true,
                    CanRunParallel: true,
                    StopTrigger: ProfilingStopTrigger.OnPipelineStop);

            case (ProfilingTargetPlatform.iOS, ProfilingTargetKind.PhysicalDevice):
                return new ProfilingCommandStep(
                    Id: "capture-logs",
                    Kind: ProfilingCommandStepKind.Capture,
                    DisplayName: "Stream device logs",
                    Description: $"Stream physical device logs for {definition.Target.DisplayName}. Redirect output to {logsArtifactPath}.",
                    Command: "idevicesyslog",
                    Arguments: ["-u", definition.Target.Identifier],
                    WorkingDirectory: options.WorkingDirectory,
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["tool"] = "idevicesyslog",
                        ["outputHint"] = logsArtifactPath,
                        ["alternativeTool"] = "pymobiledevice3 syslog live --udid <udid>"
                    },
                    IsLongRunning: true,
                    RequiresManualStop: true,
                    CanRunParallel: true,
                    StopTrigger: ProfilingStopTrigger.OnPipelineStop);

            default:
                return null;
        }
    }

    private async Task<string?> TryGetAndroidSdkPathAsync()
    {
        try
        {
            return await _androidSdkSettingsService.GetEffectiveSdkPathAsync();
        }
        catch (Exception ex)
        {
            _loggingService.LogDebug($"Failed to resolve Android SDK path for profiling orchestration: {ex.Message}");
            return null;
        }
    }

    private static string? GetDsRouterPlatformArg(ProfilingTarget target) =>
        (target.Platform, target.Kind) switch
        {
            (ProfilingTargetPlatform.Android, ProfilingTargetKind.Emulator) => "android-emu",
            (ProfilingTargetPlatform.Android, ProfilingTargetKind.PhysicalDevice) => "android",
            (ProfilingTargetPlatform.iOS, ProfilingTargetKind.Simulator) => "ios-sim",
            (ProfilingTargetPlatform.iOS, ProfilingTargetKind.PhysicalDevice) => "ios",
            _ => null
        };

    /// <summary>
    /// Build environment variables for Android targets so that adb/dsrouter target
    /// the correct device when multiple devices or emulators are connected.
    /// </summary>
    private static Dictionary<string, string>? BuildAndroidEnvironment(
        ProfilingTarget target,
        string? androidSdkPath)
    {
        if (target.Platform != ProfilingTargetPlatform.Android)
            return null;

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(androidSdkPath))
            env["ANDROID_HOME"] = androidSdkPath;
        if (!string.IsNullOrWhiteSpace(target.Identifier))
            env["ANDROID_SERIAL"] = target.Identifier;
        return env.Count > 0 ? env : null;
    }

    private static IReadOnlyDictionary<string, string> CreateArtifactProperties(
        ProfilingSessionDefinition definition,
        string category)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["targetPlatform"] = definition.Target.Platform.ToString(),
            ["targetIdentifier"] = definition.Target.Identifier,
            ["scenario"] = definition.Scenario.ToString(),
            ["category"] = category
        };
    }
}
