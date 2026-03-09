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
        var traceArtifactPath = Path.Combine(normalizedOptions.OutputDirectory!, $"{definition.Id}-trace.speedscope.json");
        var gcdumpArtifactPath = Path.Combine(normalizedOptions.OutputDirectory!, $"{definition.Id}-memory.gcdump");
        var logsArtifactPath = Path.Combine(normalizedOptions.OutputDirectory!, $"{definition.Id}-logs.txt");

        var androidSdkPath = definition.Target.Platform == ProfilingTargetPlatform.Android
            ? await TryGetAndroidSdkPathAsync()
            : null;

        // Modern dotnet-trace/dotnet-gcdump support --dsrouter natively, so we no longer
        // need a standalone dotnet-dsrouter process. Compute the platform arg once here.
        var dsrouterPlatformArg = GetDsRouterPlatformArg(definition.Target);

        var hasTraceCapture = definition.CaptureKinds.Any(kind => TraceCaptureKinds.Contains(kind));
        var hasMemoryCapture = definition.CaptureKinds.Contains(ProfilingCaptureKind.Memory);
        var hasLogCapture = definition.CaptureKinds.Contains(ProfilingCaptureKind.Logs);
        var preLaunchCaptureSteps = new List<ProfilingCommandStep>();
        var postLaunchCaptureSteps = new List<ProfilingCommandStep>();

        if (hasTraceCapture)
        {
            var (traceStep, traceArtifact) = CreateTraceCaptureStep(
                definition,
                normalizedOptions,
                dsrouterPlatformArg,
                traceArtifactPath,
                runtimeBindings);

            if (dsrouterPlatformArg is not null &&
            normalizedOptions.LaunchMode == ProfilingCaptureLaunchMode.Launch &&
            normalizedOptions.SuspendAtStartup)
            {
                preLaunchCaptureSteps.Add(traceStep);
            }
            else
            {
                postLaunchCaptureSteps.Add(traceStep);
            }

            expectedArtifacts.Add(traceArtifact);
        }

        if (normalizedOptions.LaunchMode == ProfilingCaptureLaunchMode.Launch)
        {
            commands.AddRange(preLaunchCaptureSteps);
            commands.Add(CreateLaunchStep(definition, normalizedOptions, targetFramework, workingDirectory, diagnostics));
        }

        if (dsrouterPlatformArg is null &&
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
            var (memoryStep, memoryArtifact) = CreateMemoryCaptureStep(
                definition,
                normalizedOptions,
                dsrouterPlatformArg,
                gcdumpArtifactPath,
                runtimeBindings);

            postLaunchCaptureSteps.Add(memoryStep);
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
            ? Path.Combine("artifacts", "profiling", definition.Id)
            : normalized.OutputDirectory.Trim();
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

        foreach (var check in prerequisites.Checks.Where(check => check.Status == DependencyStatusType.Warning))
        {
            warnings.Add(check.Message ?? $"{check.Name} requires attention before profiling.");
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
            : Path.Combine(Path.GetTempPath(), $"maui-sherpa-profile-{Guid.NewGuid():N}.sock");
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

        Dictionary<string, string>? environment = null;
        if (definition.Target.Platform == ProfilingTargetPlatform.Android && !string.IsNullOrWhiteSpace(androidSdkPath))
        {
            environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ANDROID_HOME"] = androidSdkPath
            };
        }

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
            RequiresManualStop: true);
    }

    private static ProfilingCommandStep CreateLaunchStep(
        ProfilingSessionDefinition definition,
        ProfilingCapturePlanOptions options,
        string targetFramework,
        string? workingDirectory,
        ProfilingDiagnosticConfiguration? diagnostics)
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

        if (diagnostics is not null)
        {
            arguments.Add("-p:EnableDiagnostics=true");
            arguments.Add($"-p:DiagnosticAddress={diagnostics.Address}");
            arguments.Add($"-p:DiagnosticPort={diagnostics.Port}");
            arguments.Add($"-p:DiagnosticSuspend={diagnostics.SuspendOnStartup.ToString().ToLowerInvariant()}");
            arguments.Add($"-p:DiagnosticListenMode={diagnostics.ListenMode.ToString().ToLowerInvariant()}");
        }

        if (options.AdditionalBuildProperties is not null)
        {
            foreach (var buildProperty in options.AdditionalBuildProperties.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                arguments.Add($"-p:{buildProperty.Key}={buildProperty.Value}");
            }
        }

        return new ProfilingCommandStep(
            Id: "build-and-run",
            Kind: ProfilingCommandStepKind.Launch,
            DisplayName: "Build and run target app",
            Description: $"Build and launch {definition.Target.DisplayName} using {targetFramework}.",
            Command: "dotnet",
            Arguments: arguments,
            WorkingDirectory: workingDirectory,
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
            StopTrigger: ProfilingStopTrigger.OnPipelineStop);
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
        List<ProfilingRuntimeBinding> runtimeBindings)
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

        arguments.Add("--format");
        arguments.Add("speedscope");
        arguments.Add("--output");
        arguments.Add(traceArtifactPath);

        return (
            new ProfilingCommandStep(
                Id: "capture-trace",
                Kind: ProfilingCommandStepKind.Capture,
                DisplayName: "Collect trace",
                Description: $"Collect a speedscope trace for {string.Join(", ", traceKinds)} captures.",
                Command: "dotnet-trace",
                Arguments: arguments,
                WorkingDirectory: options.WorkingDirectory,
                DependsOn: dsrouterPlatformArg is null && options.ProcessId is null ? ["discover-process-id"] : null,
                RequiredRuntimeBindings: dsrouterPlatformArg is null && options.ProcessId is null ? [ProcessIdToken] : null,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tool"] = "dotnet-trace",
                    ["captureKinds"] = string.Join(",", traceKinds),
                    ["output"] = traceArtifactPath
                },
                IsLongRunning: true,
                RequiresManualStop: true,
                CanRunParallel: true,
                StopTrigger: ProfilingStopTrigger.ManualStop),
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
        List<ProfilingRuntimeBinding> runtimeBindings)
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

        return (
            new ProfilingCommandStep(
                Id: "capture-memory",
                Kind: ProfilingCommandStepKind.CollectArtifacts,
                DisplayName: "Collect GC dump",
                Description: "Collect a managed memory dump using dotnet-gcdump.",
                Command: "dotnet-gcdump",
                Arguments: arguments,
                WorkingDirectory: options.WorkingDirectory,
                DependsOn: dsrouterPlatformArg is null && options.ProcessId is null ? ["discover-process-id"] : null,
                RequiredRuntimeBindings: dsrouterPlatformArg is null && options.ProcessId is null ? [ProcessIdToken] : null,
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
