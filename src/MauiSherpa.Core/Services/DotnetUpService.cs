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

    public ProcessRequest CreateProcessRequest(
        IReadOnlyList<string> arguments, string? title = null, string? description = null) =>
        new(
            Command: ExecutablePath,
            Arguments: arguments.ToArray(),
            WorkingDirectory: null,
            RequiresElevation: false,
            ElevationPrompt: null,
            Environment: null,
            Title: title,
            Description: description);

    public ProcessRequest InstallSdkRequest(string? channel = null, bool terminalMode = true) =>
        CreateProcessRequest(
            DotnetUpArguments.SdkInstall(channel, setDefaultInstall: terminalMode),
            title: "Install .NET SDK",
            description: channel is null
                ? "Installing the latest .NET SDK via dotnetup"
                : $"Installing .NET SDK channel '{channel}' via dotnetup");

    public ProcessRequest UpdateSdksRequest() =>
        CreateProcessRequest(
            DotnetUpArguments.SdkUpdate(),
            title: "Update .NET SDKs",
            description: "Updating tracked .NET SDKs via dotnetup");

    public ProcessRequest UninstallSdkRequest(string channel, DotnetUpInstallSource? source = null) =>
        CreateProcessRequest(
            DotnetUpArguments.SdkUninstall(channel, source),
            title: "Uninstall .NET SDK",
            description: $"Uninstalling .NET SDK channel '{channel}' via dotnetup");

    public ProcessRequest InstallRuntimeRequest(string? spec = null) =>
        CreateProcessRequest(
            DotnetUpArguments.RuntimeInstall(spec),
            title: "Install .NET Runtime",
            description: spec is null
                ? "Installing the latest .NET runtime via dotnetup"
                : $"Installing .NET runtime '{spec}' via dotnetup");

    public ProcessRequest UpdateRuntimesRequest() =>
        CreateProcessRequest(
            DotnetUpArguments.RuntimeUpdate(),
            title: "Update .NET Runtimes",
            description: "Updating tracked .NET runtimes via dotnetup");

    public ProcessRequest UninstallRuntimeRequest(string spec) =>
        CreateProcessRequest(
            DotnetUpArguments.RuntimeUninstall(spec),
            title: "Uninstall .NET Runtime",
            description: $"Uninstalling .NET runtime '{spec}' via dotnetup");

    public ProcessRequest UpdateAllRequest() =>
        CreateProcessRequest(
            DotnetUpArguments.UpdateAll(),
            title: "Update .NET tools",
            description: "Updating all tracked .NET SDKs and runtimes via dotnetup");

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
