using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MauiSherpa.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace MauiSherpa.Core.Services;

public sealed class BackgroundTaskService : IBackgroundTaskService
{
    private const string VaultPath = "/tasks";
    private const string VaultKey = "background-task-queue";
    private const int MaxHistory = 100;
    private const int MaxAttempts = 3;

    private readonly ILocalVaultStore? _vaultStore;
    private readonly ILocalVaultAccessService? _vaultAccess;
    private readonly ILoggingService _logger;
    private readonly IServiceProvider _serviceProvider;
    private Dictionary<string, IBackgroundTaskHandler> _handlers = new(StringComparer.Ordinal);
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly List<StoredBackgroundTask> _tasks = [];
    private readonly Dictionary<string, CancellationTokenSource> _runningTasks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private bool _initialized;
    private Task? _worker;

    public BackgroundTaskService(
        IServiceProvider serviceProvider,
        ILoggingService logger,
        ILocalVaultStore? vaultStore = null,
        ILocalVaultAccessService? vaultAccess = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _vaultStore = vaultStore;
        _vaultAccess = vaultAccess;
        if (_vaultAccess is not null)
            _vaultAccess.StateChanged += OnVaultAccessChanged;
    }

    public bool IsPersistent { get; private set; }

    public IReadOnlyList<BackgroundTaskInfo> Tasks
    {
        get
        {
            lock (_tasks)
                return _tasks
                    .OrderByDescending(task => task.CreatedAtUtc)
                    .Select(task => task.ToInfo())
                    .ToList();
        }
    }

    public event Action? TasksChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            var canPersist = _vaultStore is not null &&
                _vaultAccess?.GetState().RequiresUserAction != true;
            _handlers = _serviceProvider
                .GetServices<IBackgroundTaskHandler>()
                .ToDictionary(handler => handler.Type, StringComparer.Ordinal);

            if (canPersist)
            {
                var loadedTaskIds = await LoadAsync(mergeWithExisting: false, cancellationToken);
                IsPersistent = loadedTaskIds is not null;
            }

            _initialized = true;
            _worker = Task.Run(ProcessQueueAsync);

            List<string> pendingIds;
            lock (_tasks)
            {
                pendingIds = _tasks
                    .Where(task => task.State == BackgroundTaskState.Pending)
                    .Select(task => task.Id)
                    .ToList();
            }

            foreach (var taskId in pendingIds)
                await _queue.Writer.WriteAsync(taskId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        NotifyChanged();
    }

    public async Task<string> EnqueueAsync(
        BackgroundTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        StoredBackgroundTask task;
        lock (_tasks)
        {
            var existingTask = !string.IsNullOrWhiteSpace(request.CoalesceKey)
                ? _tasks.FirstOrDefault(existing =>
                    existing.State == BackgroundTaskState.Pending &&
                    string.Equals(existing.Request.CoalesceKey, request.CoalesceKey, StringComparison.Ordinal))
                : null;

            if (existingTask is not null)
            {
                task = existingTask;
                task.Request = request;
                task.Status = "Updated with the latest requested state";
                task.Error = null;
            }
            else
            {
                task = new StoredBackgroundTask
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Request = request,
                    State = BackgroundTaskState.Pending,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _tasks.Add(task);
            }
        }

        await PersistAsync(cancellationToken);
        NotifyChanged();
        await _queue.Writer.WriteAsync(task.Id, cancellationToken);
        return task.Id;
    }

    public async Task RetryAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var shouldQueue = false;

        lock (_tasks)
        {
            var task = FindTask(taskId);
            if (task is null || task.State is BackgroundTaskState.Running or BackgroundTaskState.Pending)
                return;
            var taskIndex = _tasks.IndexOf(task);
            if (!string.IsNullOrWhiteSpace(task.Request.CoalesceKey) &&
                _tasks.Skip(taskIndex + 1).Any(newer =>
                    string.Equals(
                        newer.Request.CoalesceKey,
                        task.Request.CoalesceKey,
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "This task was superseded by a newer sync decision and cannot be retried.");
            }

            task.State = BackgroundTaskState.Pending;
            task.Progress = null;
            task.Status = "Queued for retry";
            task.Error = null;
            task.StartedAtUtc = null;
            task.CompletedAtUtc = null;
            task.Steps.Clear();
            shouldQueue = true;
        }

        if (!shouldQueue)
            return;

        await PersistAsync(cancellationToken);
        NotifyChanged();
        await _queue.Writer.WriteAsync(taskId, cancellationToken);
    }

    public async Task CancelAsync(string taskId)
    {
        CancellationTokenSource? runningCts;
        lock (_tasks)
        {
            var task = FindTask(taskId);
            if (task is null)
                return;

            _runningTasks.TryGetValue(taskId, out runningCts);
            if (task.State == BackgroundTaskState.Pending)
            {
                task.State = BackgroundTaskState.Cancelled;
                task.Status = "Cancelled";
                task.CompletedAtUtc = DateTime.UtcNow;
            }
        }

        runningCts?.Cancel();
        await PersistAsync(CancellationToken.None);
        NotifyChanged();
    }

    public async Task DismissAsync(string taskId, CancellationToken cancellationToken = default)
    {
        lock (_tasks)
        {
            var task = FindTask(taskId);
            if (task is null || task.State is BackgroundTaskState.Pending or BackgroundTaskState.Running)
                return;

            _tasks.Remove(task);
        }

        await PersistAsync(cancellationToken);
        NotifyChanged();
    }

    public async Task ClearCompletedAsync(CancellationToken cancellationToken = default)
    {
        var removed = false;
        lock (_tasks)
        {
            removed = _tasks.RemoveAll(task =>
                task.State is BackgroundTaskState.Succeeded or BackgroundTaskState.Cancelled) > 0;
        }

        if (!removed)
            return;

        await PersistAsync(cancellationToken);
        NotifyChanged();
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var taskId in _queue.Reader.ReadAllAsync())
        {
            StoredBackgroundTask? task;
            var cts = new CancellationTokenSource();
            lock (_tasks)
            {
                task = FindTask(taskId);
                if (task is null || task.State != BackgroundTaskState.Pending)
                {
                    cts.Dispose();
                    continue;
                }

                _runningTasks[task.Id] = cts;
                task.State = BackgroundTaskState.Running;
                task.Attempt++;
                task.StartedAtUtc = DateTime.UtcNow;
                task.CompletedAtUtc = null;
                task.Status = "Starting";
                task.Error = null;
                task.Progress = null;
                task.Steps.Clear();
                task.Log.Add(new OperationLogEntry(DateTime.UtcNow, "Task started"));
            }

            try
            {
                await PersistAndNotifyAsync();
                if (!_handlers.TryGetValue(task.Request.Type, out var handler))
                {
                    await CompleteWithErrorAsync(task, $"No handler is registered for task type '{task.Request.Type}'.");
                    continue;
                }

                await RunTaskAsync(task, handler, cts.Token);
            }
            finally
            {
                lock (_tasks)
                    _runningTasks.Remove(task.Id);
                cts.Dispose();
            }
        }
    }

    private async Task RunTaskAsync(
        StoredBackgroundTask task,
        IBackgroundTaskHandler handler,
        CancellationToken cancellationToken)
    {
        var context = new BackgroundTaskContext(this, task, cancellationToken);
        try
        {
            await handler.ExecuteAsync(task.Request, context);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_tasks)
            {
                task.State = BackgroundTaskState.Succeeded;
                task.Progress = 100;
                task.Status = "Completed";
                task.CompletedAtUtc = DateTime.UtcNow;
                task.Log.Add(new OperationLogEntry(DateTime.UtcNow, "Task completed", OperationLogLevel.Success));
            }
        }
        catch (OperationCanceledException)
        {
            lock (_tasks)
            {
                task.State = BackgroundTaskState.Cancelled;
                task.Status = "Cancelled";
                task.CompletedAtUtc = DateTime.UtcNow;
                task.Log.Add(new OperationLogEntry(DateTime.UtcNow, "Task cancelled", OperationLogLevel.Warning));
            }
        }
        catch (BackgroundTaskTransientException ex) when (task.Attempt < MaxAttempts)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                lock (_tasks)
                {
                    task.State = BackgroundTaskState.Cancelled;
                    task.Status = "Cancelled";
                    task.CompletedAtUtc = DateTime.UtcNow;
                    task.Log.Add(new OperationLogEntry(
                        DateTime.UtcNow,
                        "Task cancelled while handling a transient failure.",
                        OperationLogLevel.Warning));
                }
                await PersistAndNotifyAsync();
                return;
            }

            lock (_tasks)
            {
                task.State = BackgroundTaskState.Pending;
                task.Status = $"Retrying after transient failure ({task.Attempt}/{MaxAttempts})";
                task.Error = SanitizeError(ex.Message);
                task.Log.Add(new OperationLogEntry(DateTime.UtcNow, task.Error, OperationLogLevel.Warning));
            }
            await PersistAndNotifyAsync();
            _ = RequeueAfterDelayAsync(
                task.Id,
                TimeSpan.FromSeconds(Math.Pow(2, task.Attempt - 1)));
            return;
        }
        catch (Exception ex)
        {
            lock (_tasks)
            {
                task.State = BackgroundTaskState.Failed;
                task.Status = "Needs attention";
                task.Error = SanitizeError(ex.Message);
                task.CompletedAtUtc = DateTime.UtcNow;
                task.Log.Add(new OperationLogEntry(DateTime.UtcNow, task.Error, OperationLogLevel.Error));
            }
            _logger.LogError($"Background task '{task.Request.Type}' failed: {ex.Message}", ex);
        }

        TrimHistory();
        await PersistAndNotifyAsync();
    }

    private async Task CompleteWithErrorAsync(StoredBackgroundTask task, string error)
    {
        lock (_tasks)
        {
            task.State = BackgroundTaskState.Failed;
            task.Status = "Needs attention";
            task.Error = error;
            task.CompletedAtUtc = DateTime.UtcNow;
            task.Log.Add(new OperationLogEntry(DateTime.UtcNow, error, OperationLogLevel.Error));
        }
        await PersistAndNotifyAsync();
    }

    private async Task<IReadOnlyList<string>?> LoadAsync(
        bool mergeWithExisting,
        CancellationToken cancellationToken)
    {
        try
        {
            var item = await _vaultStore!.GetAsync(
                LocalVaultScopes.BackgroundTask,
                VaultPath,
                VaultKey,
                cancellationToken);
            if (item is null)
                return Array.Empty<string>();

            var stored = JsonSerializer.Deserialize<List<StoredBackgroundTask>>(item.Value, _jsonOptions) ?? [];
            foreach (var task in stored)
            {
                if (task.State == BackgroundTaskState.Running)
                {
                    task.State = BackgroundTaskState.Pending;
                    task.Status = "Resuming after app restart";
                    task.Log.Add(new OperationLogEntry(
                        DateTime.UtcNow,
                        "The app exited while this task was running; it was queued again.",
                        OperationLogLevel.Warning));
                }
            }

            var pendingTaskIds = new List<string>();
            lock (_tasks)
            {
                if (!mergeWithExisting)
                {
                    _tasks.Clear();
                    _tasks.AddRange(stored);
                    pendingTaskIds.AddRange(stored
                        .Where(task => task.State == BackgroundTaskState.Pending)
                        .Select(task => task.Id));
                }
                else
                {
                    var existingIds = _tasks
                        .Select(task => task.Id)
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (var task in stored)
                    {
                        if (existingIds.Contains(task.Id))
                            continue;
                        if (task.State == BackgroundTaskState.Pending &&
                            !string.IsNullOrWhiteSpace(task.Request.CoalesceKey) &&
                            _tasks.Any(existing =>
                                (existing.State is BackgroundTaskState.Pending or BackgroundTaskState.Running) &&
                                string.Equals(
                                    existing.Request.CoalesceKey,
                                    task.Request.CoalesceKey,
                                    StringComparison.Ordinal)))
                        {
                            continue;
                        }

                        _tasks.Add(task);
                        if (task.State == BackgroundTaskState.Pending)
                            pendingTaskIds.Add(task.Id);
                    }
                }
            }

            return pendingTaskIds;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Background task persistence is unavailable: {ex.Message}");
            return null;
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        if (!IsPersistent || _vaultStore is null)
            return;

        try
        {
            List<StoredBackgroundTask> snapshot;
            lock (_tasks)
                snapshot = _tasks.ToList();

            var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, _jsonOptions);
            await _vaultStore.PutAsync(
                LocalVaultScopes.BackgroundTask,
                VaultPath,
                VaultKey,
                json,
                LocalVaultContentTypes.Json,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            IsPersistent = false;
            _logger.LogWarning($"Failed to persist background tasks: {ex.Message}");
        }
    }

    private async Task PersistAndNotifyAsync()
    {
        await PersistAsync(CancellationToken.None);
        NotifyChanged();
    }

    private void TrimHistory()
    {
        lock (_tasks)
        {
            var completed = _tasks
                .Where(task => task.State is not BackgroundTaskState.Pending and not BackgroundTaskState.Running)
                .OrderByDescending(task => task.CompletedAtUtc ?? task.CreatedAtUtc)
                .Skip(MaxHistory)
                .ToList();

            foreach (var task in completed)
                _tasks.Remove(task);
        }
    }

    private StoredBackgroundTask? FindTask(string taskId) =>
        _tasks.FirstOrDefault(task => string.Equals(task.Id, taskId, StringComparison.Ordinal));

    private void NotifyChanged()
    {
        if (TasksChanged is null)
            return;

        foreach (Action subscriber in TasksChanged.GetInvocationList())
        {
            try
            {
                subscriber();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"A background task notification subscriber failed: {ex.Message}");
            }
        }
    }

    private void OnVaultAccessChanged()
    {
        if (!_initialized || IsPersistent || _vaultStore is null ||
            _vaultAccess?.GetState().RequiresUserAction == true)
        {
            return;
        }

        _ = EnablePersistenceAsync();
    }

    private async Task EnablePersistenceAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (IsPersistent || _vaultStore is null ||
                _vaultAccess?.GetState().RequiresUserAction == true)
            {
                return;
            }

            var loadedTaskIds = await LoadAsync(
                mergeWithExisting: true,
                CancellationToken.None);
            if (loadedTaskIds is null)
                return;

            IsPersistent = true;
            await PersistAsync(CancellationToken.None);
            foreach (var taskId in loadedTaskIds)
                await _queue.Writer.WriteAsync(taskId);
        }
        finally
        {
            _gate.Release();
        }

        NotifyChanged();
    }

    private async Task RequeueAfterDelayAsync(string taskId, TimeSpan delay)
    {
        await Task.Delay(delay);
        await _queue.Writer.WriteAsync(taskId);
    }

    private static string SanitizeError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "The task failed.";

        var sanitized = message.ReplaceLineEndings(" ").Trim();
        return sanitized.Length <= 500 ? sanitized : sanitized[..500];
    }

    private sealed class BackgroundTaskContext : IBackgroundTaskContext
    {
        private readonly BackgroundTaskService _service;
        private readonly StoredBackgroundTask _task;

        public BackgroundTaskContext(
            BackgroundTaskService service,
            StoredBackgroundTask task,
            CancellationToken cancellationToken)
        {
            _service = service;
            _task = task;
            CancellationToken = cancellationToken;
        }

        public CancellationToken CancellationToken { get; }

        public void SetStatus(string status)
        {
            lock (_service._tasks)
                _task.Status = status;
            _ = _service.PersistAndNotifyAsync();
        }

        public void SetProgress(int? progress)
        {
            lock (_service._tasks)
                _task.Progress = progress is null ? null : Math.Clamp(progress.Value, 0, 100);
            _ = _service.PersistAndNotifyAsync();
        }

        public void SetSteps(IReadOnlyList<BackgroundTaskStepInfo> steps)
        {
            lock (_service._tasks)
            {
                _task.Steps = steps
                    .Select(step => step with
                    {
                        Label = SanitizeError(step.Label),
                        Status = string.IsNullOrWhiteSpace(step.Status)
                            ? null
                            : SanitizeError(step.Status),
                        Error = string.IsNullOrWhiteSpace(step.Error)
                            ? null
                            : SanitizeError(step.Error)
                    })
                    .ToList();
            }
            _ = _service.PersistAndNotifyAsync();
        }

        public void UpdateStep(
            string stepId,
            BackgroundTaskStepState state,
            string? status = null,
            string? error = null)
        {
            lock (_service._tasks)
            {
                var index = _task.Steps.FindIndex(step =>
                    string.Equals(step.Id, stepId, StringComparison.Ordinal));
                if (index < 0)
                    throw new InvalidOperationException($"Background task step '{stepId}' is not registered.");

                var current = _task.Steps[index];
                _task.Steps[index] = current with
                {
                    State = state,
                    Status = string.IsNullOrWhiteSpace(status) ? current.Status : SanitizeError(status),
                    Error = string.IsNullOrWhiteSpace(error) ? null : SanitizeError(error)
                };
            }
            _ = _service.PersistAndNotifyAsync();
        }

        public void Log(string message, OperationLogLevel level = OperationLogLevel.Info)
        {
            lock (_service._tasks)
                _task.Log.Add(new OperationLogEntry(DateTime.UtcNow, SanitizeError(message), level));
            _ = _service.PersistAndNotifyAsync();
        }
    }

    private sealed class StoredBackgroundTask
    {
        public StoredBackgroundTask()
        {
        }

        public string Id { get; set; } = "";
        public BackgroundTaskRequest Request { get; set; } = new("", "", "", []);
        public BackgroundTaskState State { get; set; }
        public int? Progress { get; set; }
        public string? Status { get; set; }
        public string? Error { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public List<OperationLogEntry> Log { get; set; } = [];
        public List<BackgroundTaskStepInfo> Steps { get; set; } = [];
        public int Attempt { get; set; }

        public BackgroundTaskInfo ToInfo() => new(
            Id,
            Request,
            State,
            Progress,
            Status,
            Error,
            CreatedAtUtc,
            StartedAtUtc,
            CompletedAtUtc,
            Log.ToList(),
            Attempt,
            Steps.ToList());
    }
}

public sealed class BackgroundTaskTransientException : Exception
{
    public BackgroundTaskTransientException(string message)
        : base(message)
    {
    }

    public BackgroundTaskTransientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
