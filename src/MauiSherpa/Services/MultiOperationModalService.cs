using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

/// <summary>
/// Service for managing multi-operation modal state and execution
/// </summary>
public class MultiOperationModalService : IMultiOperationModalService
{
    private TaskCompletionSource<MultiOperationResult>? _completionSource;
    private CancellationTokenSource? _cts;

    public event Action? OnModalShown;
    public event Action? OnModalClosed;
    public event Action<string, string>? OnShowRequested;
    public event Action<OperationItemStatus>? OnOperationStateChanged;
    public event Action<string, OperationLogEntry>? OnLogEntry;
    public event Action? OnStartRequested;
    public event Action? OnExecutionStarted;
    public event Action? OnExecutionCompleted;

    public string? Title { get; private set; }
    public string? Description { get; private set; }
    public bool IsVisible { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsConfirming { get; private set; }
    public List<OperationItemStatus> Operations { get; } = new();
    public int CurrentOperationIndex { get; private set; } = -1;

    private MultiOperationResult? _result;
    private List<OperationItem> _originalOperations = new();

    public async Task<MultiOperationResult> RunAsync(
        string title,
        string description,
        IEnumerable<OperationItem> operations)
    {
        if (IsVisible)
        {
            throw new InvalidOperationException("A multi-operation modal is already being shown");
        }

        // Reset state
        Title = title;
        Description = description;
        IsVisible = true;
        IsConfirming = true;
        IsRunning = false;
        CurrentOperationIndex = -1;
        _result = null;
        _cts = new CancellationTokenSource();
        _completionSource = new TaskCompletionSource<MultiOperationResult>();

        // Store original operations for execution
        _originalOperations = operations.ToList();

        // Build operation status list
        Operations.Clear();
        foreach (var op in _originalOperations)
        {
            Operations.Add(new OperationItemStatus
            {
                Id = op.Id,
                Name = op.Name,
                Description = op.Description,
                IsEnabled = op.IsEnabled,
                CanDisable = op.CanDisable,
                State = OperationItemState.Pending
            });
        }

        OnModalShown?.Invoke();
        OnShowRequested?.Invoke(title, description);

        // Wait for result (user closes modal)
        var result = await _completionSource.Task;

        IsVisible = false;
        Title = null;
        Description = null;
        OnModalClosed?.Invoke();

        return result;
    }

    /// <summary>
    /// Toggle an operation's enabled state (called from UI)
    /// </summary>
    public void ToggleOperation(string operationId, bool enabled)
    {
        var op = Operations.FirstOrDefault(o => o.Id == operationId);
        if (op != null && op.CanDisable && !IsRunning)
        {
            op.IsEnabled = enabled;
            OnOperationStateChanged?.Invoke(op);
        }
    }

    /// <summary>
    /// Start executing the enabled operations
    /// </summary>
    public async Task StartExecutionAsync()
    {
        if (IsRunning) return;

        IsConfirming = false;
        IsRunning = true;
        OnExecutionStarted?.Invoke();

        var startTime = DateTime.Now;
        var completed = 0;
        var failed = 0;
        var skipped = 0;

        var opLookup = _originalOperations.ToDictionary(o => o.Id);

        for (int i = 0; i < Operations.Count; i++)
        {
            if (_cts?.IsCancellationRequested == true)
            {
                // Mark remaining as skipped
                for (int j = i; j < Operations.Count; j++)
                {
                    Operations[j].State = OperationItemState.Skipped;
                    OnOperationStateChanged?.Invoke(Operations[j]);
                    skipped++;
                }
                break;
            }

            var status = Operations[i];
            
            if (!status.IsEnabled)
            {
                status.State = OperationItemState.Skipped;
                OnOperationStateChanged?.Invoke(status);
                skipped++;
                continue;
            }

            CurrentOperationIndex = i;
            status.State = OperationItemState.Running;
            OnOperationStateChanged?.Invoke(status);

            var opStartTime = DateTime.Now;
            
            try
            {
                if (opLookup.TryGetValue(status.Id, out var operation))
                {
                    var context = new OperationItemContext(this, status.Id, _cts!.Token);
                    var success = await operation.Execute(context);
                    
                    status.Duration = DateTime.Now - opStartTime;
                    
                    if (success)
                    {
                        status.State = OperationItemState.Completed;
                        completed++;
                    }
                    else
                    {
                        status.State = OperationItemState.Failed;
                        status.ErrorMessage ??= "Operation returned failure";
                        failed++;
                    }
                }
                else
                {
                    status.State = OperationItemState.Failed;
                    status.ErrorMessage = "Operation not found";
                    failed++;
                }
            }
            catch (OperationCanceledException)
            {
                status.State = OperationItemState.Skipped;
                status.Duration = DateTime.Now - opStartTime;
                skipped++;
            }
            catch (Exception ex)
            {
                status.State = OperationItemState.Failed;
                status.ErrorMessage = ex.Message;
                status.Duration = DateTime.Now - opStartTime;
                AddLogEntry(status.Id, new OperationLogEntry(DateTime.Now, $"Error: {ex.Message}", OperationLogLevel.Error));
                failed++;
            }

            OnOperationStateChanged?.Invoke(status);
        }

        CurrentOperationIndex = -1;
        IsRunning = false;

        _result = new MultiOperationResult(
            Operations.Count,
            completed,
            failed,
            skipped,
            DateTime.Now - startTime,
            Operations.ToList()
        );

        OnExecutionCompleted?.Invoke();
    }

    /// <summary>
    /// Request cancellation of running operations
    /// </summary>
    public void RequestCancellation()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// Close the modal (called from UI)
    /// </summary>
    public void Close()
    {
        if (IsRunning)
        {
            RequestCancellation();
            return;
        }

        var result = _result ?? new MultiOperationResult(
            Operations.Count,
            0,
            0,
            Operations.Count,
            TimeSpan.Zero,
            Operations.ToList()
        );

        _completionSource?.TrySetResult(result);
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Cancel before execution starts
    /// </summary>
    public void CancelBeforeExecution()
    {
        _result = new MultiOperationResult(
            Operations.Count,
            0,
            0,
            Operations.Count,
            TimeSpan.Zero,
            Operations.ToList()
        );
        _completionSource?.TrySetResult(_result);
    }

    internal void AddLogEntry(string operationId, OperationLogEntry entry)
    {
        var op = Operations.FirstOrDefault(o => o.Id == operationId);
        if (op != null)
        {
            op.Log.Add(entry);
            OnLogEntry?.Invoke(operationId, entry);
        }
    }

    /// <summary>
    /// Context for individual operation execution
    /// </summary>
    private class OperationItemContext : IOperationContext
    {
        private readonly MultiOperationModalService _service;
        private readonly string _operationId;

        public CancellationToken CancellationToken { get; }
        public bool IsCancellationRequested => CancellationToken.IsCancellationRequested;

        public OperationItemContext(MultiOperationModalService service, string operationId, CancellationToken cancellationToken)
        {
            _service = service;
            _operationId = operationId;
            CancellationToken = cancellationToken;
        }

        public void Log(string message, OperationLogLevel level = OperationLogLevel.Info)
        {
            _service.AddLogEntry(_operationId, new OperationLogEntry(DateTime.Now, message, level));
        }

        public void LogInfo(string message) => Log(message, OperationLogLevel.Info);
        public void LogSuccess(string message) => Log(message, OperationLogLevel.Success);
        public void LogWarning(string message) => Log(message, OperationLogLevel.Warning);
        public void LogError(string message) => Log(message, OperationLogLevel.Error);

        // These don't apply to multi-operation context (progress is shown per-operation)
        public void SetStatus(string status) => Log(status, OperationLogLevel.Info);
        public void SetProgress(int? percent) { }
    }
}
