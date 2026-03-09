using Microsoft.Extensions.DependencyInjection;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;

namespace MauiSherpa.Services;

public class ProfilingSessionRunnerService : IProfilingSessionRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggingService _logger;
    private readonly Dictionary<string, IProcessExecutionService> _stepProcesses = new();
    private readonly List<ProfilingStepStatus> _steps = new();
    private ProfilingPipelineState _state = ProfilingPipelineState.NotStarted;
    private CancellationTokenSource? _cts;
    private ProfilingCapturePlan? _plan;
    private DateTime _startTime;

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

        if (!string.IsNullOrWhiteSpace(plan.OutputDirectory))
            Directory.CreateDirectory(plan.OutputDirectory);

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
            var (found, missing) = CollectArtifacts(plan);

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
            _logger.LogError($"Pipeline failed: {ex.Message}", ex);
            SetPipelineState(ProfilingPipelineState.Failed);
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

            // Find steps whose dependencies are all satisfied
            var ready = commands
                .Where(c => remaining.Contains(c.Id))
                .Where(c => c.DependsOn is null || c.DependsOn.All(dep =>
                    completed.Contains(dep) || IsLongRunningAndStarted(dep)))
                .ToList();

            if (ready.Count == 0)
            {
                if (longRunningTasks.Count > 0)
                {
                    await Task.WhenAny(longRunningTasks.Values);
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

    private bool IsLongRunningAndStarted(string stepId)
    {
        var status = _steps.FirstOrDefault(s => s.StepId == stepId);
        return status is { IsLongRunning: true, State: ProfilingStepState.Running };
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

        processService.OutputReceived += (_, e) =>
        {
            var line = new ProfilingStepOutputLine(e.Data, e.IsError, DateTime.Now);
            status.OutputLines.Add(line);
            StepOutputReceived?.Invoke(this, new ProfilingStepOutputEventArgs
            {
                StepId = step.Id,
                Text = e.Data,
                IsError = e.IsError
            });
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
            else if (result.WasCancelled)
            {
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

    public void StopCapture()
    {
        _logger.LogInformation("StopCapture requested — sending SIGINT to manual-stop processes");

        foreach (var status in _steps.Where(s => s.State == ProfilingStepState.Running))
        {
            if (_stepProcesses.TryGetValue(status.StepId, out var proc))
            {
                var cmd = _plan?.Commands.FirstOrDefault(c => c.Id == status.StepId);
                if (cmd?.StopTrigger == ProfilingStopTrigger.ManualStop || cmd?.RequiresManualStop == true)
                {
                    SetStepState(status, ProfilingStepState.Stopped);
                    proc.Cancel();
                }
            }
        }
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
            var relativePath = artifact.RelativePath ?? artifact.FileName;
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            var fullPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(plan.OutputDirectory, relativePath);

            if (File.Exists(fullPath))
                found.Add(fullPath);
            else
                missing.Add(fullPath);
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
        KillAllProcesses();
        _stepProcesses.Clear();
    }
}
