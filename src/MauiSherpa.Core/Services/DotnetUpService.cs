using System.Diagnostics;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Drives the <c>dotnetup</c> user-level .NET toolchain manager. Bootstraps the binary
/// (download + SHA-512 verify into <c>~/.dotnetup</c>), queries installed SDKs/runtimes, and
/// builds <see cref="ProcessRequest"/>s for install/update/uninstall operations.
///
/// Sherpa always invokes dotnetup by full path so it never depends on the user's shell PATH.
/// </summary>
public class DotnetUpService : IDotnetUpService
{
    private readonly ILoggingService _logger;
    private readonly Lazy<HttpClient> _httpClient;
    private readonly string _rid;
    private readonly string _toolDirectory;
    private DotnetUpdateResolver? _updateResolver;
    private GlobalJsonResolver? _globalJsonResolver;

    public DotnetUpService(ILoggingService logger)
    {
        _logger = logger;
        _httpClient = new Lazy<HttpClient>(CreateHttpClient);

        try
        {
            _rid = DotnetUpRuntimeIdentifier.DetectCurrent();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to detect dotnetup RID: {ex.Message}", ex);
            _rid = string.Empty;
        }

        _toolDirectory = ResolveDefaultToolDirectory();
    }

    /// <summary>Default dotnetup binary directory: <c>~/.dotnetup</c> (matches the official installer).</summary>
    private static string ResolveDefaultToolDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".dotnetup");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MauiSherpa-dotnetup");
        return client;
    }

    public string ToolDirectory => _toolDirectory;

    public string ExecutablePath
    {
        get
        {
            var exeName = string.IsNullOrEmpty(_rid)
                ? (OperatingSystem.IsWindows() ? "dotnetup.exe" : "dotnetup")
                : DotnetUpRuntimeIdentifier.GetExecutableFileName(_rid);
            return Path.Combine(_toolDirectory, exeName);
        }
    }

    public bool IsInstalled => File.Exists(ExecutablePath);

    public async Task<bool> EnsureInstalledAsync(
        bool force = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (IsInstalled && !force)
        {
            progress?.Report("dotnetup is already installed.");
            return true;
        }

        if (string.IsNullOrEmpty(_rid) || !DotnetUpRuntimeIdentifier.IsSupportedRid(_rid))
        {
            _logger.LogError($"dotnetup is not available for this platform (RID '{_rid}').");
            progress?.Report($"dotnetup is not available for this platform (RID '{_rid}').");
            return false;
        }

        try
        {
            Directory.CreateDirectory(_toolDirectory);
            var downloader = new DotnetUpDownloader(_httpClient.Value);
            await downloader.DownloadAndVerifyAsync(
                _rid, ExecutablePath, quality: null, progress, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation($"dotnetup installed at {ExecutablePath}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to install dotnetup: {ex.Message}", ex);
            progress?.Report($"Error: {ex.Message}");
            return false;
        }
    }

    public async Task<DotnetUpToolInfo?> GetToolInfoAsync(CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
            return null;

        try
        {
            var (exitCode, output, error) = await RunAsync(
                DotnetUpArguments.Info(), cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                _logger.LogWarning($"dotnetup --info exited with code {exitCode}: {error}");
                return null;
            }

            return DotnetUpParser.ParseInfo(output);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to read dotnetup --info: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<DotnetUpToolUpdateInfo?> GetToolUpdateInfoAsync(
        DotnetUpToolInfo? installedInfo = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsInstalled || string.IsNullOrEmpty(_rid))
            return null;

        try
        {
            installedInfo ??= await GetToolInfoAsync(cancellationToken).ConfigureAwait(false);
            if (installedInfo is null)
                return null;

            var downloader = new DotnetUpDownloader(_httpClient.Value);
            var published = await downloader.GetPublishedArtifactAsync(
                _rid, cancellationToken: cancellationToken).ConfigureAwait(false);

            await using var executable = File.OpenRead(ExecutablePath);
            var installedHash = await DotnetUpDownloader.ComputeSha512Async(
                executable, cancellationToken).ConfigureAwait(false);

            return new DotnetUpToolUpdateInfo
            {
                InstalledVersion = installedInfo.Version,
                AvailableVersion = published.Version,
                UpdateAvailable = !DotnetUpDownloader.HashesEqual(installedHash, published.Sha512)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not check for dotnetup updates: {ex.Message}");
            return null;
        }
    }

    public async Task<DotnetUpListResult?> GetListAsync(CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
            return null;

        try
        {
            var (exitCode, output, error) = await RunAsync(
                DotnetUpArguments.List(), cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                _logger.LogWarning($"dotnetup list exited with code {exitCode}: {error}");
                return null;
            }

            return DotnetUpParser.ParseList(output);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to read dotnetup list: {ex.Message}", ex);
            return null;
        }
    }

    public async Task<IReadOnlyList<DotnetUpdatePreview>> GetUpdatePreviewAsync(
        DotnetUpListResult list, CancellationToken cancellationToken = default)
    {
        var resolver = _updateResolver ??= new DotnetUpdateResolver();
        return await resolver.GetUpdatePreviewAsync(list, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GlobalJsonResolution> InspectProjectFolderAsync(
        string folderPath, DotnetUpListResult list, CancellationToken cancellationToken = default)
    {
        var parsed = (_globalJsonResolver ??= new GlobalJsonResolver()).Resolve(folderPath);

        // Nothing to resolve against the feed when there's no SDK version pinned.
        if (parsed.Channel is not { Length: > 0 } channel)
            return parsed;

        var matchingGlobalJsonSpecs = parsed.GlobalJsonPath == null
            ? []
            : list.InstallSpecs.Where(s =>
                s.Component == DotnetUpComponent.Sdk &&
                string.Equals(
                    s.GlobalJsonPath,
                    parsed.GlobalJsonPath,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        var matchingChannelSpecs = list.InstallSpecs.Where(s =>
            s.Component == DotnetUpComponent.Sdk &&
            string.Equals(
                s.VersionOrChannel.Trim(),
                channel,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var matchingSpec = SelectUnambiguousInstallSpec(matchingGlobalJsonSpecs) ??
            SelectUnambiguousInstallSpec(matchingChannelSpecs);
        var alreadyTracked = matchingGlobalJsonSpecs.Count > 0 || matchingChannelSpecs.Count > 0;

        try
        {
            var resolver = _updateResolver ??= new DotnetUpdateResolver();
            var (available, installed) = await resolver
                .ResolveChannelAsync(DotnetUpComponent.Sdk, channel, list, cancellationToken)
                .ConfigureAwait(false);
            var installedSdk = installed == null
                ? null
                : SelectInstalledSdkTarget(list.Installations, installed, matchingSpec);

            return parsed with
            {
                ResolvedVersion = available,
                InstalledVersion = installed,
                InstalledSdkInstallRoot = installedSdk?.InstallRoot,
                InstalledSdkArchitecture = installedSdk?.Architecture,
                Satisfied = installed != null,
                UpdateAvailable = !parsed.IsPinned &&
                    DotnetUpdateResolver.IsUpdateAvailable(available, installed),
                AlreadyTracked = alreadyTracked,
                Status = available == null ? GlobalJsonStatus.Unresolved : GlobalJsonStatus.Resolved,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to resolve global.json channel '{channel}': {ex.Message}");
            return parsed with
            {
                AlreadyTracked = alreadyTracked,
                Status = GlobalJsonStatus.Unresolved,
            };
        }
    }

    private static DotnetUpInstallSpec? SelectUnambiguousInstallSpec(
        IReadOnlyList<DotnetUpInstallSpec> specs)
    {
        var targets = specs
            .GroupBy(
                spec => $"{spec.InstallRoot}|{spec.Architecture}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        return targets.Count == 1 ? targets[0] : null;
    }

    internal static DotnetUpInstallation? SelectInstalledSdkTarget(
        IReadOnlyList<DotnetUpInstallation> installations,
        string version,
        DotnetUpInstallSpec? matchingSpec)
    {
        var candidates = installations
            .Where(installation =>
                installation.Component == DotnetUpComponent.Sdk &&
                installation.IsValid &&
                string.Equals(installation.Version, version, StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                installation => $"{installation.InstallRoot}|{installation.Architecture}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (matchingSpec != null)
        {
            candidates = candidates
                .Where(installation =>
                    string.Equals(
                        installation.InstallRoot,
                        matchingSpec.InstallRoot,
                        StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(matchingSpec.Architecture) ||
                     string.Equals(
                         installation.Architecture,
                         matchingSpec.Architecture,
                         StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    public ProcessRequest CreateProcessRequest(
        IReadOnlyList<string> arguments, string? title = null, string? description = null,
        string? workingDirectory = null, bool usePseudoTerminal = false,
        bool acceptsStandardInput = false) =>
        new(
            Command: ExecutablePath,
            Arguments: arguments.ToArray(),
            WorkingDirectory: workingDirectory,
            RequiresElevation: false,
            ElevationPrompt: null,
            Environment: null,
            Title: title,
            Description: description,
            UsePseudoTerminal: usePseudoTerminal,
            AcceptsStandardInput: acceptsStandardInput);

    private static bool SupportsTerminalProgress =>
        OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst();

    public ProcessRequest InstallForProjectRequest(string folderPath) =>
        CreateProcessRequest(
            // No channel + working directory = the folder → dotnetup resolves its global.json and
            // records the spec with source = that global.json path. No --set-default-install: this
            // installs the project's SDK without changing the user's default/terminal install.
            DotnetUpArguments.SdkInstall(
                channel: null,
                setDefaultInstall: false,
                noProgress: !SupportsTerminalProgress),
            title: "Install project SDK",
            description: $"Installing the .NET SDK required by global.json in '{folderPath}'",
            workingDirectory: folderPath,
            usePseudoTerminal: SupportsTerminalProgress);

    public ProcessRequest InstallSdkRequest(string? channel = null, bool terminalMode = true) =>
        CreateProcessRequest(
            DotnetUpArguments.SdkInstall(
                channel,
                setDefaultInstall: terminalMode,
                noProgress: !SupportsTerminalProgress),
            title: "Install .NET SDK",
            description: channel is null
                ? "Installing the latest .NET SDK via dotnetup"
                : $"Installing .NET SDK channel '{channel}' via dotnetup",
            usePseudoTerminal: SupportsTerminalProgress,
            acceptsStandardInput: SupportsTerminalProgress);

    public ProcessRequest UpdateSdksRequest() =>
        CreateProcessRequest(
            DotnetUpArguments.SdkUpdate(noProgress: !SupportsTerminalProgress),
            title: "Update .NET SDKs",
            description: "Updating tracked .NET SDKs via dotnetup",
            usePseudoTerminal: SupportsTerminalProgress);

    public ProcessRequest UninstallSdkRequest(string channel, DotnetUpInstallSource? source = null) =>
        CreateProcessRequest(
            DotnetUpArguments.SdkUninstall(channel, source),
            title: "Uninstall .NET SDK",
            description: $"Uninstalling .NET SDK channel '{channel}' via dotnetup");

    public ProcessRequest InstallRuntimeRequest(string? spec = null) =>
        CreateProcessRequest(
            DotnetUpArguments.RuntimeInstall(spec, noProgress: !SupportsTerminalProgress),
            title: "Install .NET Runtime",
            description: spec is null
                ? "Installing the latest .NET runtime via dotnetup"
                : $"Installing .NET runtime '{spec}' via dotnetup",
            usePseudoTerminal: SupportsTerminalProgress);

    public ProcessRequest UpdateRuntimesRequest() =>
        CreateProcessRequest(
            DotnetUpArguments.RuntimeUpdate(noProgress: !SupportsTerminalProgress),
            title: "Update .NET Runtimes",
            description: "Updating tracked .NET runtimes via dotnetup",
            usePseudoTerminal: SupportsTerminalProgress);

    public ProcessRequest UninstallRuntimeRequest(string spec) =>
        CreateProcessRequest(
            DotnetUpArguments.RuntimeUninstall(spec),
            title: "Uninstall .NET Runtime",
            description: $"Uninstalling .NET runtime '{spec}' via dotnetup");

    public ProcessRequest UpdateAllRequest() =>
        CreateProcessRequest(
            DotnetUpArguments.UpdateAll(noProgress: !SupportsTerminalProgress),
            title: "Update .NET tools",
            description: "Updating all tracked .NET SDKs and runtimes via dotnetup",
            usePseudoTerminal: SupportsTerminalProgress);

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, string.Empty, "Failed to start dotnetup process.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        return (process.ExitCode, output, error);
    }
}
