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
