using System.Diagnostics;
using System.Text.RegularExpressions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Workloads.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MauiSherpa.Core.Services;

public class ProfilingPrerequisitesService : IProfilingPrerequisitesService
{
    private static readonly StringComparer ToolComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly Regex ToolListLineRegex = new(
        @"^(?<package>\S+)\s+(?<version>\S+)\s+(?<commands>.+?)\s*$",
        RegexOptions.Compiled);
    private static readonly Regex VersionRegex = new(@"(?<version>\d+(?:\.\d+)+(?:[-+][^\s]+)?)", RegexOptions.Compiled);

    private readonly IDoctorService _doctorService;
    private readonly IPlatformService _platformService;
    private readonly ILoggingService _loggingService;
    private readonly ILogger<ProfilingPrerequisitesService> _logger;
    private readonly Func<ProcessRequest, CancellationToken, Task<ProcessResult>> _executeProcessAsync;

    public ProfilingPrerequisitesService(
        IDoctorService doctorService,
        IPlatformService platformService,
        ILoggingService loggingService,
        ILoggerFactory? loggerFactory = null)
        : this(doctorService, platformService, loggingService, ExecuteProcessAsync, loggerFactory)
    {
    }

    internal ProfilingPrerequisitesService(
        IDoctorService doctorService,
        IPlatformService platformService,
        ILoggingService loggingService,
        Func<ProcessRequest, CancellationToken, Task<ProcessResult>> executeProcessAsync,
        ILoggerFactory? loggerFactory = null)
    {
        _doctorService = doctorService;
        _platformService = platformService;
        _loggingService = loggingService;
        _executeProcessAsync = executeProcessAsync;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ProfilingPrerequisitesService>();
    }

    public async Task<ProfilingPrerequisiteReport> GetPrerequisitesAsync(
        ProfilingTargetPlatform platform,
        IReadOnlyList<ProfilingCaptureKind>? captureKinds = null,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var normalizedCaptureKinds = captureKinds?
            .Distinct()
            .OrderBy(kind => kind)
            .ToArray() ?? Array.Empty<ProfilingCaptureKind>();

        var doctorContext = await _doctorService.GetContextAsync(workingDirectory);
        var doctorReport = await _doctorService.RunDoctorAsync(doctorContext);
        var dotNetExecutablePath = _doctorService.GetDotNetExecutablePath();
        var effectiveWorkingDirectory = workingDirectory ?? doctorContext.WorkingDirectory;

        var statuses = new List<ProfilingPrerequisiteStatus>();
        AddHostPlatformStatus(platform, statuses);
        AddDoctorDependencyStatus(
            doctorReport,
            statuses,
            ".NET SDK",
            ProfilingPrerequisiteKind.DotNetSdk,
            isRequired: true,
            requiredVersion: doctorContext.ResolvedSdkVersion ?? doctorContext.PinnedSdkVersion,
            recommendedVersion: doctorContext.ActiveSdkVersion);
        AddPlatformDependencyStatuses(platform, doctorReport, statuses);

        var discoveredTools = await DiscoverDotNetToolsAsync(dotNetExecutablePath, effectiveWorkingDirectory, ct);
        AddDotNetToolStatus(
            statuses,
            discoveredTools,
            "dotnet-trace",
            "dotnet-trace",
            isRequired: RequiresTraceTool(normalizedCaptureKinds),
            missingMessage: "Install dotnet-trace to capture EventPipe traces for profiling workflows.");

        AddDotNetToolStatus(
            statuses,
            discoveredTools,
            "dotnet-gcdump",
            "dotnet-gcdump",
            isRequired: RequiresGcDumpTool(normalizedCaptureKinds),
            missingMessage: "Install dotnet-gcdump to collect memory dumps for profiling sessions.");

        AddDotNetToolStatus(
            statuses,
            discoveredTools,
            "dotnet-dsrouter",
            "dotnet-dsrouter",
            isRequired: RequiresDiagnosticsRouter(platform, normalizedCaptureKinds),
            missingMessage: "Install dotnet-dsrouter to bridge diagnostics traffic for mobile profiling.");

        return new ProfilingPrerequisiteReport(
            new ProfilingPrerequisiteContext(
                platform,
                normalizedCaptureKinds,
                effectiveWorkingDirectory,
                dotNetExecutablePath,
                doctorContext),
            statuses,
            DateTimeOffset.UtcNow);
    }

    private void AddHostPlatformStatus(
        ProfilingTargetPlatform platform,
        List<ProfilingPrerequisiteStatus> statuses)
    {
        var hostPlatform = _platformService.PlatformName;
        var (supported, message) = platform switch
        {
            ProfilingTargetPlatform.iOS or ProfilingTargetPlatform.MacCatalyst or ProfilingTargetPlatform.MacOS
                when !_platformService.IsMacOS && !_platformService.IsMacCatalyst
                => (false, $"{platform} profiling requires a macOS host."),
            ProfilingTargetPlatform.Windows when !_platformService.IsWindows
                => (false, "Windows profiling requires a Windows host."),
            _ => (true, $"Host platform '{hostPlatform}' supports {platform} profiling prerequisites.")
        };

        statuses.Add(new ProfilingPrerequisiteStatus(
            "Host Platform",
            ProfilingPrerequisiteKind.HostPlatform,
            supported ? DependencyStatusType.Ok : DependencyStatusType.Error,
            IsRequired: true,
            RequiredVersion: null,
            RecommendedVersion: null,
            InstalledVersion: hostPlatform,
            Message: message));
    }

    private void AddPlatformDependencyStatuses(
        ProfilingTargetPlatform platform,
        DoctorReport doctorReport,
        List<ProfilingPrerequisiteStatus> statuses)
    {
        switch (platform)
        {
            case ProfilingTargetPlatform.Android:
                AddDoctorDependencyStatus(
                    doctorReport,
                    statuses,
                    "Android SDK",
                    ProfilingPrerequisiteKind.AndroidToolchain,
                    isRequired: true);
                AddDoctorDependencyStatus(
                    doctorReport,
                    statuses,
                    "Platform Tools",
                    ProfilingPrerequisiteKind.AndroidToolchain,
                    isRequired: true,
                    upgradeWarningToError: true,
                    missingMessage: "Android platform-tools (adb) are required to profile Android targets.");
                break;

            case ProfilingTargetPlatform.iOS:
                AddDoctorDependencyStatus(
                    doctorReport,
                    statuses,
                    "Xcode",
                    ProfilingPrerequisiteKind.AppleToolchain,
                    isRequired: true);
                AddDoctorDependencyStatus(
                    doctorReport,
                    statuses,
                    "iOS Simulators",
                    ProfilingPrerequisiteKind.AppleToolchain,
                    isRequired: false);
                break;

            case ProfilingTargetPlatform.MacCatalyst:
                AddDoctorDependencyStatus(
                    doctorReport,
                    statuses,
                    "Xcode",
                    ProfilingPrerequisiteKind.AppleToolchain,
                    isRequired: true);
                break;

            case ProfilingTargetPlatform.Windows:
                statuses.Add(new ProfilingPrerequisiteStatus(
                    "Windows Toolchain",
                    ProfilingPrerequisiteKind.WindowsToolchain,
                    DependencyStatusType.Info,
                    IsRequired: false,
                    RequiredVersion: null,
                    RecommendedVersion: null,
                    InstalledVersion: _platformService.IsWindows ? _platformService.PlatformName : null,
                    Message: "Windows-specific toolchain validation is not implemented yet for profiling prerequisites."));
                break;
        }
    }

    private static bool RequiresTraceTool(IReadOnlyList<ProfilingCaptureKind> captureKinds) =>
        captureKinds.Count == 0 ||
        captureKinds.Any(kind => kind is ProfilingCaptureKind.Startup
            or ProfilingCaptureKind.Cpu
            or ProfilingCaptureKind.Memory
            or ProfilingCaptureKind.Rendering
            or ProfilingCaptureKind.Energy
            or ProfilingCaptureKind.SystemTrace);

    private static bool RequiresGcDumpTool(IReadOnlyList<ProfilingCaptureKind> captureKinds) =>
        captureKinds.Contains(ProfilingCaptureKind.Memory);

    private static bool RequiresDiagnosticsRouter(
        ProfilingTargetPlatform platform,
        IReadOnlyList<ProfilingCaptureKind> captureKinds) =>
        platform is ProfilingTargetPlatform.Android or ProfilingTargetPlatform.iOS &&
        RequiresTraceTool(captureKinds);

    private void AddDoctorDependencyStatus(
        DoctorReport doctorReport,
        List<ProfilingPrerequisiteStatus> statuses,
        string dependencyName,
        ProfilingPrerequisiteKind kind,
        bool isRequired,
        string? requiredVersion = null,
        string? recommendedVersion = null,
        bool upgradeWarningToError = false,
        string? missingMessage = null)
    {
        var dependency = doctorReport.Dependencies
            .FirstOrDefault(item => item.Name.Equals(dependencyName, StringComparison.OrdinalIgnoreCase));

        if (dependency is null)
        {
            statuses.Add(new ProfilingPrerequisiteStatus(
                dependencyName,
                kind,
                isRequired ? DependencyStatusType.Error : DependencyStatusType.Warning,
                isRequired,
                requiredVersion,
                recommendedVersion,
                InstalledVersion: null,
                Message: missingMessage ?? $"{dependencyName} could not be validated from the doctor report."));
            return;
        }

        var status = dependency.Status;
        if (upgradeWarningToError && status == DependencyStatusType.Warning)
            status = DependencyStatusType.Error;

        var message = dependency.Message;
        if (upgradeWarningToError && dependency.Status == DependencyStatusType.Warning && !string.IsNullOrWhiteSpace(message))
            message = $"{message} This is required for profiling readiness.";

        statuses.Add(new ProfilingPrerequisiteStatus(
            dependency.Name,
            kind,
            status,
            isRequired,
            requiredVersion ?? dependency.RequiredVersion,
            recommendedVersion ?? dependency.RecommendedVersion,
            dependency.InstalledVersion,
            message,
            IsFixable: dependency.IsFixable,
            FixAction: dependency.FixAction));
    }

    private void AddDotNetToolStatus(
        List<ProfilingPrerequisiteStatus> statuses,
        IReadOnlyDictionary<string, DiscoveredDotNetTool> discoveredTools,
        string commandName,
        string packageId,
        bool isRequired,
        string missingMessage)
    {
        if (!isRequired && !discoveredTools.ContainsKey(commandName))
            return;

        if (!discoveredTools.TryGetValue(commandName, out var tool))
        {
            statuses.Add(new ProfilingPrerequisiteStatus(
                commandName,
                ProfilingPrerequisiteKind.DotNetTool,
                isRequired ? DependencyStatusType.Error : DependencyStatusType.Warning,
                isRequired,
                RequiredVersion: null,
                RecommendedVersion: null,
                InstalledVersion: null,
                Message: missingMessage,
                DiscoveredBy: null,
                ExecutablePath: null,
                IsFixable: true,
                FixAction: $"install-dotnet-tool:{packageId}",
                SuggestedCommand: $"dotnet tool install --global {packageId}"));
            return;
        }

        var message = $"{commandName} {tool.Version} discovered via {tool.Source}.";

        statuses.Add(new ProfilingPrerequisiteStatus(
            commandName,
            ProfilingPrerequisiteKind.DotNetTool,
            DependencyStatusType.Ok,
            isRequired,
            RequiredVersion: null,
            RecommendedVersion: null,
            InstalledVersion: tool.Version,
            Message: message,
            DiscoveredBy: tool.Source,
            ExecutablePath: tool.ExecutablePath,
            IsFixable: false,
            FixAction: null,
            SuggestedCommand: null));
    }

    private async Task<IReadOnlyDictionary<string, DiscoveredDotNetTool>> DiscoverDotNetToolsAsync(
        string dotNetExecutablePath,
        string? workingDirectory,
        CancellationToken ct)
    {
        var discoveredTools = new Dictionary<string, DiscoveredDotNetTool>(ToolComparer);

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            foreach (var tool in await TryListDotNetToolsAsync(dotNetExecutablePath, ["tool", "list", "--local"], "local-manifest", workingDirectory, ct))
                discoveredTools[tool.Command] = tool;
        }

        foreach (var tool in await TryListDotNetToolsAsync(dotNetExecutablePath, ["tool", "list", "--global"], "global-tool", null, ct))
            discoveredTools[tool.Command] = tool;

        foreach (var command in new[] { "dotnet-trace", "dotnet-gcdump", "dotnet-dsrouter" })
        {
            if (discoveredTools.ContainsKey(command))
                continue;

            var shimPath = ResolveGlobalToolShim(command);
            if (shimPath is null)
                continue;

            var result = await _executeProcessAsync(
                new ProcessRequest(shimPath, ["--version"], workingDirectory),
                ct);

            if (!result.Success)
                continue;

            var version = TryExtractVersion(result.Output) ?? TryExtractVersion(result.Error);
            if (version is null)
                continue;

            discoveredTools[command] = new DiscoveredDotNetTool(command, version, "global-shim", shimPath);
        }

        return discoveredTools;
    }

    private async Task<IReadOnlyList<DiscoveredDotNetTool>> TryListDotNetToolsAsync(
        string dotNetExecutablePath,
        string[] arguments,
        string source,
        string? workingDirectory,
        CancellationToken ct)
    {
        try
        {
            var result = await _executeProcessAsync(
                new ProcessRequest(dotNetExecutablePath, arguments, workingDirectory),
                ct);

            if (!result.Success)
            {
                _logger.LogDebug("Failed to list {Source} dotnet tools: {Error}", source, result.Error);
                return Array.Empty<DiscoveredDotNetTool>();
            }

            return ParseDotNetToolList(result.Output, source);
        }
        catch (Exception ex)
        {
            _loggingService.LogDebug($"Failed to discover {source} dotnet tools: {ex.Message}");
            return Array.Empty<DiscoveredDotNetTool>();
        }
    }

    internal static IReadOnlyList<DiscoveredDotNetTool> ParseDotNetToolList(string output, string source)
    {
        var results = new List<DiscoveredDotNetTool>();
        var lines = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

        var separatorIndex = Array.FindIndex(lines, line => line.StartsWith("---", StringComparison.Ordinal));
        if (separatorIndex < 0)
            return results;

        foreach (var line in lines[(separatorIndex + 1)..])
        {
            var match = ToolListLineRegex.Match(line);
            if (!match.Success)
                continue;

            var version = match.Groups["version"].Value;
            var commands = match.Groups["commands"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var command in commands)
                results.Add(new DiscoveredDotNetTool(command, version, source, ResolveGlobalToolShim(command)));
        }

        return results;
    }

    private static int? GetSdkMajorVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        if (SdkVersion.TryParse(version, out var sdkVersion) && sdkVersion is not null)
            return sdkVersion.Major;

        var match = Regex.Match(version, @"^(?<major>\d+)");
        return match.Success && int.TryParse(match.Groups["major"].Value, out var major) ? major : null;
    }

    private static string? TryExtractVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var match = VersionRegex.Match(output);
        return match.Success ? match.Groups["version"].Value : null;
    }

    private static string? ResolveGlobalToolShim(string command)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
            return null;

        var executableName = OperatingSystem.IsWindows() ? $"{command}.exe" : command;
        var candidate = Path.Combine(userProfile, ".dotnet", "tools", executableName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static async Task<ProcessResult> ExecuteProcessAsync(ProcessRequest request, CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = request.Command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory
            };

            foreach (var arg in request.Arguments)
                startInfo.ArgumentList.Add(arg);

            if (request.Environment is not null)
            {
                foreach (var (key, value) in request.Environment)
                    startInfo.Environment[key] = value;
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            var output = await outputTask;
            var error = await errorTask;
            var finalState = process.ExitCode == 0 ? ProcessState.Completed : ProcessState.Failed;

            return new ProcessResult(process.ExitCode, output, error, TimeSpan.Zero, finalState);
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, string.Empty, ex.Message, TimeSpan.Zero, ProcessState.Failed);
        }
    }

    internal sealed record DiscoveredDotNetTool(
        string Command,
        string Version,
        string Source,
        string? ExecutablePath);
}
