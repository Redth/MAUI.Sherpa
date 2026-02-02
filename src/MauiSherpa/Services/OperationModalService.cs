using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

/// <summary>
/// Service for managing operation modal state and execution
/// </summary>
public class OperationModalService : IOperationModalService
{
    private TaskCompletionSource<OperationResult>? _completionSource;
    private OperationResult? _pendingResult;

    public event Action? OnModalShown;
    public event Action? OnModalClosed;
    public event Action<string, string, bool>? OnShowRequested;
    public event Action<OperationLogEntry>? OnLogEntry;
    public event Action<string>? OnStatusChanged;
    public event Action<int?>? OnProgressChanged;
    public event Action<OperationState>? OnStateChanged;
    public event Func<string, string, IEnumerable<OperationLogEntry>, Task>? OnTryWithCopilotRequested;

    public string? Title { get; private set; }
    public string? Description { get; private set; }
    public bool IsVisible { get; private set; }
    public bool IsRunning { get; private set; }
    public bool CanCancel { get; private set; }
    public OperationState CurrentState { get; private set; } = OperationState.Pending;
    public string? CurrentStatus { get; private set; }
    public int? CurrentProgress { get; private set; }
    public List<OperationLogEntry> LogEntries { get; } = new();

    private CancellationTokenSource? _cts;

    public async Task<OperationResult> RunAsync(
        string title,
        string description,
        Func<IOperationContext, Task<bool>> operation,
        bool canCancel = true)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("An operation is already running");
        }

        // Reset state
        Title = title;
        Description = description;
        CanCancel = canCancel;
        IsVisible = true;
        IsRunning = true;
        CurrentState = OperationState.Running;
        CurrentStatus = null;
        CurrentProgress = null;
        LogEntries.Clear();
        _pendingResult = null;
        _cts = new CancellationTokenSource();
        _completionSource = new TaskCompletionSource<OperationResult>();

        OnModalShown?.Invoke();
        OnShowRequested?.Invoke(title, description, canCancel);
        OnStateChanged?.Invoke(CurrentState);

        var startTime = DateTime.Now;
        var context = new OperationContext(this, _cts.Token);

        try
        {
            context.LogInfo($"Starting: {description}");
            
            var success = await operation(context);
            var duration = DateTime.Now - startTime;

            CurrentState = success ? OperationState.Completed : OperationState.Failed;
            OnStateChanged?.Invoke(CurrentState);

            if (success)
            {
                context.LogSuccess("Operation completed successfully");
            }
            else
            {
                context.LogError("Operation failed");
            }

            _pendingResult = new OperationResult(
                success,
                success ? "Operation completed successfully" : "Operation failed",
                duration,
                CurrentState,
                LogEntries.ToList()
            );
        }
        catch (OperationCanceledException)
        {
            var duration = DateTime.Now - startTime;
            CurrentState = OperationState.Cancelled;
            OnStateChanged?.Invoke(CurrentState);
            context.LogWarning("Operation was cancelled");

            _pendingResult = new OperationResult(
                false,
                "Operation was cancelled",
                duration,
                OperationState.Cancelled,
                LogEntries.ToList()
            );
        }
        catch (Exception ex)
        {
            var duration = DateTime.Now - startTime;
            CurrentState = OperationState.Failed;
            OnStateChanged?.Invoke(CurrentState);
            context.LogError($"Error: {ex.Message}");

            _pendingResult = new OperationResult(
                false,
                ex.Message,
                duration,
                OperationState.Failed,
                LogEntries.ToList()
            );
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }

        // Wait for user to close the modal
        var result = await _completionSource.Task;

        IsVisible = false;
        Title = null;
        Description = null;
        OnModalClosed?.Invoke();

        return result;
    }

    /// <summary>
    /// Request cancellation of the running operation
    /// </summary>
    public void RequestCancellation()
    {
        if (CanCancel && IsRunning)
        {
            _cts?.Cancel();
        }
    }

    /// <summary>
    /// Close the modal (called by UI after operation completes)
    /// </summary>
    public void Close()
    {
        _completionSource?.TrySetResult(_pendingResult ?? new OperationResult(
            false,
            "Operation was closed",
            TimeSpan.Zero,
            CurrentState,
            LogEntries.ToList()
        ));
        _pendingResult = null;
    }

    /// <summary>
    /// Trigger Copilot to try fixing the failed operation
    /// </summary>
    public async Task TryWithCopilotAsync()
    {
        if (OnTryWithCopilotRequested != null)
        {
            await OnTryWithCopilotRequested.Invoke(
                Title ?? "Unknown operation",
                Description ?? "",
                LogEntries.ToList()
            );
        }
    }

    internal void AddLogEntry(OperationLogEntry entry)
    {
        LogEntries.Add(entry);
        OnLogEntry?.Invoke(entry);
    }

    internal void SetStatus(string status)
    {
        CurrentStatus = status;
        OnStatusChanged?.Invoke(status);
    }

    internal void SetProgress(int? percent)
    {
        CurrentProgress = percent;
        OnProgressChanged?.Invoke(percent);
    }

    /// <summary>
    /// Internal context implementation
    /// </summary>
    private class OperationContext : IOperationContext
    {
        private readonly OperationModalService _service;
        
        public CancellationToken CancellationToken { get; }
        public bool IsCancellationRequested => CancellationToken.IsCancellationRequested;

        public OperationContext(OperationModalService service, CancellationToken cancellationToken)
        {
            _service = service;
            CancellationToken = cancellationToken;
        }

        public void Log(string message, OperationLogLevel level = OperationLogLevel.Info)
        {
            _service.AddLogEntry(new OperationLogEntry(DateTime.Now, message, level));
        }

        public void LogInfo(string message) => Log(message, OperationLogLevel.Info);
        public void LogSuccess(string message) => Log(message, OperationLogLevel.Success);
        public void LogWarning(string message) => Log(message, OperationLogLevel.Warning);
        public void LogError(string message) => Log(message, OperationLogLevel.Error);

        public void SetStatus(string status) => _service.SetStatus(status);
        public void SetProgress(int? percent) => _service.SetProgress(percent);
    }
}
