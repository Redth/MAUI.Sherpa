using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Workloads.Models;
using MauiSherpa.Workloads.Services;
using NuGet.Versioning;

namespace MauiSherpa.Core.Services;

public sealed class DotnetWorkloadService : IDotnetWorkloadService
{
    private readonly IDotnetUpService _dotnetUp;
    private readonly ILoggingService _logger;
    private readonly IGlobalJsonWorkloadPinEditor _pinEditor;
    private readonly ConcurrentDictionary<string, Lazy<Task<DotnetWorkloadInventory>>> _inventoryCache = new();
    private readonly string _runtimeIdentifier;

    public DotnetWorkloadService(
        IDotnetUpService dotnetUp,
        ILoggingService logger,
        IGlobalJsonWorkloadPinEditor? pinEditor = null)
    {
        _dotnetUp = dotnetUp;
        _logger = logger;
        _pinEditor = pinEditor ?? new GlobalJsonWorkloadPinEditor();
        try
        {
            _runtimeIdentifier = DotnetUpRuntimeIdentifier.DetectCurrent();
        }
        catch
        {
            _runtimeIdentifier = "any";
        }
    }

    public async Task<IReadOnlyList<DotnetWorkloadTarget>> GetTargetsAsync(
        DotnetUpListResult? list = null,
        CancellationToken cancellationToken = default)
    {
        list ??= await _dotnetUp.GetListAsync(cancellationToken).ConfigureAwait(false);
        if (list == null)
            return [];

        return list.Installations
            .Where(installation =>
                installation.Component == DotnetUpComponent.Sdk &&
                installation.IsValid &&
                SdkFeatureBand.TryParse(installation.Version, out _))
            .GroupBy(
                installation =>
                {
                    var featureBand = new SdkFeatureBand(installation.Version);
                    return $"{installation.InstallRoot}|{installation.Architecture}|{featureBand}";
                },
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var representative = group
                    .OrderByDescending(
                        item => NuGetVersion.TryParse(item.Version, out var version)
                            ? version
                            : new NuGetVersion(0, 0, 0))
                    .First();
                var architecture = string.IsNullOrWhiteSpace(representative.Architecture)
                    ? RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
                    : representative.Architecture!;
                var dotnetPath = Path.Combine(
                    representative.InstallRoot,
                    OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

                return new DotnetWorkloadTarget
                {
                    InstallRoot = representative.InstallRoot,
                    DotnetPath = dotnetPath,
                    Architecture = architecture,
                    FeatureBand = new SdkFeatureBand(representative.Version),
                    RepresentativeSdkVersion = representative.Version,
                    IsManagedByDotnetUp = true,
                    CanWrite = CanWriteWorkloadState(representative.InstallRoot)
                };
            })
            .OrderByDescending(target => target.FeatureBand)
            .ThenBy(target => target.InstallRoot, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<DotnetWorkloadInventory>> GetInventoriesAsync(
        DotnetUpListResult? list = null,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var targets = await GetTargetsAsync(list, cancellationToken).ConfigureAwait(false);
        return await Task.WhenAll(targets.Select(target =>
            GetInventoryAsync(target, forceRefresh: forceRefresh, cancellationToken: cancellationToken)))
            .ConfigureAwait(false);
    }

    public async Task<DotnetWorkloadInventory> GetInventoryAsync(
        DotnetWorkloadTarget target,
        string? workingDirectory = null,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var cacheKey = $"{target.Key}|{Path.GetFullPath(workingDirectory ?? GetCommandContext(target))}";
        if (forceRefresh)
            _inventoryCache.TryRemove(cacheKey, out _);

        var lazy = _inventoryCache.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<DotnetWorkloadInventory>>(
                () => ReadInventoryAsync(target, workingDirectory, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            _inventoryCache.TryRemove(cacheKey, out _);
            throw;
        }
    }

    public ProcessRequest CreateInstallRequest(
        DotnetWorkloadTarget target,
        IReadOnlyList<string> workloadIds,
        string? workloadSetVersion = null)
    {
        var ids = ValidateWorkloadIds(workloadIds);
        var arguments = new List<string> { "workload", "install" };
        arguments.AddRange(ids);
        if (!string.IsNullOrWhiteSpace(workloadSetVersion))
        {
            arguments.Add("--version");
            arguments.Add(workloadSetVersion);
        }

        return CreateMutationRequest(
            target,
            arguments,
            "Install .NET workloads",
            $"Installing {string.Join(", ", ids)} for SDK feature band {target.FeatureBand}",
            confirmationButtonText: "Install");
    }

    public async Task<DotnetWorkloadSetPreview> GetWorkloadSetPreviewAsync(
        DotnetWorkloadTarget target,
        string workloadSetVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadSetVersion);
        var inventory = await GetInventoryAsync(target, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var result = await RunCaptureAsync(
            target,
            ["workload", "search", "version", workloadSetVersion, "--format", "json"],
            GetCommandContext(target),
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw CreateCliException(target, $"workload set {workloadSetVersion}", result);

        var targetManifests = DotnetWorkloadParser.ParseManifestVersions(result.Output);
        var current = inventory.ManifestVersions.ToDictionary(
            manifest => manifest.ManifestId,
            manifest => manifest.Version,
            StringComparer.OrdinalIgnoreCase);
        var next = targetManifests.ToDictionary(
            manifest => manifest.ManifestId,
            manifest => manifest.Version,
            StringComparer.OrdinalIgnoreCase);
        var changes = current.Keys
            .Concat(next.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new DotnetManifestVersionChange
            {
                ManifestId = id,
                CurrentVersion = current.GetValueOrDefault(id),
                TargetVersion = next.GetValueOrDefault(id)
            })
            .Where(change => !string.Equals(
                change.CurrentVersion,
                change.TargetVersion,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(change => change.ManifestId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DotnetWorkloadSetPreview
        {
            Target = target,
            CurrentVersion = inventory.ActiveWorkloadVersion,
            TargetVersion = workloadSetVersion,
            ManifestChanges = changes,
            InstalledWorkloadIds = inventory.InstalledWorkloads.Select(workload => workload.Id).ToList(),
            CommandLine = $"{target.DotnetPath} workload update --version {workloadSetVersion}"
        };
    }

    public ProcessRequest CreateUninstallRequest(
        DotnetWorkloadTarget target,
        IReadOnlyList<string> workloadIds)
    {
        var ids = ValidateWorkloadIds(workloadIds);
        var arguments = new List<string> { "workload", "uninstall" };
        arguments.AddRange(ids);
        return CreateMutationRequest(
            target,
            arguments,
            "Uninstall .NET workloads",
            $"Removing {string.Join(", ", ids)} from SDK feature band {target.FeatureBand}",
            confirmationButtonText: "Uninstall");
    }

    public ProcessRequest CreateUpdateSetRequest(
        DotnetWorkloadTarget target,
        string workloadSetVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadSetVersion);
        return CreateMutationRequest(
            target,
            ["workload", "update", "--version", workloadSetVersion],
            "Change .NET workload set",
            $"Updating feature band {target.FeatureBand} to workload set {workloadSetVersion}",
            confirmationButtonText: "Change set");
    }

    public ProcessRequest CreateLatestSetUpdateRequest(DotnetWorkloadTarget target) =>
        CreateMutationRequest(
            target,
            target.FeatureBand.IsPrerelease
                ? ["workload", "update", "--include-previews"]
                : ["workload", "update"],
            "Update .NET workload set",
            $"Updating feature band {target.FeatureBand} to its latest compatible workload set",
            confirmationButtonText: "Update set");

    public ProcessRequest CreateRepairRequest(DotnetWorkloadTarget target) =>
        CreateMutationRequest(
            target,
            ["workload", "repair"],
            "Repair .NET workloads",
            $"Repairing installed workloads for SDK feature band {target.FeatureBand}",
            confirmationButtonText: "Repair");

    public ProcessRequest CreateRestoreRequest(
        DotnetWorkloadTarget target,
        string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        return CreateMutationRequest(
            target,
            ["workload", "restore"],
            "Restore project workloads",
            $"Restoring workloads for '{projectDirectory}'",
            Path.GetFullPath(projectDirectory),
            confirmationButtonText: "Restore");
    }

    public GlobalJsonWorkloadPinPreview PreviewProjectWorkloadPin(
        string projectDirectory,
        string? workloadSetVersion) =>
        _pinEditor.Preview(projectDirectory, workloadSetVersion);

    public Task ApplyProjectWorkloadPinAsync(
        GlobalJsonWorkloadPinPreview preview,
        CancellationToken cancellationToken = default) =>
        _pinEditor.ApplyAsync(preview, cancellationToken);

    public void Invalidate(string? installRoot = null)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            _inventoryCache.Clear();
            return;
        }

        foreach (var key in _inventoryCache.Keys.Where(
                     key => key.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase)))
            _inventoryCache.TryRemove(key, out _);
    }

    private async Task<DotnetWorkloadInventory> ReadInventoryAsync(
        DotnetWorkloadTarget target,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(target.DotnetPath))
            throw new FileNotFoundException("The selected dotnet executable no longer exists.", target.DotnetPath);

        var diagnostics = new List<string>();
        var commandDirectory = Path.GetFullPath(workingDirectory ?? GetCommandContext(target));

        var versionResult = await RunCaptureAsync(
            target,
            ["workload", "--version"],
            commandDirectory,
            cancellationToken).ConfigureAwait(false);
        if (versionResult.ExitCode != 0)
            throw CreateCliException(target, "dotnet workload --version", versionResult);

        var workloadVersion = DotnetWorkloadParser.ParseWorkloadVersion(versionResult.Output);
        var globalJson = workingDirectory == null
            ? null
            : new GlobalJsonService().GetGlobalJson(workingDirectory);
        var updateMode = ResolveUpdateMode(target, globalJson, diagnostics);
        var versionSource = globalJson?.WorkloadSetVersion != null
            ? DotnetWorkloadVersionSource.GlobalJson
            : updateMode == DotnetWorkloadUpdateMode.WorkloadSet
                ? DotnetWorkloadVersionSource.MachineDefault
                : updateMode == DotnetWorkloadUpdateMode.Manifests
                    ? DotnetWorkloadVersionSource.LooseManifests
                    : DotnetWorkloadVersionSource.Unknown;

        DotnetWorkloadListResult workloadList;
        var machineList = await RunCaptureAsync(
            target,
            ["workload", "list", "--machine-readable"],
            commandDirectory,
            cancellationToken).ConfigureAwait(false);
        var machineReadable = machineList.ExitCode == 0;
        if (machineReadable)
        {
            try
            {
                workloadList = DotnetWorkloadParser.ParseMachineReadableList(machineList.Output);
            }
            catch (FormatException ex)
            {
                diagnostics.Add($"Machine-readable workload list was not understood: {ex.Message}");
                machineReadable = false;
                workloadList = await ReadPlainListAsync(target, commandDirectory, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            diagnostics.Add("This SDK does not support the machine-readable workload list; using the table fallback.");
            workloadList = await ReadPlainListAsync(target, commandDirectory, cancellationToken)
                .ConfigureAwait(false);
        }

        IReadOnlyList<DotnetWorkloadSetVersion> availableSets = [];
        var versionsResult = await RunCaptureAsync(
            target,
            ["workload", "search", "version", "--take", "20", "--format", "json", "--include-previews"],
            commandDirectory,
            cancellationToken).ConfigureAwait(false);
        var jsonVersionSearch = versionsResult.ExitCode == 0;
        if (jsonVersionSearch)
        {
            try
            {
                availableSets = DotnetWorkloadParser.ParseAvailableSetVersions(versionsResult.Output);
            }
            catch (FormatException ex)
            {
                diagnostics.Add($"Available workload-set versions were not understood: {ex.Message}");
                jsonVersionSearch = false;
            }
        }
        else
        {
            diagnostics.Add($"Available workload-set versions could not be queried: {FirstError(versionsResult)}");
        }

        IReadOnlyList<DotnetManifestVersion> manifestVersions = [];
        if (updateMode == DotnetWorkloadUpdateMode.WorkloadSet)
        {
            var manifestResult = await RunCaptureAsync(
                target,
                ["workload", "search", "version", workloadVersion, "--format", "json"],
                commandDirectory,
                cancellationToken).ConfigureAwait(false);
            if (manifestResult.ExitCode == 0)
            {
                try
                {
                    manifestVersions = DotnetWorkloadParser.ParseManifestVersions(manifestResult.Output);
                }
                catch (FormatException ex)
                {
                    diagnostics.Add($"The active workload-set manifest map was not understood: {ex.Message}");
                }
            }
            else
            {
                diagnostics.Add($"The active workload-set manifest map could not be queried: {FirstError(manifestResult)}");
            }
        }
        else if (updateMode == DotnetWorkloadUpdateMode.Manifests)
        {
            try
            {
                manifestVersions = WorkloadInstallStateReader.ReadManifestVersions(target);
            }
            catch (FormatException ex)
            {
                diagnostics.Add($"The loose-manifest install state was not understood: {ex.Message}");
            }
        }

        var (manifests, discoveredVersions) = await LoadActiveManifestsAsync(
            target,
            manifestVersions,
            cancellationToken).ConfigureAwait(false);
        if (manifestVersions.Count == 0)
            manifestVersions = discoveredVersions;

        IReadOnlyList<ResolvedWorkloadDefinition> availableWorkloads;
        try
        {
            availableWorkloads = WorkloadGraphResolver.Resolve(manifests, _runtimeIdentifier);
        }
        catch (InvalidDataException ex)
        {
            diagnostics.Add(ex.Message);
            availableWorkloads = [];
        }

        var installationState = WorkloadInstallationStateResolver.Resolve(
            workloadList.Installed,
            availableWorkloads);
        diagnostics.AddRange(installationState.Diagnostics);

        return new DotnetWorkloadInventory
        {
            Target = target,
            UpdateMode = updateMode,
            VersionSource = versionSource,
            ActiveWorkloadVersion = workloadVersion,
            InstalledWorkloads = workloadList.Installed,
            EffectiveInstalledWorkloads = installationState.States,
            AvailableWorkloads = availableWorkloads,
            ManifestVersions = manifestVersions,
            AvailableSetVersions = availableSets,
            WorkloadUpdates = workloadList.Updates,
            Capabilities = new DotnetWorkloadCapabilities
            {
                MachineReadableList = machineReadable,
                WorkloadVersion = true,
                JsonVersionSearch = jsonVersionSearch
            },
            Diagnostics = diagnostics
        };
    }

    private async Task<DotnetWorkloadListResult> ReadPlainListAsync(
        DotnetWorkloadTarget target,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var result = await RunCaptureAsync(
            target,
            ["workload", "list"],
            workingDirectory,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw CreateCliException(target, "dotnet workload list", result);
        return DotnetWorkloadParser.ParsePlainList(result.Output);
    }

    private static async Task<(IReadOnlyList<WorkloadManifest> Manifests, IReadOnlyList<DotnetManifestVersion> Versions)>
        LoadActiveManifestsAsync(
            DotnetWorkloadTarget target,
            IReadOnlyList<DotnetManifestVersion> manifestVersions,
            CancellationToken cancellationToken)
    {
        var manifests = new List<WorkloadManifest>();
        var versions = new List<DotnetManifestVersion>();

        if (manifestVersions.Count > 0)
        {
            foreach (var item in manifestVersions)
            {
                var file = Path.Combine(
                    target.InstallRoot,
                    "sdk-manifests",
                    item.FeatureBand,
                    item.ManifestId,
                    item.Version,
                    "WorkloadManifest.json");
                await AddManifestAsync(file, item, manifests, versions, cancellationToken).ConfigureAwait(false);
            }
            return (manifests, versions);
        }

        var manifestBand = target.FeatureBand.ToString();
        var bandRoot = Path.Combine(target.InstallRoot, "sdk-manifests", manifestBand);
        if (!Directory.Exists(bandRoot))
            return (manifests, versions);

        foreach (var manifestDirectory in Directory.GetDirectories(bandRoot))
        {
            var versionDirectory = Directory.GetDirectories(manifestDirectory)
                .OrderByDescending(path =>
                    NuGetVersion.TryParse(Path.GetFileName(path), out var version)
                        ? version
                        : new NuGetVersion(0, 0, 0))
                .FirstOrDefault();
            if (versionDirectory == null)
                continue;

            var item = new DotnetManifestVersion
            {
                ManifestId = Path.GetFileName(manifestDirectory),
                Version = Path.GetFileName(versionDirectory),
                FeatureBand = manifestBand
            };
            await AddManifestAsync(
                Path.Combine(versionDirectory, "WorkloadManifest.json"),
                item,
                manifests,
                versions,
                cancellationToken).ConfigureAwait(false);
        }

        return (manifests, versions);
    }

    private static async Task AddManifestAsync(
        string file,
        DotnetManifestVersion item,
        ICollection<WorkloadManifest> manifests,
        ICollection<DotnetManifestVersion> versions,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(file))
            return;

        var json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        var manifest = WorkloadManifestService.ParseManifest(json);
        if (manifest == null)
            return;
        manifests.Add(manifest);
        versions.Add(item);
    }

    private ProcessRequest CreateMutationRequest(
        DotnetWorkloadTarget target,
        IReadOnlyList<string> arguments,
        string title,
        string description,
        string? workingDirectory = null,
        string? confirmationButtonText = null)
    {
        var directory = Path.GetFullPath(workingDirectory ?? GetCommandContext(target));
        return new ProcessRequest(
            Command: target.DotnetPath,
            Arguments: arguments.ToArray(),
            WorkingDirectory: directory,
            RequiresElevation: !target.CanWrite,
            ElevationPrompt: target.CanWrite
                ? null
                : $"Workload changes require permission to write to {target.InstallRoot}.",
            Environment: CreateEnvironment(target),
            Title: title,
            Description: description,
            UsePseudoTerminal: SupportsTerminalProgress,
            ConfirmationButtonText: confirmationButtonText);
    }

    private static DotnetWorkloadUpdateMode ResolveUpdateMode(
        DotnetWorkloadTarget target,
        GlobalJsonInfo? globalJson,
        ICollection<string> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(globalJson?.WorkloadsUpdateMode))
        {
            if (string.Equals(
                    globalJson.WorkloadsUpdateMode,
                    "workload-set",
                    StringComparison.OrdinalIgnoreCase))
                return DotnetWorkloadUpdateMode.WorkloadSet;
            if (string.Equals(
                    globalJson.WorkloadsUpdateMode,
                    "manifests",
                    StringComparison.OrdinalIgnoreCase))
                return DotnetWorkloadUpdateMode.Manifests;

            diagnostics.Add(
                $"global.json specifies an unsupported sdk.workloads-update-mode value: '{globalJson.WorkloadsUpdateMode}'.");
        }

        if (!string.IsNullOrWhiteSpace(globalJson?.WorkloadSetVersion))
            return DotnetWorkloadUpdateMode.WorkloadSet;

        try
        {
            return WorkloadInstallStateReader.ReadUpdateMode(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            diagnostics.Add($"The workload install state could not be read: {ex.Message}");
            return DotnetWorkloadUpdateMode.Unknown;
        }
    }

    private static IReadOnlyList<string> ValidateWorkloadIds(IReadOnlyList<string> workloadIds)
    {
        ArgumentNullException.ThrowIfNull(workloadIds);
        var result = workloadIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (result.Count == 0)
            throw new ArgumentException("At least one workload ID is required.", nameof(workloadIds));
        if (result.Any(id => id.Any(char.IsWhiteSpace)))
            throw new ArgumentException("Workload IDs cannot contain whitespace.", nameof(workloadIds));
        return result;
    }

    private string GetCommandContext(DotnetWorkloadTarget target)
    {
        var safeVersion = string.Concat(target.RepresentativeSdkVersion.Select(
            character => char.IsLetterOrDigit(character) || character is '.' or '-' ? character : '_'));
        var directory = Path.Combine(
            Path.GetTempPath(),
            "MauiSherpa",
            "workload-contexts",
            safeVersion);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "global.json");
        var allowPrerelease = target.FeatureBand.IsPrerelease ? ",\n    \"allowPrerelease\": true" : string.Empty;
        var content = $"{{\n  \"sdk\": {{\n    \"version\": \"{target.RepresentativeSdkVersion}\",\n    \"rollForward\": \"disable\"{allowPrerelease}\n  }}\n}}\n";
        if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            File.WriteAllText(path, content);
        return directory;
    }

    private static Dictionary<string, string> CreateEnvironment(DotnetWorkloadTarget target)
    {
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_ROOT"] = target.InstallRoot,
            ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
            ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["PATH"] = $"{target.InstallRoot}{separator}{existingPath}"
        };
    }

    private static bool CanWriteWorkloadState(string installRoot)
    {
        var metadataRoot = Path.Combine(installRoot, "metadata");
        var workloadMetadata = Path.Combine(metadataRoot, "workloads");
        var statePath = Directory.Exists(workloadMetadata)
            ? workloadMetadata
            : Directory.Exists(metadataRoot)
                ? metadataRoot
                : installRoot;
        return CanWriteDirectory(installRoot) && CanWriteDirectory(statePath);
    }

    private static bool CanWriteDirectory(string path) =>
        OperatingSystem.IsWindows()
            ? WindowsAccess(path, WriteAccess) == 0
            : UnixAccess(path, WriteAccess) == 0;

    private const int WriteAccess = 2;

    [DllImport("libc", EntryPoint = "access", SetLastError = true)]
    private static extern int UnixAccess(string path, int mode);

    [DllImport("msvcrt", EntryPoint = "_waccess", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WindowsAccess(string path, int mode);

    private async Task<CaptureResult> RunCaptureAsync(
        DotnetWorkloadTarget target,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = target.DotnetPath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (var value in CreateEnvironment(target))
            startInfo.Environment[value.Key] = value.Value;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to launch {target.DotnetPath}.");
        var command = string.Join(' ', arguments);
        _logger.LogDebug($"Reading workload state: {target.FeatureBand} - dotnet {command}");

        var output = new StringBuilder();
        var error = new StringBuilder();
        var outputClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data == null)
                outputClosed.TrySetResult();
            else
                lock (output)
                    output.AppendLine(eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data == null)
                errorClosed.TrySetResult();
            else
                lock (error)
                    error.AppendLine(eventArgs.Data);
        };
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => exited.TrySetResult();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (process.HasExited)
            exited.TrySetResult();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));

        try
        {
            await exited.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"dotnet {command} did not finish within 45 seconds for SDK feature band {target.FeatureBand}.");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        var streamsClosed = Task.WhenAll(outputClosed.Task, errorClosed.Task);
        if (await Task.WhenAny(streamsClosed, Task.Delay(500, CancellationToken.None)).ConfigureAwait(false) != streamsClosed)
        {
            try
            {
                process.CancelOutputRead();
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                process.CancelErrorRead();
            }
            catch (InvalidOperationException)
            {
            }
        }

        string outputText;
        string errorText;
        lock (output)
            outputText = output.ToString();
        lock (error)
            errorText = error.ToString();
        _logger.LogDebug($"Read workload state: {target.FeatureBand} - dotnet {command} exited {process.ExitCode}");

        return new CaptureResult(
            process.ExitCode,
            outputText,
            errorText);
    }

    private static InvalidOperationException CreateCliException(
        DotnetWorkloadTarget target,
        string operation,
        CaptureResult result) =>
        new($"{operation} failed for {target.FeatureBand}: {FirstError(result)}");

    private static string FirstError(CaptureResult result) =>
        (string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error).Trim();

    private static bool SupportsTerminalProgress =>
        OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst();

    private sealed record CaptureResult(int ExitCode, string Output, string Error);
}
