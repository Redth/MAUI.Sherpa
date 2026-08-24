using System.Xml.Linq;
using MauiSherpa.Workloads.Services;

namespace MauiSherpa.Bundles;

public sealed record BundlePreparationResult(
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string> Diagnostics);

internal sealed class BundleToolchainHost
{
    public Func<Environment.SpecialFolder, string> GetFolderPath { get; init; } = Environment.GetFolderPath;
    public Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;
    public Func<string, bool> FileExists { get; init; } = File.Exists;
    public Func<string, string, SearchOption, IEnumerable<string>> EnumerateDirectories { get; init; } = Directory.EnumerateDirectories;
    public Func<string, string> ReadAllText { get; init; } = File.ReadAllText;
}

public sealed class BundleToolchainInstaller
{
    private readonly IBundleProcessRunner _processRunner;
    private readonly HttpClient _httpClient;
    private readonly BundleToolchainHost _host;

    public BundleToolchainInstaller(
        IBundleProcessRunner processRunner,
        HttpClient? httpClient = null)
        : this(processRunner, httpClient, new BundleToolchainHost())
    {
    }

    internal BundleToolchainInstaller(
        IBundleProcessRunner processRunner,
        HttpClient? httpClient,
        BundleToolchainHost host)
    {
        _processRunner = processRunner;
        _httpClient = httpClient ?? new HttpClient();
        _host = host;
    }

    public async Task<BundlePreparationResult> PrepareAsync(
        BundleToolchainRequirements requirements,
        BundlePlatform platform,
        IReadOnlySet<string> secretValues,
        bool dryRun,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateHost(platform);

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();

        if (!string.IsNullOrWhiteSpace(requirements.DotnetSdkVersion))
        {
            var sdks = await RunAsync("dotnet", ["--list-sdks"], null, secretValues, progress, cancellationToken);
            EnsureSuccess(sdks, $"inspect installed .NET SDKs for {requirements.DotnetSdkVersion}");

            if (IsDotnetSdkInstalled(sdks.StandardOutput, requirements.DotnetSdkVersion))
            {
                diagnostics.Add($"Verified .NET SDK {requirements.DotnetSdkVersion}.");
            }
            else if (dryRun)
            {
                diagnostics.Add($"Would install .NET SDK {requirements.DotnetSdkVersion}.");
            }
            else
            {
                var dotnetUp = await EnsureDotnetUpAsync(progress, cancellationToken);
                var install = await RunAsync(
                    dotnetUp,
                    ["sdk", "install", requirements.DotnetSdkVersion, "--set-default-install", "--no-progress"],
                    null,
                    secretValues,
                    progress,
                    cancellationToken);
                EnsureSuccess(install, $"install .NET SDK {requirements.DotnetSdkVersion}");
                diagnostics.Add($"Installed .NET SDK {requirements.DotnetSdkVersion}.");
            }
        }

        var workloads = requirements.Workloads
            .Where(workload => !string.IsNullOrWhiteSpace(workload))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (workloads.Length > 0)
        {
            var workloadList = await RunAsync("dotnet", ["workload", "list"], null, secretValues, progress, cancellationToken);
            EnsureSuccess(workloadList, "inspect installed .NET workloads");
            var installedWorkloads = ParseInstalledWorkloads(workloadList.StandardOutput);

            foreach (var workload in workloads)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (installedWorkloads.Contains(workload))
                {
                    diagnostics.Add($"Verified .NET workload {workload}.");
                    continue;
                }

                var arguments = new List<string> { "workload", "install", workload };
                if (!string.IsNullOrWhiteSpace(requirements.WorkloadSetVersion))
                {
                    arguments.Add("--version");
                    arguments.Add(requirements.WorkloadSetVersion);
                }
                arguments.Add("--skip-manifest-update");

                if (dryRun)
                {
                    diagnostics.Add($"Would install .NET workload {workload}.");
                    continue;
                }

                var result = await RunAsync("dotnet", arguments, null, secretValues, progress, cancellationToken);
                EnsureSuccess(result, $"install .NET workload {workload}");
                diagnostics.Add($"Prepared .NET workload {workload}.");
            }
        }

        var androidPackages = requirements.AndroidSdkPackages
            .Where(package => !string.IsNullOrWhiteSpace(package))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (platform == BundlePlatform.Android && androidPackages.Length > 0)
        {
            var sdkManager = FindSdkManager()
                ?? throw new InvalidOperationException(
                    "Android sdkmanager was not found. Set ANDROID_HOME or ANDROID_SDK_ROOT and install command-line tools.");
            var installedPackagesResult = await RunAsync(
                sdkManager,
                ["--list_installed"],
                null,
                secretValues,
                progress,
                cancellationToken);
            EnsureSuccess(installedPackagesResult, "inspect installed Android SDK packages");
            var installedPackages = ParseInstalledAndroidPackages(installedPackagesResult.StandardOutput);

            foreach (var package in androidPackages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (installedPackages.Contains(package))
                {
                    diagnostics.Add($"Verified Android SDK package {package}.");
                    continue;
                }

                if (dryRun)
                {
                    diagnostics.Add($"Would install Android SDK package {package}.");
                    continue;
                }
                var result = await RunAsync(
                    sdkManager,
                    ["--install", package],
                    null,
                    secretValues,
                    progress,
                    cancellationToken);
                EnsureSuccess(result, $"install Android SDK package {package}");
                diagnostics.Add($"Prepared Android SDK package {package}.");
            }
        }

        if (platform == BundlePlatform.Android && !string.IsNullOrWhiteSpace(requirements.JdkVersion))
        {
            var java = await RunAsync("java", ["-version"], null, secretValues, progress, cancellationToken);
            if (java.ExitCode != 0 ||
                !IsJdkVersionInstalled(java.StandardOutput + Environment.NewLine + java.StandardError, requirements.JdkVersion))
            {
                throw new InvalidOperationException(
                    $"JDK {requirements.JdkVersion} is required but was not found. Set JAVA_HOME to a matching JDK.");
            }
            diagnostics.Add($"Verified JDK {requirements.JdkVersion}.");
        }

        if (IsApple(platform) && !string.IsNullOrWhiteSpace(requirements.XcodeVersion))
        {
            var xcodePath = FindXcode(requirements.XcodeVersion)
                ?? throw new InvalidOperationException(
                    $"Xcode {requirements.XcodeVersion} is required but no matching bundle exists in /Applications.");
            environment["DEVELOPER_DIR"] = Path.Combine(xcodePath, "Contents", "Developer");
            diagnostics.Add($"Selected Xcode {requirements.XcodeVersion} through DEVELOPER_DIR.");
        }

        return new BundlePreparationResult(environment, diagnostics);
    }

    private async Task<string> EnsureDotnetUpAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var rid = DotnetUpRuntimeIdentifier.DetectCurrent();
        var executable = Path.Combine(
            _host.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnetup",
            DotnetUpRuntimeIdentifier.GetExecutableFileName(rid));
        if (!_host.FileExists(executable))
        {
            var downloader = new DotnetUpDownloader(_httpClient);
            await downloader.DownloadAndVerifyAsync(rid, executable, progress: progress, cancellationToken: cancellationToken);
        }
        return executable;
    }

    private async Task<BundleProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlySet<string> secretValues,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        await _processRunner.RunAsync(
            new BundleProcessRequest
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                SecretValues = secretValues
            },
            progress,
            cancellationToken);

    private static void EnsureSuccess(BundleProcessResult result, string action)
    {
        if (result.ExitCode != 0)
        {
            var details = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            details = string.IsNullOrWhiteSpace(details) ? "The command returned a non-zero exit code." : details.Trim();
            throw new InvalidOperationException($"Failed to {action}: {details}");
        }
    }

    private string? FindSdkManager()
    {
        var sdkRoot = _host.GetEnvironmentVariable("ANDROID_HOME")
            ?? _host.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (string.IsNullOrWhiteSpace(sdkRoot))
            return null;

        var executable = OperatingSystem.IsWindows() ? "sdkmanager.bat" : "sdkmanager";
        return new[]
        {
            Path.Combine(sdkRoot, "cmdline-tools", "latest", "bin", executable),
            Path.Combine(sdkRoot, "tools", "bin", executable)
        }.FirstOrDefault(_host.FileExists);
    }

    private string? FindXcode(string version)
    {
        if (!OperatingSystem.IsMacOS())
            return null;

        foreach (var path in _host.EnumerateDirectories("/Applications", "Xcode*.app", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (name.Contains(version, StringComparison.OrdinalIgnoreCase) ||
                name.Replace(' ', '_').Contains(version.Replace(' ', '_'), StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            if (VersionMatches(version, TryReadXcodeVersion(path)))
                return path;
        }

        return null;
    }

    private string? TryReadXcodeVersion(string bundlePath)
    {
        foreach (var plistPath in new[]
                 {
                     Path.Combine(bundlePath, "Contents", "version.plist"),
                     Path.Combine(bundlePath, "Contents", "Info.plist")
                 })
        {
            if (!_host.FileExists(plistPath))
                continue;

            try
            {
                var document = XDocument.Parse(_host.ReadAllText(plistPath));
                var version = TryReadPlistValue(document, "CFBundleShortVersionString")
                    ?? TryReadPlistValue(document, "ProductVersion");
                if (!string.IsNullOrWhiteSpace(version))
                    return version;
            }
            catch
            {
                // Best effort; continue probing other metadata files.
            }
        }

        return null;
    }

    private static string? TryReadPlistValue(XDocument document, string keyName)
    {
        var dict = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "dict");
        if (dict is null)
            return null;

        var children = dict.Elements().ToList();
        for (var index = 0; index < children.Count - 1; index++)
        {
            if (children[index].Name.LocalName == "key" &&
                string.Equals(children[index].Value, keyName, StringComparison.Ordinal) &&
                children[index + 1].Name.LocalName == "string")
            {
                return children[index + 1].Value;
            }
        }

        return null;
    }

    private static bool VersionMatches(string requestedVersion, string? installedVersion)
    {
        if (string.IsNullOrWhiteSpace(installedVersion))
            return false;

        var requested = requestedVersion.Trim();
        var installed = installedVersion.Trim();
        return string.Equals(installed, requested, StringComparison.OrdinalIgnoreCase)
            || installed.StartsWith(requested + '.', StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDotnetSdkInstalled(string output, string version) =>
        output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Any(installedVersion => string.Equals(installedVersion, version, StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> ParseInstalledWorkloads(string output)
    {
        var workloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.StartsWith("-", StringComparison.Ordinal) ||
                trimmed.StartsWith("Installed", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Workload", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var token = trimmed.Split([' ', '	'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(token) && token.Any(char.IsLetter))
                workloads.Add(token);
        }

        return workloads;
    }

    private static HashSet<string> ParseInstalledAndroidPackages(string output)
    {
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.StartsWith("-", StringComparison.Ordinal) ||
                trimmed.EndsWith(':') ||
                trimmed.StartsWith("Path", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var token = trimmed.Contains('|')
                ? trimmed.Split('|', 2)[0].Trim()
                : trimmed.Split([' ', '	'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(token))
                packages.Add(token);
        }

        return packages;
    }

    private static bool IsJdkVersionInstalled(string output, string version)
    {
        var requested = version.Trim();
        foreach (var line in output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var marker = line.IndexOf("version", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
                continue;

            var firstQuote = line.IndexOf('"', marker);
            if (firstQuote < 0)
                continue;
            var secondQuote = line.IndexOf('"', firstQuote + 1);
            if (secondQuote <= firstQuote + 1)
                continue;

            var installed = line[(firstQuote + 1)..secondQuote];
            return VersionMatches(requested, installed);
        }

        return false;
    }

    private static void ValidateHost(BundlePlatform platform)
    {
        if (IsApple(platform) && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException($"{platform} builds require macOS.");
        if (platform == BundlePlatform.Windows && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows builds require Windows.");
    }

    private static bool IsApple(BundlePlatform platform) =>
        platform is BundlePlatform.Ios or BundlePlatform.MacOS or BundlePlatform.MacCatalyst;
}
