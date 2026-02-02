using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Platform.Services;

/// <summary>
/// Service for managing process execution modal state
/// </summary>
public class ProcessModalService : IProcessModalService
{
    private readonly IProcessExecutionService _processService;
    private TaskCompletionSource<ProcessResult?>? _completionSource;
    private ProcessResult? _pendingResult;

    public event Action? OnModalShown;
    public event Action? OnModalClosed;
    public event Action<ProcessRequest>? OnShowRequested;
    public event Action<bool>? OnConfirmationResponse;
    public event Func<string, string, string, Task>? OnTryWithCopilotRequested;

    public ProcessRequest? CurrentRequest { get; private set; }
    public bool IsVisible { get; private set; }
    public bool RequiresConfirmation { get; private set; }
    public ProcessResult? LastResult => _pendingResult;

    public ProcessModalService(IProcessExecutionService processService)
    {
        _processService = processService;
    }

    public async Task<ProcessResult?> ShowProcessAsync(ProcessRequest request, bool requireConfirmation = true)
    {
        if (IsVisible)
        {
            throw new InvalidOperationException("A process modal is already being shown");
        }

        CurrentRequest = request;
        RequiresConfirmation = requireConfirmation;
        IsVisible = true;
        _pendingResult = null;
        _completionSource = new TaskCompletionSource<ProcessResult?>();

        OnModalShown?.Invoke();
        OnShowRequested?.Invoke(request);

        var result = await _completionSource.Task;

        IsVisible = false;
        CurrentRequest = null;
        OnModalClosed?.Invoke();

        return result;
    }

    /// <summary>
    /// Called by the modal component when user confirms execution
    /// </summary>
    public void ConfirmExecution()
    {
        OnConfirmationResponse?.Invoke(true);
    }

    /// <summary>
    /// Called by the modal component when user cancels before execution
    /// </summary>
    public void CancelBeforeExecution()
    {
        OnConfirmationResponse?.Invoke(false);
        _completionSource?.TrySetResult(null);
    }

    /// <summary>
    /// Called by the modal component when process completes.
    /// Does NOT close the modal - user must click Close.
    /// </summary>
    public void CompleteWithResult(ProcessResult result)
    {
        // Store the result but don't complete the task yet
        // The modal stays visible until user clicks Close
        _pendingResult = result;
    }

    /// <summary>
    /// Called by the modal component when user closes the modal
    /// </summary>
    public void Close()
    {
        // Return the pending result (or null if cancelled before execution)
        _completionSource?.TrySetResult(_pendingResult);
        _pendingResult = null;
    }

    /// <summary>
    /// Trigger Copilot to try fixing the failed process
    /// </summary>
    public async Task TryWithCopilotAsync(string output)
    {
        if (OnTryWithCopilotRequested != null && CurrentRequest != null)
        {
            await OnTryWithCopilotRequested.Invoke(
                CurrentRequest.Title ?? "Command execution",
                CurrentRequest.CommandLine ?? "",
                output
            );
        }
    }
}
