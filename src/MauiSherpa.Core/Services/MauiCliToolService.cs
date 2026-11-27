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
    private readonly Func<string, INuGetClient> _nugetClientFactory;
    private readonly Func<Task<string?>> _getFeedOverrideAsync;
    private INuGetClient? _nugetClient;
    private string? _nugetClientSourceUrl;

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public MauiCliToolService(
        IProcessExecutionService process,
        ILoggingService logger,
        IEncryptedSettingsService? settingsService = null)
        : this(
            process,
            logger,
            () => MauiCliExecutableResolver.Resolve(),
            feedUrl => new NuGetClient(feedUrl),
            settingsService is null ? null : () => ReadConfiguredFeedAsync(settingsService, logger))
    {
    }

    public MauiCliToolService(
        IProcessExecutionService process,
        ILoggingService logger,
        Func<string?> resolveExecutable,
        Func<INuGetClient>? nugetClientFactory = null)
        : this(
            process,
            logger,
            resolveExecutable,
            nugetClientFactory is null ? (feedUrl => new NuGetClient(feedUrl)) : (_ => nugetClientFactory()),
            getFeedOverrideAsync: null)
    {
    }

    public MauiCliToolService(
        IProcessExecutionService process,
        ILoggingService logger,
        Func<string?> resolveExecutable,
        Func<string, INuGetClient>? nugetClientFactory,
        Func<Task<string?>>? getFeedOverrideAsync)
    {
        _process = process;
        _logger = logger;
        _resolveExecutable = resolveExecutable;
        _nugetClientFactory = nugetClientFactory ?? (feedUrl => new NuGetClient(feedUrl));
        _getFeedOverrideAsync = getFeedOverrideAsync ?? (() => Task.FromResult<string?>(null));
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

            var feedUrl = await GetEffectiveFeedOverrideAsync() ?? NuGetClient.DefaultPackageSourceUrl;
            var nugetClient = GetOrCreateNuGetClient(feedUrl);
            var versions = await nugetClient.GetPackageVersionsAsync(
                PackageId,
                includePrerelease: true,
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

    public async Task<ProcessResult> InstallAsync(string? version = null, CancellationToken ct = default)
    {
        var feedOverride = await GetEffectiveFeedOverrideAsync();
        return await ExecuteAsync(
            "dotnet",
            BuildToolArguments("install", version, feedOverride),
            "Installing MAUI CLI",
            ct);
    }

    public async Task<ProcessResult> UpdateAsync(string? version = null, CancellationToken ct = default)
    {
        var feedOverride = await GetEffectiveFeedOverrideAsync();
        return await ExecuteAsync(
            "dotnet",
            BuildToolArguments("update", version, feedOverride),
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

    private static string[] BuildToolArguments(string verb, string? version, string? sourceOverride = null)
    {
        List<string> arguments = ["tool", verb, "--global", PackageId];
        if (string.IsNullOrWhiteSpace(version))
        {
            arguments.Add("--prerelease");
        }
        else
        {
            arguments.Add("--version");
            arguments.Add(version);
        }

        if (!string.IsNullOrWhiteSpace(sourceOverride))
        {
            arguments.Add("--source");
            arguments.Add(sourceOverride);
        }

        return [.. arguments];
    }

    private INuGetClient GetOrCreateNuGetClient(string feedUrl)
    {
        if (_nugetClient is not null &&
            string.Equals(_nugetClientSourceUrl, feedUrl, StringComparison.OrdinalIgnoreCase))
        {
            return _nugetClient;
        }

        (_nugetClient as IDisposable)?.Dispose();
        _nugetClient = _nugetClientFactory(feedUrl);
        _nugetClientSourceUrl = feedUrl;
        return _nugetClient;
    }

    private async Task<string?> GetEffectiveFeedOverrideAsync()
    {
        var configured = await _getFeedOverrideAsync();
        return DotnetToolNuGetFeedOptions.Normalize(configured);
    }

    private static async Task<string?> ReadConfiguredFeedAsync(
        IEncryptedSettingsService settingsService,
        ILoggingService logger)
    {
        try
        {
            var settings = await settingsService.GetSettingsAsync().ConfigureAwait(false);
            return settings.Preferences.DotnetToolNuGetFeedUrl;
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Unable to read configured .NET tools NuGet feed: {ex.Message}");
            return null;
        }
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
