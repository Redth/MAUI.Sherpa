using Microsoft.Extensions.DependencyInjection;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;

namespace MauiSherpa.Services;

public class ProfilingSessionRunnerService : IProfilingSessionRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggingService _logger;
    private readonly Dictionary<string, IProcessExecutionService> _stepProcesses = new();
    private readonly Dictionary<string, StreamWriter> _stepLogWriters = new();
    private readonly List<ProfilingStepStatus> _steps = new();
    private ProfilingPipelineState _state = ProfilingPipelineState.NotStarted;
    private CancellationTokenSource? _cts;
    private ProfilingCapturePlan? _plan;
    private string? _outputDirectory;
    private DateTime _startTime;
    private volatile bool _stopRequested;
    private int _gcDumpCount;
    private int _traceCount;
    private TaskCompletionSource _stopTcs = new();

    public ProfilingPipelineState State => _state;
    public IReadOnlyList<ProfilingStepStatus> Steps => _steps;

    public event EventHandler<ProfilingPipelineStateChangedEventArgs>? PipelineStateChanged;
    public event EventHandler<ProfilingStepStateChangedEventArgs>? StepStateChanged;
    public event EventHandler<ProfilingStepOutputEventArgs>? StepOutputReceived;

    public ProfilingSessionRunnerService(IServiceProvider serviceProvider, ILoggingService logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<ProfilingPipelineResult> RunAsync(ProfilingCapturePlan plan, CancellationToken ct = default)
    {
        _plan = plan;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _startTime = DateTime.Now;
        _stopRequested = false;
        _gcDumpCount = 0;
        _traceCount = 0;
        _stopTcs = new TaskCompletionSource();

        if (!string.IsNullOrWhiteSpace(plan.OutputDirectory))
            Directory.CreateDirectory(plan.OutputDirectory);

        _outputDirectory = plan.OutputDirectory;

        _steps.Clear();
        _stepProcesses.Clear();
        foreach (var cmd in plan.Commands)
        {
            _steps.Add(new ProfilingStepStatus
            {
                StepId = cmd.Id,
                DisplayName = cmd.DisplayName,
                Kind = cmd.Kind,
                IsLongRunning = cmd.IsLongRunning,
                CanRunParallel = cmd.CanRunParallel,
                StopTrigger = cmd.StopTrigger
            });
        }

        SetPipelineState(ProfilingPipelineState.Running);

        try
        {
            await ExecutePipelineAsync(plan.Commands, _cts.Token);

            SetPipelineState(ProfilingPipelineState.Completing);
            FlushAllStepLogs();
            var (found, missing) = CollectArtifacts(plan);

            // Post-process: convert any .nettrace files to speedscope format
            found = await ConvertTraceArtifactsAsync(found, _cts.Token);

            var finalState = _steps.Any(s => s.State == ProfilingStepState.Failed)
                ? ProfilingPipelineState.Failed
                : ProfilingPipelineState.Completed;
            SetPipelineState(finalState);

            return new ProfilingPipelineResult(
                Success: finalState == ProfilingPipelineState.Completed,
                TotalDuration: DateTime.Now - _startTime,
                FinalState: finalState,
                StepResults: _steps.ToList(),
                ArtifactPaths: found,
                MissingArtifacts: missing);
        }
        catch (OperationCanceledException)
        {
            SetPipelineState(ProfilingPipelineState.Cancelled);
            FlushAllStepLogs();
            KillAllProcesses();
            return new ProfilingPipelineResult(
                Success: false,
                TotalDuration: DateTime.Now - _startTime,
                FinalState: ProfilingPipelineState.Cancelled,
                StepResults: _steps.ToList(),
                ArtifactPaths: Array.Empty<string>(),
                MissingArtifacts: Array.Empty<string>());
        }
        catch (Exception ex)
        {
            if (_stopRequested)
            {
                // Stop was requested — failures during shutdown are expected
                _logger.LogInformation("Pipeline stopped by user. Collecting available artifacts.");
                SetPipelineState(ProfilingPipelineState.Completing);
                FlushAllStepLogs();
                var (stopFound, stopMissing) = CollectArtifacts(plan);
                IReadOnlyList<string> convertedStopFound = await ConvertTraceArtifactsAsync(stopFound, CancellationToken.None);
                SetPipelineState(ProfilingPipelineState.Completed);
                return new ProfilingPipelineResult(
                    Success: true,
                    TotalDuration: DateTime.Now - _startTime,
                    FinalState: ProfilingPipelineState.Completed,
                    StepResults: _steps.ToList(),
                    ArtifactPaths: convertedStopFound,
                    MissingArtifacts: stopMissing);
            }

            _logger.LogError($"Pipeline failed: {ex.Message}", ex);
            SetPipelineState(ProfilingPipelineState.Failed);
            FlushAllStepLogs();
            KillAllProcesses();
            return new ProfilingPipelineResult(
                Success: false,
                TotalDuration: DateTime.Now - _startTime,
                FinalState: ProfilingPipelineState.Failed,
                StepResults: _steps.ToList(),
                ArtifactPaths: Array.Empty<string>(),
                MissingArtifacts: Array.Empty<string>());
        }
    }

    private async Task ExecutePipelineAsync(IReadOnlyList<ProfilingCommandStep> commands, CancellationToken ct)
    {
        var remaining = new HashSet<string>(commands.Select(c => c.Id));
        var completed = new HashSet<string>();
        var longRunningTasks = new Dictionary<string, Task>();

        while (remaining.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            // Find steps whose dependencies are all satisfied.
            // Long-running steps (like dotnet-trace) can proceed when their dependency
            // is merely started, because they themselves wait for the app to connect.
            // Non-long-running steps (like dotnet-gcdump) must wait until their
            // long-running dependency signals IsReady (app is actually connected).
            var ready = commands
                .Where(c => remaining.Contains(c.Id))
                .Where(c => c.DependsOn is null || c.DependsOn.All(dep =>
                    completed.Contains(dep) || IsDependencySatisfied(dep, c.IsLongRunning)))
                .ToList();

            if (ready.Count == 0)
            {
                if (longRunningTasks.Count > 0)
                {
                    // Wait briefly then re-check — a long-running step may signal
                    // IsReady at any time via its output, unblocking dependent steps.
                    var completedTask = await Task.WhenAny(
                        Task.WhenAny(longRunningTasks.Values),
                        Task.Delay(500, ct));

                    foreach (var (id, task) in longRunningTasks.ToList())
                    {
                        if (task.IsCompleted)
                        {
                            completed.Add(id);
                            remaining.Remove(id);
                            longRunningTasks.Remove(id);
                        }
                    }
                    continue;
                }
                throw new InvalidOperationException(
                    $"Pipeline deadlock: steps {string.Join(", ", remaining)} have unresolved dependencies.");
            }

            var parallelSteps = ready.Where(c => c.CanRunParallel).ToList();
            var sequentialSteps = ready.Where(c => !c.CanRunParallel).ToList();

            // Launch parallel steps
            var parallelTasks = new List<Task>();
            foreach (var step in parallelSteps)
            {
                remaining.Remove(step.Id);
                var task = LaunchStepAsync(step, ct);

                if (step.IsLongRunning)
                {
                    longRunningTasks[step.Id] = task;
                    await Task.Delay(500, ct);
                }
                else
                {
                    var stepId = step.Id;
                    parallelTasks.Add(task.ContinueWith(_ =>
                    {
                        completed.Add(stepId);
                    }, TaskContinuationOptions.OnlyOnRanToCompletion));
                }
            }

            // Run sequential steps one at a time
            foreach (var step in sequentialSteps)
            {
                remaining.Remove(step.Id);
                var task = LaunchStepAsync(step, ct);

                if (step.IsLongRunning)
                {
                    longRunningTasks[step.Id] = task;
                    await Task.Delay(500, ct);
                }
                else
                {
                    await task;
                    completed.Add(step.Id);
                }
            }

            if (parallelTasks.Count > 0)
                await Task.WhenAll(parallelTasks);
        }

        // If long-running ManualStop steps remain, wait for StopCapture()
        var manualStopSteps = longRunningTasks.Keys
            .Select(id => commands.First(c => c.Id == id))
            .Where(c => c.StopTrigger == ProfilingStopTrigger.ManualStop)
            .ToList();

        if (manualStopSteps.Count > 0)
        {
            SetPipelineState(ProfilingPipelineState.WaitingForStop);
            await Task.WhenAll(longRunningTasks.Values);
        }
        else if (longRunningTasks.Count > 0)
        {
            // No ManualStop steps, but long-running infrastructure steps (dsrouter, build-and-run)
            // are still running. Enter WaitingForStop so the user can perform on-demand actions
            // (Start Trace, Collect GC Dump) before choosing to stop.
            SetPipelineState(ProfilingPipelineState.WaitingForStop);
            await _stopTcs.Task;
        }

        // Stop any OnPipelineStop steps
        foreach (var (id, task) in longRunningTasks.ToList())
        {
            var step = commands.First(c => c.Id == id);
            if (step.StopTrigger == ProfilingStopTrigger.OnPipelineStop
                && _stepProcesses.TryGetValue(id, out var proc))
            {
                proc.Cancel();
                await task;
            }
        }
    }

    /// <summary>
    /// Checks if a dependency is satisfied for a dependent step.
    /// Long-running dependents (e.g. dotnet-trace) can proceed when the dependency
    /// is just started — they themselves wait for the app. Non-long-running dependents
    /// (e.g. dotnet-gcdump) must wait for IsReady, meaning the dependency has
    /// established its connection and the app is actually available.
    /// </summary>
    private bool IsDependencySatisfied(string depStepId, bool dependentIsLongRunning)
    {
        var status = _steps.FirstOrDefault(s => s.StepId == depStepId);
        if (status is null) return false;
        if (!status.IsLongRunning) return false;
        if (status.State != ProfilingStepState.Running) return false;

        // Long-running dependents can proceed as soon as the dep is started
        if (dependentIsLongRunning) return true;

        // Non-long-running dependents must wait for the dep to signal readiness
        return status.IsReady;
    }

    private async Task LaunchStepAsync(ProfilingCommandStep step, CancellationToken ct)
    {
        var status = _steps.First(s => s.StepId == step.Id);

        if (step.IsOptional && step.RequiredRuntimeBindings?.Count > 0)
        {
            SetStepState(status, ProfilingStepState.Skipped);
            return;
        }

        SetStepState(status, ProfilingStepState.Running);
        status.StartedAt = DateTime.Now;

        var processService = _serviceProvider.GetRequiredService<IProcessExecutionService>();
        _stepProcesses[step.Id] = processService;

        // Open a log file for this step's output
        var logWriter = OpenStepLogWriter(step.Id, step.DisplayName, step.Command, step.Arguments);

        processService.OutputReceived += (_, e) =>
        {
            var line = new ProfilingStepOutputLine(e.Data, e.IsError, DateTime.Now);
            status.OutputLines.Add(line);
            WriteToStepLog(logWriter, e.Data, e.IsError);
            StepOutputReceived?.Invoke(this, new ProfilingStepOutputEventArgs
            {
                StepId = step.Id,
                Text = e.Data,
                IsError = e.IsError
            });

            // Detect readiness for long-running steps by matching output patterns
            if (step.IsLongRunning && !status.IsReady && !e.IsError
                && step.ReadyOutputPattern is not null
                && e.Data.Contains(step.ReadyOutputPattern, StringComparison.OrdinalIgnoreCase))
            {
                status.IsReady = true;
                _logger.LogDebug($"Step '{step.Id}' is ready (matched: {step.ReadyOutputPattern})");
            }
        };

        try
        {
            var request = step.ToProcessRequest();
            var result = await processService.ExecuteAsync(request, ct);

            status.ExitCode = result.ExitCode;
            status.ProcessId = processService.ProcessId;
            status.CompletedAt = DateTime.Now;
            status.Duration = status.CompletedAt.Value - status.StartedAt.Value;

            if (result.Success)
            {
                SetStepState(status, ProfilingStepState.Completed);
            }
            else if (result.WasCancelled || _stopRequested)
            {
                // Step was cancelled or stop was requested — treat as stopped, not failed
                if (status.State != ProfilingStepState.Stopped)
                    SetStepState(status, ProfilingStepState.Stopped);
            }
            else if (step.IsLongRunning && status.State == ProfilingStepState.Stopped)
            {
                // Long-running step was manually stopped — non-zero exit is expected
            }
            else if (step.IsOptional)
            {
                SetStepState(status, ProfilingStepState.Skipped);
                status.ErrorMessage = result.Error;
            }
            else
            {
                SetStepState(status, ProfilingStepState.Failed);
                status.ErrorMessage = result.Error;
                throw new InvalidOperationException(
                    $"Required step '{step.DisplayName}' failed with exit code {result.ExitCode}: {result.Error}");
            }
        }
        catch (OperationCanceledException)
        {
            status.CompletedAt = DateTime.Now;
            status.Duration = status.CompletedAt.Value - (status.StartedAt ?? DateTime.Now);
            SetStepState(status, ProfilingStepState.Cancelled);
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            status.CompletedAt = DateTime.Now;
            status.Duration = status.CompletedAt.Value - (status.StartedAt ?? DateTime.Now);
            status.ErrorMessage = ex.Message;
            SetStepState(status, ProfilingStepState.Failed);
            if (!step.IsOptional)
                throw;
        }
    }

    public async Task StopCaptureAsync()
    {
        _stopRequested = true;
        _logger.LogInformation("StopCapture requested — sending SIGINT to all running processes");

        // Signal the WaitingForStop await so the pipeline can proceed to Completing
        _stopTcs.TrySetResult();

        // Send SIGINT to all running steps. Cancel() sends SIGINT but does NOT
        // immediately cancel the CTS, so WaitForExitAsync in LaunchStepAsync will
        // block until the process actually exits and flushes its output files.
        foreach (var status in _steps.Where(s => s.State == ProfilingStepState.Running))
        {
            SetStepState(status, ProfilingStepState.Stopped);
            if (_stepProcesses.TryGetValue(status.StepId, out var proc))
            {
                proc.Cancel();
            }
        }

        // The pipeline's ExecutePipelineAsync is awaiting Task.WhenAll on long-running
        // tasks. Those tasks will complete once processes exit after SIGINT. We just
        // need to return and let the pipeline flow continue naturally.
        await Task.CompletedTask;
    }

    public void Cancel()
    {
        _logger.LogInformation("Pipeline cancel requested — killing all processes");
        _cts?.Cancel();
        KillAllProcesses();
    }

    private void KillAllProcesses()
    {
        foreach (var (stepId, proc) in _stepProcesses)
        {
            try
            {
                if (proc.CurrentState == ProcessState.Running)
                    proc.Kill();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to kill process for step {stepId}: {ex.Message}");
            }
        }
    }

    private (IReadOnlyList<string> found, IReadOnlyList<string> missing) CollectArtifacts(ProfilingCapturePlan plan)
    {
        var found = new List<string>();
        var missing = new List<string>();

        foreach (var artifact in plan.ExpectedArtifacts)
        {
            // RelativePath already includes the output directory (e.g., "artifacts/profiling/proj/date-1/trace.nettrace")
            // FileName is just the basename (e.g., "trace.nettrace")
            // Use RelativePath as-is, fall back to combining FileName with OutputDirectory
            string path;
            if (!string.IsNullOrWhiteSpace(artifact.RelativePath))
            {
                path = Path.IsPathRooted(artifact.RelativePath)
                    ? artifact.RelativePath
                    : Path.GetFullPath(artifact.RelativePath);
            }
            else if (!string.IsNullOrWhiteSpace(artifact.FileName))
            {
                path = Path.GetFullPath(Path.Combine(plan.OutputDirectory, artifact.FileName));
            }
            else
            {
                continue;
            }

            if (File.Exists(path))
                found.Add(path);
            else
                missing.Add(path);
        }

        // Scan for on-demand artifacts that aren't in the expected list.
        // On-demand gcdumps (memory-1.gcdump) and traces (trace-1.nettrace) use numbered
        // filenames that don't match the plan's expected artifact names.
        if (Directory.Exists(plan.OutputDirectory))
        {
            var foundSet = new HashSet<string>(found, StringComparer.OrdinalIgnoreCase);
            foreach (var pattern in new[] { "*.gcdump", "*.nettrace", "*.speedscope.json" })
            {
                foreach (var file in Directory.GetFiles(plan.OutputDirectory, pattern))
                {
                    var fullPath = Path.GetFullPath(file);
                    if (!foundSet.Contains(fullPath))
                    {
                        found.Add(fullPath);
                        foundSet.Add(fullPath);
                    }
                }
            }
        }

        return (found, missing);
    }

    private void SetPipelineState(ProfilingPipelineState newState)
    {
        var old = _state;
        _state = newState;
        _logger.LogInformation($"Pipeline state: {old} → {newState}");
        PipelineStateChanged?.Invoke(this, new ProfilingPipelineStateChangedEventArgs
        {
            OldState = old,
            NewState = newState
        });
    }

    private void SetStepState(ProfilingStepStatus status, ProfilingStepState newState)
    {
        var old = status.State;
        status.State = newState;
        StepStateChanged?.Invoke(this, new ProfilingStepStateChangedEventArgs
        {
            StepId = status.StepId,
            OldState = old,
            NewState = newState
        });
    }

    public async Task<string?> CollectGcDumpAsync(CancellationToken ct = default)
    {
        if (_plan is null || _outputDirectory is null || _state != ProfilingPipelineState.WaitingForStop)
        {
            _logger.LogWarning("Cannot collect GC dump: pipeline is not in WaitingForStop state.");
            return null;
        }

        var dumpNumber = Interlocked.Increment(ref _gcDumpCount);
        var fileName = $"memory-{dumpNumber}.gcdump";
        var outputPath = Path.Combine(_outputDirectory, fileName);

        // Build arguments: reuse the diagnostic connection info from the running plan
        var arguments = new List<string> { "collect" };
        var diagnostics = _plan.Diagnostics;
        var options = _plan.Options;

        if (diagnostics?.IpcAddress is not null)
        {
            // Standalone dsrouter mode — connect via IPC
            arguments.Add("--diagnostic-port");
            arguments.Add($"{diagnostics.IpcAddress},connect");
        }
        else
        {
            // Desktop mode — use process ID discovered at runtime
            var discoveredPid = _steps
                .FirstOrDefault(s => s.StepId == "discover-process-id")
                ?.OutputLines
                .LastOrDefault(l => !l.IsError)
                ?.Text;

            // Try to get PID from the build-and-run step's process
            var buildStep = _stepProcesses.GetValueOrDefault("build-and-run");
            var pid = options.ProcessId
                ?? (discoveredPid is not null && int.TryParse(discoveredPid.Trim(), out var parsed) ? parsed : (int?)null)
                ?? buildStep?.ProcessId;

            if (pid is null)
            {
                _logger.LogWarning("Cannot collect GC dump: no process ID available.");
                Interlocked.Decrement(ref _gcDumpCount);
                return null;
            }

            arguments.Add("--process-id");
            arguments.Add(pid.ToString()!);
        }

        arguments.Add("-o");
        arguments.Add(outputPath);

        var stepId = $"gcdump-{dumpNumber}";
        var step = new ProfilingCommandStep(
            Id: stepId,
            Kind: ProfilingCommandStepKind.CollectArtifacts,
            DisplayName: $"GC dump #{dumpNumber}",
            Description: $"On-demand heap snapshot #{dumpNumber}.",
            Command: "dotnet-gcdump",
            Arguments: arguments,
            WorkingDirectory: options.WorkingDirectory,
            IsLongRunning: false,
            RequiresManualStop: false,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tool"] = "dotnet-gcdump",
                ["output"] = outputPath
            });

        var status = new ProfilingStepStatus
        {
            StepId = stepId,
            DisplayName = step.DisplayName,
            Kind = step.Kind,
            IsLongRunning = false,
            CanRunParallel = false,
            StopTrigger = ProfilingStopTrigger.None
        };
        _steps.Add(status);
        StepStateChanged?.Invoke(this, new ProfilingStepStateChangedEventArgs
        {
            StepId = stepId,
            OldState = ProfilingStepState.Pending,
            NewState = ProfilingStepState.Pending
        });

        try
        {
            await LaunchStepAsync(step, ct);
            return File.Exists(outputPath) ? outputPath : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"On-demand GC dump failed: {ex.Message}");
            return null;
        }
    }

    private string? _activeTraceStepId;
    private Task? _activeTraceTask;

    /// <summary>
    /// Whether a trace capture is currently active.
    /// </summary>
    public bool IsTraceActive => _activeTraceStepId is not null
        && _steps.Any(s => s.StepId == _activeTraceStepId && s.State == ProfilingStepState.Running);

    /// <summary>
    /// Start an on-demand trace capture. Returns the step ID or null if it cannot start.
    /// The trace runs until StopTraceAsync() is called.
    /// </summary>
    public string? StartTraceAsync()
    {
        if (_plan is null || _outputDirectory is null || _state != ProfilingPipelineState.WaitingForStop)
        {
            _logger.LogWarning("Cannot start trace: pipeline is not in WaitingForStop state.");
            return null;
        }

        if (IsTraceActive)
        {
            _logger.LogWarning("Cannot start trace: a trace is already running.");
            return null;
        }

        var traceNumber = Interlocked.Increment(ref _traceCount);
        var fileName = traceNumber == 1 ? "trace.nettrace" : $"trace-{traceNumber}.nettrace";
        var outputPath = Path.Combine(_outputDirectory, fileName);

        var arguments = new List<string> { "collect" };
        var diagnostics = _plan.Diagnostics;
        var options = _plan.Options;

        if (diagnostics?.IpcAddress is not null)
        {
            arguments.Add("--diagnostic-port");
            arguments.Add($"{diagnostics.IpcAddress},connect");
        }
        else
        {
            var discoveredPid = _steps
                .FirstOrDefault(s => s.StepId == "discover-process-id")
                ?.OutputLines
                .LastOrDefault(l => !l.IsError)
                ?.Text;

            var buildStep = _stepProcesses.GetValueOrDefault("build-and-run");
            var pid = options.ProcessId
                ?? (discoveredPid is not null && int.TryParse(discoveredPid.Trim(), out var parsed) ? parsed : (int?)null)
                ?? buildStep?.ProcessId;

            if (pid is null)
            {
                _logger.LogWarning("Cannot start trace: no process ID available.");
                Interlocked.Decrement(ref _traceCount);
                return null;
            }

            arguments.Add("--process-id");
            arguments.Add(pid.ToString()!);
        }

        arguments.Add("--output");
        arguments.Add(outputPath);

        // Add profiling profiles based on capture kinds
        var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in _plan.Session.CaptureKinds)
        {
            switch (kind)
            {
                case ProfilingCaptureKind.Cpu:
                case ProfilingCaptureKind.Startup:
                    profiles.Add("dotnet-sampled-thread-time");
                    break;
                case ProfilingCaptureKind.Rendering:
                case ProfilingCaptureKind.Network:
                case ProfilingCaptureKind.Energy:
                case ProfilingCaptureKind.SystemTrace:
                    profiles.Add("dotnet-common");
                    break;
            }
        }
        if (profiles.Count == 0)
            profiles.Add("dotnet-sampled-thread-time");
        arguments.Add("--profile");
        arguments.Add(string.Join(",", profiles));

        // Add JIT/Loader provider flags for managed symbol resolution in speedscope.
        // 0x10000018 = JitTracing | NGenTracing | Loader keywords, Verbose level (5).
        arguments.Add("--providers");
        arguments.Add("Microsoft-Windows-DotNETRuntime:0x10000018:5");

        var stepId = $"capture-trace-{traceNumber}";
        _activeTraceStepId = stepId;
        var step = new ProfilingCommandStep(
            Id: stepId,
            Kind: ProfilingCommandStepKind.Capture,
            DisplayName: traceNumber == 1 ? "Collect trace" : $"Collect trace #{traceNumber}",
            Description: "On-demand trace capture.",
            Command: "dotnet-trace",
            Arguments: arguments,
            WorkingDirectory: options.WorkingDirectory,
            IsLongRunning: true,
            RequiresManualStop: true,
            CanRunParallel: false,
            StopTrigger: ProfilingStopTrigger.ManualStop,
            ReadyOutputPattern: "Process",
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tool"] = "dotnet-trace",
                ["output"] = outputPath
            });

        var status = new ProfilingStepStatus
        {
            StepId = stepId,
            DisplayName = step.DisplayName,
            Kind = step.Kind,
            IsLongRunning = true,
            CanRunParallel = false,
            StopTrigger = ProfilingStopTrigger.ManualStop
        };
        _steps.Add(status);
        StepStateChanged?.Invoke(this, new ProfilingStepStateChangedEventArgs
        {
            StepId = stepId,
            OldState = ProfilingStepState.Pending,
            NewState = ProfilingStepState.Pending
        });

        _activeTraceTask = Task.Run(async () =>
        {
            try
            {
                await LaunchStepAsync(step, _cts?.Token ?? CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Trace capture failed: {ex.Message}");
            }
        });

        return stepId;
    }

    /// <summary>
    /// Stop the currently running on-demand trace.
    /// </summary>
    public async Task StopTraceAsync()
    {
        if (_activeTraceStepId is null)
        {
            _logger.LogWarning("No active trace to stop.");
            return;
        }

        var stepId = _activeTraceStepId;
        _logger.LogInformation("Stopping on-demand trace capture...");

        var status = _steps.FirstOrDefault(s => s.StepId == stepId);
        if (status is not null)
            SetStepState(status, ProfilingStepState.Stopped);

        if (_stepProcesses.TryGetValue(stepId, out var proc))
        {
            _logger.LogInformation("Sending cancel signal to process");
            proc.Cancel();
        }

        if (_activeTraceTask is not null)
        {
            try
            {
                await _activeTraceTask.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Trace process did not exit within timeout.");
            }
        }

        _activeTraceStepId = null;
        _activeTraceTask = null;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        FlushAllStepLogs();
        KillAllProcesses();
        _stepProcesses.Clear();
    }

    private StreamWriter? OpenStepLogWriter(string stepId, string displayName, string command, IReadOnlyList<string>? arguments)
    {
        if (string.IsNullOrWhiteSpace(_outputDirectory))
            return null;

        try
        {
            var logPath = Path.Combine(_outputDirectory, $"{stepId}.log");
            var writer = new StreamWriter(logPath, append: false) { AutoFlush = true };
            writer.WriteLine($"# {displayName}");
            writer.WriteLine($"# Command: {command} {(arguments is not null ? string.Join(" ", arguments) : "")}");
            writer.WriteLine($"# Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine();
            _stepLogWriters[stepId] = writer;
            return writer;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to create log file for step '{stepId}': {ex.Message}");
            return null;
        }
    }

    private static void WriteToStepLog(StreamWriter? writer, string text, bool isError)
    {
        if (writer is null)
            return;

        try
        {
            var prefix = isError ? "[ERR] " : "";
            writer.WriteLine($"{prefix}{text}");
        }
        catch
        {
            // Don't let log writing failures break the pipeline
        }
    }

    private void FlushAllStepLogs()
    {
        foreach (var (stepId, writer) in _stepLogWriters)
        {
            try
            {
                var status = _steps.FirstOrDefault(s => s.StepId == stepId);
                if (status is not null)
                {
                    writer.WriteLine();
                    writer.WriteLine($"# Finished: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine($"# State: {status.State}");
                    writer.WriteLine($"# Exit code: {status.ExitCode?.ToString() ?? "N/A"}");
                    if (status.Duration.HasValue)
                        writer.WriteLine($"# Duration: {status.Duration.Value.TotalSeconds:F1}s");
                    if (!string.IsNullOrWhiteSpace(status.ErrorMessage))
                        writer.WriteLine($"# Error: {status.ErrorMessage}");
                }

                writer.Flush();
                writer.Dispose();
            }
            catch
            {
                // Best effort
            }
        }

        _stepLogWriters.Clear();
    }

    /// <summary>
    /// Post-processes trace artifacts: converts any .nettrace files to .speedscope.json
    /// and adds the converted files to the artifact list.
    /// </summary>
    private async Task<IReadOnlyList<string>> ConvertTraceArtifactsAsync(
        IReadOnlyList<string> artifacts, CancellationToken ct)
    {
        var converter = _serviceProvider.GetService<IProfilingArtifactConverterService>();
        if (converter is null)
        {
            _logger.LogWarning("No IProfilingArtifactConverterService registered — skipping trace conversion");
            return artifacts;
        }

        var result = new List<string>(artifacts);

        foreach (var artifact in artifacts)
        {
            if (!artifact.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
                continue;

            // dotnet-trace collect --format Speedscope already emits a .speedscope.json
            // alongside the .nettrace — skip conversion if it already exists.
            var baseName = artifact[..^".nettrace".Length];
            var existingSpeedscope = $"{baseName}.speedscope.json";
            if (File.Exists(existingSpeedscope))
            {
                _logger.LogInformation($"Speedscope file already exists from capture: {Path.GetFileName(existingSpeedscope)}");
                if (!result.Contains(existingSpeedscope))
                    result.Add(existingSpeedscope);
                continue;
            }

            try
            {
                _logger.LogInformation($"Converting {Path.GetFileName(artifact)} to speedscope format...");
                var converted = await converter.ConvertToSpeedscopeAsync(artifact, ct);
                if (converted is not null && !result.Contains(converted))
                {
                    result.Add(converted);
                    _logger.LogInformation($"Speedscope file added: {Path.GetFileName(converted)}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to convert {artifact} to speedscope: {ex.Message}");
            }
        }

        return result;
    }
}
