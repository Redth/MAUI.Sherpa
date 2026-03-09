using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class ProfilingPrerequisitesServiceTests
{
    private readonly Mock<IDoctorService> _doctorService = new();
    private readonly Mock<IPlatformService> _platformService = new();
    private readonly Mock<ILoggingService> _loggingService = new();

    public ProfilingPrerequisitesServiceTests()
    {
        _platformService.SetupGet(x => x.PlatformName).Returns("macOS");
        _platformService.SetupGet(x => x.IsMacOS).Returns(true);
        _platformService.SetupGet(x => x.IsMacCatalyst).Returns(false);
        _platformService.SetupGet(x => x.IsWindows).Returns(false);
        _platformService.SetupGet(x => x.IsLinux).Returns(false);
    }

    [Fact]
    public async Task GetPrerequisitesAsync_ForAndroidMemoryCapture_ReturnsReadyWhenRequiredToolsExist()
    {
        var context = CreateDoctorContext(activeSdkVersion: "10.0.103");
        SetupDoctor(context, CreateDoctorReport(
            context,
            new DependencyStatus(".NET SDK", DependencyCategory.DotNetSdk, null, "10.0.103", "10.0.103", DependencyStatusType.Ok, "SDK ready", false),
            new DependencyStatus("Android SDK", DependencyCategory.AndroidSdk, null, null, "/Users/test/Library/Android/sdk", DependencyStatusType.Ok, "Found SDK", false),
            new DependencyStatus("Platform Tools", DependencyCategory.AndroidSdk, null, null, "Installed", DependencyStatusType.Ok, "adb available", false)));

        var service = CreateService((request, _) => Task.FromResult(CreateToolListResult(request, """
            Package Id      Version         Commands
            ----------------------------------------------------------
            dotnet-trace    10.0.41001      dotnet-trace
            dotnet-gcdump   10.0.41001      dotnet-gcdump
            dotnet-dsrouter 10.0.41001      dotnet-dsrouter
            """)));

        var report = await service.GetPrerequisitesAsync(
            ProfilingTargetPlatform.Android,
            [ProfilingCaptureKind.Cpu, ProfilingCaptureKind.Memory]);

        report.IsReady.Should().BeTrue();
        report.Checks.Should().ContainSingle(x => x.Name == "dotnet-trace" && x.Status == DependencyStatusType.Ok);
        report.Checks.Should().ContainSingle(x => x.Name == "dotnet-gcdump" && x.IsRequired && x.Status == DependencyStatusType.Ok);
        report.Checks.Should().ContainSingle(x => x.Name == "dotnet-dsrouter" && x.IsRequired && x.Status == DependencyStatusType.Ok);
        report.Checks.Should().ContainSingle(x => x.Name == "Platform Tools" && x.Status == DependencyStatusType.Ok);
    }

    [Fact]
    public async Task GetPrerequisitesAsync_UpgradesRequiredAndroidPlatformToolsWarningToError()
    {
        var context = CreateDoctorContext(activeSdkVersion: "10.0.103");
        SetupDoctor(context, CreateDoctorReport(
            context,
            new DependencyStatus(".NET SDK", DependencyCategory.DotNetSdk, null, "10.0.103", "10.0.103", DependencyStatusType.Ok, "SDK ready", false),
            new DependencyStatus("Android SDK", DependencyCategory.AndroidSdk, null, null, "/Users/test/Library/Android/sdk", DependencyStatusType.Ok, "Found SDK", false),
            new DependencyStatus("Platform Tools", DependencyCategory.AndroidSdk, null, null, null, DependencyStatusType.Warning, "Platform tools not installed", true, "install-android-package:platform-tools")));

        var service = CreateService((request, _) => Task.FromResult(CreateToolListResult(request, """
            Package Id      Version         Commands
            ----------------------------------------------------------
            dotnet-trace    10.0.41001      dotnet-trace
            dotnet-dsrouter 10.0.41001      dotnet-dsrouter
            """)));

        var report = await service.GetPrerequisitesAsync(
            ProfilingTargetPlatform.Android,
            [ProfilingCaptureKind.Cpu]);

        report.IsReady.Should().BeFalse();
        report.Checks.Should().ContainSingle(x =>
            x.Name == "Platform Tools" &&
            x.Status == DependencyStatusType.Error &&
            x.Message!.Contains("required for profiling readiness", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetPrerequisitesAsync_RequiresGcDumpForMemoryCapture()
    {
        var context = CreateDoctorContext(activeSdkVersion: "10.0.103");
        SetupDoctor(context, CreateDoctorReport(
            context,
            new DependencyStatus(".NET SDK", DependencyCategory.DotNetSdk, null, "10.0.103", "10.0.103", DependencyStatusType.Ok, "SDK ready", false)));

        var service = CreateService((request, _) => Task.FromResult(CreateToolListResult(request, """
            Package Id     Version        Commands
            --------------------------------------------------------
            dotnet-trace   10.0.41001     dotnet-trace
            """)));

        var report = await service.GetPrerequisitesAsync(
            ProfilingTargetPlatform.MacOS,
            [ProfilingCaptureKind.Memory]);

        report.IsReady.Should().BeFalse();
        report.Checks.Should().ContainSingle(x =>
            x.Name == "dotnet-gcdump" &&
            x.Status == DependencyStatusType.Error &&
            x.IsRequired);
    }

    [Fact]
    public async Task GetPrerequisitesAsync_DoesNotWarnWhenToolMajorDoesNotMatchActiveSdk()
    {
        var context = CreateDoctorContext(activeSdkVersion: "10.0.103");
        SetupDoctor(context, CreateDoctorReport(
            context,
            new DependencyStatus(".NET SDK", DependencyCategory.DotNetSdk, null, "10.0.103", "10.0.103", DependencyStatusType.Ok, "SDK ready", false)));

        var service = CreateService((request, _) => Task.FromResult(CreateToolListResult(request, """
            Package Id     Version        Commands
            --------------------------------------------------------
            dotnet-trace   9.0.553801     dotnet-trace
            """)));

        var report = await service.GetPrerequisitesAsync(
            ProfilingTargetPlatform.MacOS,
            [ProfilingCaptureKind.Cpu]);

        report.IsReady.Should().BeTrue();
        report.Checks.Should().ContainSingle(x =>
            x.Name == "dotnet-trace" &&
            x.Status == DependencyStatusType.Ok &&
            x.RecommendedVersion == null &&
            x.RequiredVersion == null &&
            x.SuggestedCommand == null);
    }

    private ProfilingPrerequisitesService CreateService(
        Func<ProcessRequest, CancellationToken, Task<ProcessResult>> processExecutor)
    {
        return new ProfilingPrerequisitesService(
            _doctorService.Object,
            _platformService.Object,
            _loggingService.Object,
            processExecutor,
            NullLoggerFactory.Instance);
    }

    private void SetupDoctor(DoctorContext context, DoctorReport report)
    {
        _doctorService.Setup(x => x.GetContextAsync(It.IsAny<string?>()))
            .ReturnsAsync(context);
        _doctorService.Setup(x => x.RunDoctorAsync(It.IsAny<DoctorContext?>(), It.IsAny<IProgress<string>?>()))
            .ReturnsAsync(report);
        _doctorService.Setup(x => x.GetDotNetExecutablePath())
            .Returns("/usr/local/share/dotnet/dotnet");
    }

    private static ProcessResult CreateToolListResult(ProcessRequest request, string output)
    {
        if (request.Arguments.Length >= 2 &&
            request.Arguments[0] == "tool" &&
            request.Arguments[1] == "list")
        {
            return new ProcessResult(0, output, string.Empty, TimeSpan.Zero, ProcessState.Completed);
        }

        return new ProcessResult(1, string.Empty, $"Unexpected command: {request.CommandLine}", TimeSpan.Zero, ProcessState.Failed);
    }

    private static DoctorContext CreateDoctorContext(string activeSdkVersion) => new(
        WorkingDirectory: "/Users/test/code/MAUI.Sherpa",
        DotNetSdkPath: "/usr/local/share/dotnet",
        GlobalJsonPath: "/Users/test/code/MAUI.Sherpa/global.json",
        PinnedSdkVersion: "10.0.100",
        PinnedWorkloadSetVersion: null,
        EffectiveFeatureBand: "10.0.100",
        IsPreviewSdk: false,
        ActiveSdkVersion: activeSdkVersion,
        RollForwardPolicy: "latestPatch",
        ResolvedSdkVersion: activeSdkVersion);

    private static DoctorReport CreateDoctorReport(DoctorContext context, params DependencyStatus[] dependencies) => new(
        context,
        [new SdkVersionInfo(context.ActiveSdkVersion ?? "10.0.103", "10.0.100", 10, 0, false)],
        AvailableSdkVersions: null,
        InstalledWorkloadSetVersion: null,
        AvailableWorkloadSetVersions: null,
        Manifests: [],
        Dependencies: dependencies,
        DateTime.UtcNow);
}
