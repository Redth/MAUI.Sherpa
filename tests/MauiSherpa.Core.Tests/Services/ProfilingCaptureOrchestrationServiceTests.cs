using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class ProfilingCaptureOrchestrationServiceTests
{
    private readonly ProfilingCatalogService _catalogService = new([]);
    private readonly Mock<IProfilingPrerequisitesService> _prerequisitesService = new();
    private readonly Mock<IDeviceMonitorService> _deviceMonitorService = new();
    private readonly Mock<IPlatformService> _platformService = new();
    private readonly Mock<IAndroidSdkSettingsService> _androidSdkSettingsService = new();
    private readonly Mock<ILoggingService> _loggingService = new();

    public ProfilingCaptureOrchestrationServiceTests()
    {
        _platformService.SetupGet(x => x.PlatformName).Returns("macOS");
        _platformService.SetupGet(x => x.IsMacOS).Returns(true);
        _platformService.SetupGet(x => x.IsMacCatalyst).Returns(false);
        _platformService.SetupGet(x => x.IsWindows).Returns(false);
        _platformService.SetupGet(x => x.IsLinux).Returns(false);
        _deviceMonitorService.SetupGet(x => x.Current).Returns(ConnectedDevicesSnapshot.Empty);
        _androidSdkSettingsService.Setup(x => x.GetEffectiveSdkPathAsync())
            .ReturnsAsync("/Users/test/Library/Android/sdk");
        _prerequisitesService.Setup(x => x.GetPrerequisitesAsync(
                It.IsAny<ProfilingTargetPlatform>(),
                It.IsAny<IReadOnlyList<ProfilingCaptureKind>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProfilingTargetPlatform platform, IReadOnlyList<ProfilingCaptureKind>? captureKinds, string? _, CancellationToken _) =>
                CreateReadyPrerequisites(platform, captureKinds ?? []));
    }

    [Fact]
    public async Task PlanCaptureAsync_AndroidEmulatorLaunch_UsesDsRouterServerServerAndDiagnosticBuildProperties()
    {
        var snapshot = ConnectedDevicesSnapshot.Empty with
        {
            AndroidEmulators = [new DeviceInfo("emulator-5554", "device", "Pixel 8", true)]
        };
        _deviceMonitorService.SetupGet(x => x.Current).Returns(snapshot);

        var service = CreateService();
        var session = _catalogService.CreateSessionDefinition(
            new ProfilingTarget(
                ProfilingTargetPlatform.Android,
                ProfilingTargetKind.Emulator,
                "emulator-5554",
                "Pixel 8"),
            ProfilingScenarioKind.Launch,
            appId: "com.example.app");

        var plan = await service.PlanCaptureAsync(session, new ProfilingCapturePlanOptions(
            ProjectPath: "/Users/test/src/HelloMaui/HelloMaui.csproj"));

        plan.Validation.IsValid.Should().BeTrue();
        plan.CanExecute.Should().BeTrue();
        plan.Diagnostics.Should().NotBeNull();
        plan.Diagnostics!.DsRouterMode.Should().Be(ProfilingDsRouterMode.ServerServer);
        plan.Diagnostics.Address.Should().Be("10.0.2.2");
        plan.IsTargetCurrentlyAvailable.Should().BeTrue();

        // Both trace and memory requested on mobile → standalone dsrouter is needed
        // because only one tool can use --dsrouter at a time
        plan.Commands.Select(command => command.Id).Should().ContainInOrder(
            "start-dsrouter",
            "capture-trace",
            "build-and-run",
            "capture-memory");
        plan.Commands.Should().Contain(command => command.Id == "start-dsrouter");

        var buildStep = plan.Commands.Single(command => command.Id == "build-and-run");
        buildStep.CommandLine.Should().Contain("-p:DiagnosticAddress=10.0.2.2");
        buildStep.CommandLine.Should().Contain("-p:DiagnosticListenMode=connect");
        buildStep.CommandLine.Should().Contain("-f net10.0-android");
        buildStep.CanRunParallel.Should().BeTrue();
        buildStep.StopTrigger.Should().Be(ProfilingStopTrigger.OnPipelineStop);

        var traceStep = plan.Commands.Single(command => command.Id == "capture-trace");
        traceStep.CommandLine.Should().Contain("--diagnostic-port");
        traceStep.CommandLine.Should().NotContain("--dsrouter");
        traceStep.CanRunParallel.Should().BeTrue();
        traceStep.StopTrigger.Should().Be(ProfilingStopTrigger.ManualStop);

        var memoryStep = plan.Commands.Single(command => command.Id == "capture-memory");
        memoryStep.CommandLine.Should().Contain("--diagnostic-port");
        memoryStep.CommandLine.Should().NotContain("--dsrouter");
    }

    [Fact]
    public async Task PlanCaptureAsync_IosPhysicalDeviceLaunch_UsesDsRouterServerClientAndListenMode()
    {
        var snapshot = ConnectedDevicesSnapshot.Empty with
        {
            ApplePhysicalDevices =
            [
                new AppleDeviceInfo("00008110-000E64C20A91801E", "Test iPhone", "iPhone 15", "iOS", "arm64", "17.5", false, true, "usb", null)
            ]
        };
        _deviceMonitorService.SetupGet(x => x.Current).Returns(snapshot);

        var service = CreateService();
        var session = _catalogService.CreateSessionDefinition(
            new ProfilingTarget(
                ProfilingTargetPlatform.iOS,
                ProfilingTargetKind.PhysicalDevice,
                "00008110-000E64C20A91801E",
                "Test iPhone"),
            ProfilingScenarioKind.Launch,
            appId: "com.example.iosapp");

        var plan = await service.PlanCaptureAsync(session, new ProfilingCapturePlanOptions(
            ProjectPath: "/Users/test/src/HelloMaui/HelloMaui.csproj"));

        plan.Validation.IsValid.Should().BeTrue();
        plan.CanExecute.Should().BeTrue();
        plan.Diagnostics.Should().NotBeNull();
        plan.Diagnostics!.DsRouterMode.Should().Be(ProfilingDsRouterMode.ServerClient);
        plan.Diagnostics.ListenMode.Should().Be(ProfilingDiagnosticListenMode.Listen);

        // Both trace and memory requested on mobile → standalone dsrouter is needed
        plan.Commands.Select(command => command.Id).Should().ContainInOrder(
            "start-dsrouter",
            "capture-trace",
            "build-and-run",
            "capture-memory");
        plan.Commands.Should().Contain(command => command.Id == "start-dsrouter");

        var traceStep = plan.Commands.Single(command => command.Id == "capture-trace");
        traceStep.CommandLine.Should().Contain("--diagnostic-port");
        traceStep.CommandLine.Should().NotContain("--dsrouter");

        var memoryStep = plan.Commands.Single(command => command.Id == "capture-memory");
        memoryStep.CommandLine.Should().Contain("--diagnostic-port");
        memoryStep.CommandLine.Should().NotContain("--dsrouter");

        var buildStep = plan.Commands.Single(command => command.Id == "build-and-run");
        buildStep.CommandLine.Should().Contain("-f net10.0-ios");
        buildStep.CommandLine.Should().Contain("-p:DiagnosticListenMode=listen");
    }

    [Fact]
    public async Task PlanCaptureAsync_AndroidEmulatorTraceOnly_UsesInlineDsRouter()
    {
        var snapshot = ConnectedDevicesSnapshot.Empty with
        {
            AndroidEmulators = [new DeviceInfo("emulator-5554", "device", "Pixel 8", true)]
        };
        _deviceMonitorService.SetupGet(x => x.Current).Returns(snapshot);

        var service = CreateService();
        // Only CPU trace, no memory — should use inline --dsrouter
        var session = _catalogService.CreateSessionDefinition(
            new ProfilingTarget(
                ProfilingTargetPlatform.Android,
                ProfilingTargetKind.Emulator,
                "emulator-5554",
                "Pixel 8"),
            ProfilingScenarioKind.Launch,
            captureKinds: [ProfilingCaptureKind.Cpu],
            appId: "com.example.app");

        var plan = await service.PlanCaptureAsync(session, new ProfilingCapturePlanOptions(
            ProjectPath: "/Users/test/src/HelloMaui/HelloMaui.csproj"));

        plan.Validation.IsValid.Should().BeTrue();
        plan.Commands.Should().NotContain(command => command.Id == "start-dsrouter");

        var traceStep = plan.Commands.Single(command => command.Id == "capture-trace");
        traceStep.CommandLine.Should().Contain("--dsrouter android-emu");
        traceStep.CommandLine.Should().NotContain("--diagnostic-port");
    }

    [Fact]
    public async Task PlanCaptureAsync_MacCatalystLaunch_AddsRuntimeBindingForProcessAttach()
    {
        var service = CreateService();
        var session = new ProfilingSessionDefinition(
            "session-1",
            "Mac Catalyst Trace",
            new ProfilingTarget(
                ProfilingTargetPlatform.MacCatalyst,
                ProfilingTargetKind.Desktop,
                "local-desktop",
                "MAUI Sherpa"),
            ProfilingScenarioKind.Interaction,
            [ProfilingCaptureKind.Cpu],
            AppId: "codes.redth.mauisherpa",
            Duration: TimeSpan.FromMinutes(5),
            CreatedAt: DateTimeOffset.UtcNow);

        var plan = await service.PlanCaptureAsync(session, new ProfilingCapturePlanOptions(
            ProjectPath: "/Users/test/src/MauiSherpa/MauiSherpa.csproj"));

        plan.Validation.IsValid.Should().BeTrue();
        plan.Diagnostics.Should().BeNull();
        plan.RequiresRuntimeInputs.Should().BeTrue();
        plan.CanExecute.Should().BeFalse();
        plan.RuntimeBindings.Should().ContainSingle(binding => binding.Token == "{{PROCESS_ID}}");
        plan.Commands.Select(command => command.Id).Should().ContainInOrder(
            "build-and-run",
            "discover-process-id",
            "capture-trace");
    }

    [Fact]
    public async Task PlanCaptureAsync_DefaultOutputDirectory_UsesProjectNameAndDate()
    {
        var snapshot = ConnectedDevicesSnapshot.Empty with
        {
            AndroidEmulators = [new DeviceInfo("emulator-5554", "device", "Pixel 8", true)]
        };
        _deviceMonitorService.SetupGet(x => x.Current).Returns(snapshot);

        var service = CreateService();
        var createdAt = new DateTimeOffset(2026, 3, 9, 14, 0, 0, TimeSpan.Zero);
        var session = _catalogService.CreateSessionDefinition(
            new ProfilingTarget(
                ProfilingTargetPlatform.Android,
                ProfilingTargetKind.Emulator,
                "emulator-5554",
                "Pixel 8"),
            ProfilingScenarioKind.Launch,
            appId: "com.example.app");
        session = session with { CreatedAt = createdAt };

        var plan = await service.PlanCaptureAsync(session, new ProfilingCapturePlanOptions(
            ProjectPath: "/Users/test/src/HelloMaui/HelloMaui.csproj"));

        plan.Validation.IsValid.Should().BeTrue();
        var dateStr = createdAt.LocalDateTime.ToString("yyyy-MM-dd");
        var expectedDir = Path.Combine("artifacts", "profiling", "HelloMaui", $"{dateStr}-1");
        plan.Options.OutputDirectory.Should().Be(expectedDir);
    }

    [Fact]
    public async Task PlanCaptureAsync_ArtifactFileNames_UseSimpleNames()
    {
        var snapshot = ConnectedDevicesSnapshot.Empty with
        {
            AndroidEmulators = [new DeviceInfo("emulator-5554", "device", "Pixel 8", true)]
        };
        _deviceMonitorService.SetupGet(x => x.Current).Returns(snapshot);

        var service = CreateService();
        var session = _catalogService.CreateSessionDefinition(
            new ProfilingTarget(
                ProfilingTargetPlatform.Android,
                ProfilingTargetKind.Emulator,
                "emulator-5554",
                "Pixel 8"),
            ProfilingScenarioKind.Launch,
            appId: "com.example.app");

        var plan = await service.PlanCaptureAsync(session, new ProfilingCapturePlanOptions(
            ProjectPath: "/Users/test/src/HelloMaui/HelloMaui.csproj"));

        plan.Validation.IsValid.Should().BeTrue();
        plan.ExpectedArtifacts.Should().Contain(a => a.FileName == "trace.nettrace");
        plan.ExpectedArtifacts.Should().Contain(a => a.FileName == "memory.gcdump");
    }

    [Fact]
    public async Task PlanCaptureAsync_NoProjectPath_FallsBackToSessionInOutputDir()
    {
        var service = CreateService();
        var session = _catalogService.CreateSessionDefinition(
            new ProfilingTarget(
                ProfilingTargetPlatform.MacCatalyst,
                ProfilingTargetKind.Desktop,
                "local-desktop",
                "MAUI Sherpa"),
            ProfilingScenarioKind.Interaction,
            appId: "codes.redth.mauisherpa");

        var plan = await service.PlanCaptureAsync(session, new ProfilingCapturePlanOptions());

        // Missing project path in launch mode causes validation error, but for attach scenarios
        // the output dir still uses "session" fallback when no project path is given
        plan.Options.OutputDirectory.Should().Contain(Path.Combine("artifacts", "profiling", "session"));
    }

    [Fact]
    public async Task PlanCaptureAsync_LaunchWithoutProjectPath_ReturnsValidationError()
    {
        var service = CreateService();
        var session = _catalogService.CreateSessionDefinition(
            new ProfilingTarget(
                ProfilingTargetPlatform.Android,
                ProfilingTargetKind.PhysicalDevice,
                "device-01",
                "Pixel 9"),
            ProfilingScenarioKind.Launch);

        var plan = await service.PlanCaptureAsync(session, new ProfilingCapturePlanOptions());

        plan.Validation.IsValid.Should().BeFalse();
        plan.Validation.Errors.Should().Contain(error =>
            error.Contains("project path", StringComparison.OrdinalIgnoreCase));
    }

    private ProfilingCaptureOrchestrationService CreateService() =>
        new(
            _catalogService,
            _prerequisitesService.Object,
            _deviceMonitorService.Object,
            _platformService.Object,
            _androidSdkSettingsService.Object,
            _loggingService.Object);

    private static ProfilingPrerequisiteReport CreateReadyPrerequisites(
        ProfilingTargetPlatform platform,
        IReadOnlyList<ProfilingCaptureKind> captureKinds)
    {
        return new ProfilingPrerequisiteReport(
            new ProfilingPrerequisiteContext(
                platform,
                captureKinds,
                "/Users/test/code/MAUI.Sherpa",
                "/usr/local/share/dotnet/dotnet",
                new DoctorContext(
                    "/Users/test/code/MAUI.Sherpa",
                    "/usr/local/share/dotnet",
                    "/Users/test/code/MAUI.Sherpa/global.json",
                    "10.0.100",
                    null,
                    "10.0.100",
                    ActiveSdkVersion: "10.0.103",
                    ResolvedSdkVersion: "10.0.103")),
            [new ProfilingPrerequisiteStatus(
                "Host Platform",
                ProfilingPrerequisiteKind.HostPlatform,
                DependencyStatusType.Ok,
                IsRequired: true,
                RequiredVersion: null,
                RecommendedVersion: null,
                InstalledVersion: "macOS",
                Message: "Host ready")],
            DateTimeOffset.UtcNow);
    }
}
