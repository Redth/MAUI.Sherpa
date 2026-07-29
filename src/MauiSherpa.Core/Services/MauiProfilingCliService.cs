using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Models.Profiling;

namespace MauiSherpa.Core.Services;

public sealed class MauiProfilingCliService : IMauiProfilingCliService
{
    private readonly IProcessExecutionService _process;
    private readonly IMauiCliToolService _toolService;
    private readonly ILoggingService _logger;
    private readonly object _sync = new();
    private readonly List<MauiCliStatusMessage> _statusMessages = [];

    private MauiCliJsonStreamParser _parser = new();
    private MauiProfileResult? _profileResult;
    private MauiCliErrorMessage? _error;
    private MauiProfileRequest? _activeRequest;
    private MauiProfileRunState _state = MauiProfileRunState.Idle;
    private bool _disposed;

    public MauiProfileRunState State
    {
        get
        {
            lock (_sync)
                return _state;
        }
    }

    public event EventHandler<MauiProfileStateChangedEventArgs>? StateChanged;
    public event EventHandler<MauiCliMessageEventArgs>? MessageReceived;

    public MauiProfilingCliService(
        IProcessExecutionService process,
        IMauiCliToolService toolService,
        ILoggingService logger)
    {
        _process = process;
        _toolService = toolService;
        _logger = logger;
        _process.OutputReceived += OnOutputReceived;
    }

    public async Task<MauiProfileExecutionResult> RunAsync(
        MauiProfileRequest request,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            if (_state is MauiProfileRunState.Starting or
                MauiProfileRunState.AwaitingRecording or
                MauiProfileRunState.Recording or
                MauiProfileRunState.Finalizing)
            {
                throw new InvalidOperationException("A profiling session is already running.");
            }

            _parser = new MauiCliJsonStreamParser();
            _profileResult = null;
            _error = null;
            _statusMessages.Clear();
            _activeRequest = request;
        }

        var toolStatus = await _toolService.GetStatusAsync(ct);
        if (!toolStatus.IsAvailable || string.IsNullOrWhiteSpace(toolStatus.ExecutablePath))
            throw new InvalidOperationException(toolStatus.Message ?? "The MAUI CLI is not ready.");

        SetState(MauiProfileRunState.Starting);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var arguments = MauiProfileCommandBuilder.BuildArguments(request);
        var processTask = _process.ExecuteAsync(
            new ProcessRequest(
                toolStatus.ExecutablePath,
                arguments,
                WorkingDirectory: Path.GetDirectoryName(request.ProjectPath),
                Title: request.Mode == MauiProfileMode.Startup
                    ? "Profiling app startup"
                    : "Profiling app interaction",
                // Interaction captures drive the CLI's start/stop prompts over stdin.
                AcceptsStandardInput: request.Mode == MauiProfileMode.Interaction),
            ct);

        if (request.Mode == MauiProfileMode.Interaction)
            SetState(MauiProfileRunState.AwaitingRecording);

        var processResult = await processTask;

        MauiProfileResult? profile;
        MauiCliErrorMessage? error;
        IReadOnlyList<MauiCliStatusMessage> statusMessages;
        lock (_sync)
        {
            profile = _profileResult;
            error = _error;
            statusMessages = _statusMessages.ToArray();
            _activeRequest = null;
        }

        if (processResult.WasCancelled || processResult.ExitCode == 130)
        {
            SetState(MauiProfileRunState.Cancelled);
        }
        else if (!processResult.Success || error is not null || profile is null)
        {
            if (profile is null && IsRecoverableError(error))
            {
                profile = TryRecoverProfile(request, startedAtUtc, processResult);
                if (profile is not null)
                    error = null;
            }

            if (profile is not null && error is null)
            {
                SetState(MauiProfileRunState.Completed);
            }
            else
            {
                error = NormalizeError(error, processResult);
                SetState(MauiProfileRunState.Failed);
            }
        }
        else
        {
            SetState(MauiProfileRunState.Completed);
        }

        return new MauiProfileExecutionResult(
            processResult,
            profile,
            error,
            statusMessages);
    }

    /// <summary>
    /// The CLI reports its own result-serialization defect as a normal error envelope even
    /// though the trace was already written, so that specific failure must not discard a
    /// capture that exists on disk.
    /// </summary>
    private static bool IsRecoverableError(MauiCliErrorMessage? error)
    {
        if (error is null)
            return true;

        return MauiProfileArtifactRecovery.IsResultSerializationFailure(
            string.Join(Environment.NewLine, error.Message, error.NativeError));
    }

    private MauiProfileResult? TryRecoverProfile(
        MauiProfileRequest request,
        DateTimeOffset startedAtUtc,
        ProcessResult processResult)
    {
        MauiProfileResult? recovered;
        try
        {
            recovered = MauiProfileArtifactRecovery.TryRecover(
                request,
                startedAtUtc,
                DateTimeOffset.UtcNow,
                CombineOutput(processResult));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to recover a MAUI profile from disk.", ex);
            return null;
        }

        if (recovered is not null)
        {
            _logger.LogWarning(
                $"The MAUI CLI exited with code {processResult.ExitCode} but wrote '{recovered.OutputPath}'. " +
                "Recovering the capture from disk.");
        }

        return recovered;
    }

    private static MauiCliErrorMessage NormalizeError(
        MauiCliErrorMessage? error,
        ProcessResult processResult)
    {
        var combined = string.Join(
            Environment.NewLine,
            error?.Message,
            error?.NativeError,
            CombineOutput(processResult));

        if (MauiProfileArtifactRecovery.IsResultSerializationFailure(combined))
        {
            return new MauiCliErrorMessage(
                "SHERPA_PROFILE_CLI_RESULT_SERIALIZATION",
                "tool",
                "error",
                "The MAUI CLI failed while reporting its result and no profile was found on disk.",
                error?.Message ?? processResult.Error,
                new MauiCliRemediation(
                    "command",
                    "dotnet tool update -g Microsoft.Maui.Cli",
                    [
                        "This is a defect in the installed Microsoft.Maui.Cli build.",
                        "Update the MAUI CLI, then run the capture again."
                    ]));
        }

        return error ?? new MauiCliErrorMessage(
            "SHERPA_PROFILE_RESULT",
            "tool",
            "error",
            string.IsNullOrWhiteSpace(processResult.Error)
                ? "The MAUI CLI did not return a profiling result."
                : processResult.Error);
    }

    private static string CombineOutput(ProcessResult processResult) =>
        string.Join(Environment.NewLine, processResult.Output, processResult.Error);

    public async Task BeginRecordingAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeRequest?.Mode != MauiProfileMode.Interaction ||
            State != MauiProfileRunState.AwaitingRecording)
        {
            throw new InvalidOperationException("The interaction profile is not waiting to begin recording.");
        }

        await SendEnterAsync(ct);
        SetState(MauiProfileRunState.Recording);
    }

    public async Task StopRecordingAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activeRequest?.Mode != MauiProfileMode.Interaction ||
            State != MauiProfileRunState.Recording)
        {
            throw new InvalidOperationException("No interaction profile is currently recording.");
        }

        SetState(MauiProfileRunState.Finalizing);
        await SendEnterAsync(ct);
    }

    /// <summary>
    /// The MAUI CLI advances its interactive prompts on a bare newline.
    /// </summary>
    private async Task SendEnterAsync(CancellationToken ct)
    {
        if (!await _process.SendInputAsync(Environment.NewLine, ct))
            throw new InvalidOperationException("The MAUI CLI is no longer accepting input.");
    }

    public void Cancel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State is not (MauiProfileRunState.Starting or
            MauiProfileRunState.AwaitingRecording or
            MauiProfileRunState.Recording or
            MauiProfileRunState.Finalizing))
        {
            return;
        }

        SetState(MauiProfileRunState.Cancelled);
        _process.Cancel();
    }

    private void OnOutputReceived(object? sender, ProcessOutputEventArgs e)
    {
        IReadOnlyList<MauiCliMessage> messages;
        lock (_sync)
        {
            messages = _parser.Append(e.Data);
            foreach (var message in messages)
            {
                switch (message)
                {
                    case MauiProfileResultMessage result:
                        _profileResult = result.Result;
                        break;
                    case MauiCliErrorMessage error:
                        _error = error;
                        break;
                    case MauiCliStatusMessage status:
                        _statusMessages.Add(status);
                        break;
                }
            }
        }

        foreach (var message in messages)
            MessageReceived?.Invoke(this, new MauiCliMessageEventArgs(message));
    }

    private void SetState(MauiProfileRunState state)
    {
        MauiProfileRunState oldState;
        lock (_sync)
        {
            oldState = _state;
            if (oldState == state)
                return;
            _state = state;
        }

        _logger.LogDebug($"MAUI profile state: {oldState} -> {state}");
        StateChanged?.Invoke(this, new MauiProfileStateChangedEventArgs(oldState, state));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _process.OutputReceived -= OnOutputReceived;
        _disposed = true;
    }
}
