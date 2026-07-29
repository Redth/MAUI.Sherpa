using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;
using MauiSherpa.Workloads.NuGet;
using NuGet.Versioning;

namespace MauiSherpa.Core.Services;

public sealed class MauiCliToolService : IMauiCliToolService
{
    private const string PackageId = "Microsoft.Maui.Cli";
    private static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromSeconds(20);

    private readonly IProcessExecutionService _process;
    private readonly ILoggingService _logger;
    private readonly Func<string?> _resolveExecutable;
    private readonly Func<INuGetClient> _nugetClientFactory;
    private INuGetClient? _nugetClient;

    public MauiCliToolService(
        IProcessExecutionService process,
        ILoggingService logger)
        : this(process, logger, () => MauiCliExecutableResolver.Resolve())
    {
    }

    public MauiCliToolService(
        IProcessExecutionService process,
        ILoggingService logger,
        Func<string?> resolveExecutable,
        Func<INuGetClient>? nugetClientFactory = null)
    {
        _process = process;
        _logger = logger;
        _resolveExecutable = resolveExecutable;
        _nugetClientFactory = nugetClientFactory ?? (() => new NuGetClient());
    }

    public async Task<MauiCliToolStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var executablePath = _resolveExecutable();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new MauiCliToolStatus(
                MauiCliToolState.Missing,
                Message: "The Microsoft MAUI CLI global tool is not installed.");
        }

        var versionResult = await ExecuteAsync(
            executablePath,
            ["version", "--json", "--ci"],
            "Checking MAUI CLI",
            ct);

        var version = NormalizeVersion(ParseMessages(versionResult.Output)
            .OfType<MauiCliVersionMessage>()
            .LastOrDefault()
            ?.Version);

        if (!versionResult.Success)
        {
            return new MauiCliToolStatus(
                MauiCliToolState.UpdateRequired,
                executablePath,
                version,
                "The installed MAUI CLI could not report its version.");
        }

        var startupHelp = await ExecuteAsync(
            executablePath,
            ["profile", "startup", "--help"],
            "Checking startup profiling",
            ct);
        var manualHelp = await ExecuteAsync(
            executablePath,
            ["profile", "manual", "--help"],
            "Checking interaction profiling",
            ct);

        if (!startupHelp.Success || !manualHelp.Success)
        {
            return new MauiCliToolStatus(
                MauiCliToolState.UpdateRequired,
                executablePath,
                version,
                "Update Microsoft.Maui.Cli to a version that supports startup and manual profiling.");
        }

        return new MauiCliToolStatus(
            MauiCliToolState.Available,
            executablePath,
            version);
    }

    public async Task<MauiCliToolUpdateInfo> GetUpdateInfoAsync(
        MauiCliToolStatus status,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(status);

        var installedText = status.Version;
        if (status.State == MauiCliToolState.Missing)
            return new MauiCliToolUpdateInfo(Message: "The MAUI CLI global tool is not installed.");

        NuGetVersion? installed = null;
        if (!string.IsNullOrWhiteSpace(installedText))
            NuGetVersion.TryParse(installedText, out installed);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(UpdateCheckTimeout);

            _nugetClient ??= _nugetClientFactory();
            var versions = await _nugetClient.GetPackageVersionsAsync(
                PackageId,
                includePrerelease: installed?.IsPrerelease ?? true,
                timeout.Token);

            var latest = versions.Count == 0 ? null : versions.Max();
            if (latest is null)
                return new MauiCliToolUpdateInfo(installedText, Message: "No published MAUI CLI versions were found.");

            var latestText = latest.ToNormalizedString();
            if (installed is null)
            {
                return new MauiCliToolUpdateInfo(
                    installedText,
                    latestText,
                    Message: "Sherpa could not read the installed MAUI CLI version.");
            }

            return new MauiCliToolUpdateInfo(
                installed.ToNormalizedString(),
                latestText,
                latest > installed);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new MauiCliToolUpdateInfo(installedText, Message: "Timed out while checking NuGet for MAUI CLI updates.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Unable to check for MAUI CLI updates: {ex.Message}");
            return new MauiCliToolUpdateInfo(installedText, Message: $"Unable to check NuGet for updates: {ex.Message}");
        }
    }

    public Task<ProcessResult> InstallAsync(string? version = null, CancellationToken ct = default)
    {
        return ExecuteAsync(
            "dotnet",
            BuildToolArguments("install", version),
            "Installing MAUI CLI",
            ct);
    }

    public Task<ProcessResult> UpdateAsync(string? version = null, CancellationToken ct = default)
    {
        return ExecuteAsync(
            "dotnet",
            BuildToolArguments("update", version),
            "Updating MAUI CLI",
            ct);
    }

    // Microsoft.Maui.Cli currently ships prerelease-only, so `dotnet tool install/update`
    // cannot resolve it without either an explicit version or --prerelease.
    private static string? NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var trimmed = version.Trim();
        return NuGetVersion.TryParse(trimmed, out var parsed)
            ? parsed.ToNormalizedString()
            : trimmed;
    }

    private static string[] BuildToolArguments(string verb, string? version)
    {
        string[] arguments = ["tool", verb, "--global", PackageId];
        return string.IsNullOrWhiteSpace(version)
            ? [.. arguments, "--prerelease"]
            : [.. arguments, "--version", version];
    }

    public async Task<IReadOnlyList<MauiCliDevice>> GetDevicesAsync(CancellationToken ct = default)
    {
        var status = await GetStatusAsync(ct);
        if (!status.IsAvailable || string.IsNullOrWhiteSpace(status.ExecutablePath))
            throw new InvalidOperationException(status.Message ?? "The MAUI CLI is not ready.");

        var result = await ExecuteAsync(
            status.ExecutablePath,
            ["device", "list", "--platform", "all", "--json", "--ci"],
            "Listing MAUI devices",
            ct);

        var messages = ParseMessages(result.Output);
        var error = messages.OfType<MauiCliErrorMessage>().LastOrDefault();
        if (!result.Success)
        {
            var message = error?.Message;
            if (string.IsNullOrWhiteSpace(message))
                message = string.IsNullOrWhiteSpace(result.Error) ? "Unable to list MAUI devices." : result.Error;
            throw new InvalidOperationException(message);
        }

        var devices = messages
            .OfType<MauiCliDeviceListMessage>()
            .LastOrDefault()
            ?.Devices;

        if (devices is null)
            throw new InvalidOperationException("The MAUI CLI returned an unexpected device response.");

        return devices
            .Where(IsSupportedRunningTarget)
            .OrderBy(x => x.Platform)
            .ThenBy(x => x.Name)
            .ToArray();
    }

    private Task<ProcessResult> ExecuteAsync(
        string command,
        string[] arguments,
        string title,
        CancellationToken ct)
    {
        _logger.LogDebug($"{title}: {command} {string.Join(' ', arguments)}");
        return _process.ExecuteAsync(
            new ProcessRequest(
                command,
                arguments,
                Title: title),
            ct);
    }

    private static IReadOnlyList<MauiCliMessage> ParseMessages(string output)
    {
        return new MauiCliJsonStreamParser().Append(output);
    }

    private static bool IsSupportedRunningTarget(MauiCliDevice device)
    {
        if (!device.IsRunning)
            return false;

        if (device.Platforms.Any(x => string.Equals(x, "android", StringComparison.OrdinalIgnoreCase)))
            return true;

        return device.IsEmulator &&
               device.Platforms.Any(x => string.Equals(x, "ios", StringComparison.OrdinalIgnoreCase));
    }
}
