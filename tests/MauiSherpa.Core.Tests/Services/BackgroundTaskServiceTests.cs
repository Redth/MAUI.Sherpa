using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace MauiSherpa.Core.Tests.Services;

public class BackgroundTaskServiceTests
{
    [Fact]
    public async Task EnqueueAsync_ExecutesRegisteredHandler()
    {
        using var services = CreateServices(new CompletingHandler());
        var sut = services.GetRequiredService<IBackgroundTaskService>();

        var taskId = await sut.EnqueueAsync(CreateRequest());
        var completed = await WaitForTerminalStateAsync(sut, taskId);

        completed.State.Should().Be(BackgroundTaskState.Succeeded);
        completed.Progress.Should().Be(100);
        completed.Steps.Should().NotBeNull();
        completed.Steps!.Select(step => step.Id).Should().Equal("local", "remote");
        completed.Steps.Should().OnlyContain(step =>
            step.State == BackgroundTaskStepState.Succeeded);
        completed.Log.Should().Contain(entry => entry.Level == OperationLogLevel.Success);
        sut.IsPersistent.Should().BeFalse();
    }

    [Fact]
    public async Task EnqueueAsync_WhenHandlerFails_PreservesDesiredTaskForRetry()
    {
        using var services = CreateServices(new FailingHandler());
        var sut = services.GetRequiredService<IBackgroundTaskService>();

        var taskId = await sut.EnqueueAsync(CreateRequest());
        var failed = await WaitForTerminalStateAsync(sut, taskId);

        failed.State.Should().Be(BackgroundTaskState.Failed);
        failed.Error.Should().Be("Provider authorization is required.");

        await sut.RetryAsync(taskId);
        var retried = await WaitForTerminalStateAsync(sut, taskId, minimumAttempt: 2);
        retried.State.Should().Be(BackgroundTaskState.Failed);
        retried.Attempt.Should().Be(2);
    }

    [Fact]
    public async Task InitializeAsync_WithLocalVault_RestoresPersistedHistory()
    {
        var vault = new InMemoryVaultStore();
        using (var services = CreateServices(
            new CompletingHandler(),
            vault,
            new AvailableVaultAccessService()))
        {
            var first = services.GetRequiredService<IBackgroundTaskService>();
            var taskId = await first.EnqueueAsync(CreateRequest());
            await WaitForTerminalStateAsync(first, taskId);
            first.IsPersistent.Should().BeTrue();
        }

        using var restoredServices = CreateServices(
            new CompletingHandler(),
            vault,
            new AvailableVaultAccessService());
        var restored = restoredServices.GetRequiredService<IBackgroundTaskService>();
        await restored.InitializeAsync();

        restored.IsPersistent.Should().BeTrue();
        restored.Tasks.Should().ContainSingle(task =>
            task.Request.Type == "test" &&
            task.State == BackgroundTaskState.Succeeded);
    }

    [Fact]
    public async Task UnlockingVault_AfterMemoryOnlyStart_MergesPersistedHistory()
    {
        var vault = new InMemoryVaultStore();
        using (var firstServices = CreateServices(
            new FailingHandler(),
            vault,
            new AvailableVaultAccessService()))
        {
            var first = firstServices.GetRequiredService<IBackgroundTaskService>();
            var taskId = await first.EnqueueAsync(CreateRequest());
            await WaitForTerminalStateAsync(first, taskId);
        }

        var access = new MutableVaultAccessService(
            new LocalVaultAccessState(LocalVaultAccessProblem.AccessDenied));
        using var secondServices = CreateServices(new CompletingHandler(), vault, access);
        var second = secondServices.GetRequiredService<IBackgroundTaskService>();
        await second.InitializeAsync();
        second.IsPersistent.Should().BeFalse();
        second.Tasks.Should().BeEmpty();

        access.SetState(LocalVaultAccessState.Available);

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout && second.Tasks.Count == 0)
            await Task.Delay(20);

        second.IsPersistent.Should().BeTrue();
        second.Tasks.Should().ContainSingle(task =>
            task.State == BackgroundTaskState.Failed &&
            task.Error == "Provider authorization is required.");
    }

    [Fact]
    public async Task CancelAsync_ForPendingTask_PreventsExecution()
    {
        var handler = new ControlledHandler();
        using var services = CreateServices(handler);
        var sut = services.GetRequiredService<IBackgroundTaskService>();
        var firstId = await sut.EnqueueAsync(CreateRequest() with { CoalesceKey = "test:first" });

        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondId = await sut.EnqueueAsync(CreateRequest() with { CoalesceKey = "test:second" });
        await sut.CancelAsync(secondId);
        handler.Release.TrySetResult();

        await WaitForTerminalStateAsync(sut, firstId);
        var cancelled = await WaitForTerminalStateAsync(sut, secondId);

        cancelled.State.Should().Be(BackgroundTaskState.Cancelled);
        handler.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task RetryAsync_WhenNewerDecisionExists_RejectsStaleTask()
    {
        var handler = new SwitchableHandler { ShouldFail = true };
        using var services = CreateServices(handler);
        var sut = services.GetRequiredService<IBackgroundTaskService>();
        var staleTaskId = await sut.EnqueueAsync(CreateRequest());
        await WaitForTerminalStateAsync(sut, staleTaskId);

        handler.ShouldFail = false;
        var currentTaskId = await sut.EnqueueAsync(CreateRequest());
        await WaitForTerminalStateAsync(sut, currentTaskId);

        Func<Task> act = () => sut.RetryAsync(staleTaskId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*superseded*");
    }

    [Fact]
    public async Task ClearCompletedAsync_RemovesSuccessAndRetainsFailures()
    {
        var handler = new SwitchableHandler();
        using var services = CreateServices(handler);
        var sut = services.GetRequiredService<IBackgroundTaskService>();
        var completedId = await sut.EnqueueAsync(
            CreateRequest() with { CoalesceKey = "test:completed" });
        await WaitForTerminalStateAsync(sut, completedId);
        handler.ShouldFail = true;
        var failedId = await sut.EnqueueAsync(
            CreateRequest() with { CoalesceKey = "test:failed" });
        await WaitForTerminalStateAsync(sut, failedId);

        await sut.ClearCompletedAsync();

        sut.Tasks.Should().ContainSingle(task =>
            task.Id == failedId && task.State == BackgroundTaskState.Failed);
    }

    [Fact]
    public async Task ClearCompletedAsync_PersistsClearedHistory()
    {
        var vault = new InMemoryVaultStore();
        using (var services = CreateServices(
            new CompletingHandler(),
            vault,
            new AvailableVaultAccessService()))
        {
            var sut = services.GetRequiredService<IBackgroundTaskService>();
            var taskId = await sut.EnqueueAsync(CreateRequest());
            await WaitForTerminalStateAsync(sut, taskId);

            await sut.ClearCompletedAsync();

            sut.Tasks.Should().BeEmpty();
        }

        using var restoredServices = CreateServices(
            new CompletingHandler(),
            vault,
            new AvailableVaultAccessService());
        var restored = restoredServices.GetRequiredService<IBackgroundTaskService>();
        await restored.InitializeAsync();

        restored.Tasks.Should().BeEmpty();
    }

    private static ServiceProvider CreateServices(
        IBackgroundTaskHandler handler,
        ILocalVaultStore? vaultStore = null,
        ILocalVaultAccessService? vaultAccess = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<ILoggingService>().Object);
        services.AddSingleton<IBackgroundTaskHandler>(handler);
        if (vaultStore is not null)
            services.AddSingleton(vaultStore);
        if (vaultAccess is not null)
            services.AddSingleton(vaultAccess);
        services.AddSingleton<BackgroundTaskService>();
        services.AddSingleton<IBackgroundTaskService>(provider =>
            provider.GetRequiredService<BackgroundTaskService>());
        return services.BuildServiceProvider();
    }

    private static BackgroundTaskRequest CreateRequest() => new(
        "test",
        "Test task",
        "Exercise the background queue.",
        new Dictionary<string, string> { ["desiredProviderIds"] = "[\"local\"]" },
        CoalesceKey: "test:item");

    private static async Task<BackgroundTaskInfo> WaitForTerminalStateAsync(
        IBackgroundTaskService service,
        string taskId,
        int minimumAttempt = 1)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            var task = service.Tasks.Single(info => info.Id == taskId);
            if ((task.Attempt >= minimumAttempt || task.State == BackgroundTaskState.Cancelled) &&
                task.State is BackgroundTaskState.Succeeded or
                    BackgroundTaskState.Failed or
                    BackgroundTaskState.Cancelled)
            {
                return task;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Background task did not finish.");
    }

    private sealed class CompletingHandler : IBackgroundTaskHandler
    {
        public string Type => "test";

        public Task ExecuteAsync(BackgroundTaskRequest request, IBackgroundTaskContext context)
        {
            context.SetStatus("Working");
            context.SetProgress(50);
            context.SetSteps(
            [
                new("local", "Local Vault", BackgroundTaskStepState.Pending, "Waiting"),
                new("remote", "Remote", BackgroundTaskStepState.Pending, "Waiting")
            ]);
            context.UpdateStep("local", BackgroundTaskStepState.Running, "Syncing");
            context.UpdateStep("local", BackgroundTaskStepState.Succeeded, "Synced");
            context.UpdateStep("remote", BackgroundTaskStepState.Running, "Syncing");
            context.UpdateStep("remote", BackgroundTaskStepState.Succeeded, "Synced");
            context.Log("Finished the provider step.", OperationLogLevel.Success);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingHandler : IBackgroundTaskHandler
    {
        public string Type => "test";

        public Task ExecuteAsync(BackgroundTaskRequest request, IBackgroundTaskContext context) =>
            throw new InvalidOperationException("Provider authorization is required.");
    }

    private sealed class ControlledHandler : IBackgroundTaskHandler
    {
        public string Type => "test";
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ExecutionCount { get; private set; }

        public async Task ExecuteAsync(
            BackgroundTaskRequest request,
            IBackgroundTaskContext context)
        {
            ExecutionCount++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(context.CancellationToken);
        }
    }

    private sealed class SwitchableHandler : IBackgroundTaskHandler
    {
        public string Type => "test";
        public bool ShouldFail { get; set; }

        public Task ExecuteAsync(
            BackgroundTaskRequest request,
            IBackgroundTaskContext context)
        {
            if (ShouldFail)
                throw new InvalidOperationException("Expected test failure.");
            return Task.CompletedTask;
        }
    }

    private sealed class AvailableVaultAccessService : ILocalVaultAccessService
    {
        public LocalVaultAccessState GetState() => LocalVaultAccessState.Available;

        public Task<LocalVaultAccessState> RequestAccessAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LocalVaultAccessState.Available);

        public event Action? StateChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class MutableVaultAccessService(
        LocalVaultAccessState state) : ILocalVaultAccessService
    {
        private LocalVaultAccessState _state = state;

        public LocalVaultAccessState GetState() => _state;

        public Task<LocalVaultAccessState> RequestAccessAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_state);

        public event Action? StateChanged;

        public void SetState(LocalVaultAccessState state)
        {
            _state = state;
            StateChanged?.Invoke();
        }
    }

    private sealed class InMemoryVaultStore : ILocalVaultStore
    {
        private LocalVaultItem? _item;

        public string DatabasePath => "memory";

        public Task<LocalVaultItem> PutAsync(
            string scope,
            string path,
            string key,
            byte[] value,
            string contentType,
            Dictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            _item = new LocalVaultItem
            {
                Id = LocalVaultItem.CreateId(scope, path, key),
                Scope = scope,
                Path = path,
                Key = key,
                Value = value.ToArray(),
                ContentType = contentType,
                Metadata = metadata ?? [],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            return Task.FromResult(_item);
        }

        public Task<LocalVaultItem?> GetAsync(
            string scope,
            string path,
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _item is not null &&
                _item.Scope == scope &&
                _item.Path == path &&
                _item.Key == key
                    ? _item
                    : null);

        public Task<bool> RemoveAsync(
            string scope,
            string path,
            string key,
            CancellationToken cancellationToken = default)
        {
            var existed = _item is not null;
            _item = null;
            return Task.FromResult(existed);
        }

        public Task<bool> ExistsAsync(
            string scope,
            string path,
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_item is not null);

        public Task<IReadOnlyList<LocalVaultItem>> ListAsync(
            string scope,
            string? path = null,
            string? keyPrefix = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalVaultItem>>(
                _item is null ? [] : [_item]);
    }
}
