using System.Text;
using System.Text.Json;
using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests;
using MauiSherpa.Core.Services;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.Services;

public class SecretSyncCoordinatorTests
{
    [Fact]
    public async Task SetDesiredProvidersAsync_QueuesOnlyTypedReferences()
    {
        var registry = CreateRegistry();
        var tasks = new RecordingBackgroundTasks();
        var adapter = new FakeAdapter();
        var sut = CreateCoordinator(registry, tasks, adapter);
        var item = new SecretItemRef(SecretItemKind.ManagedSecret, "api-key", "API key");

        var taskId = await sut.SetDesiredProvidersAsync(item, ["remote", "local"]);

        taskId.Should().NotBeNull();
        var request = tasks.Tasks.Should().ContainSingle().Subject.Request;
        request.Type.Should().Be(SecretSyncCoordinator.TaskType);
        request.Parameters.Should().ContainKey("itemId").WhoseValue.Should().Be("api-key");
        request.Parameters.Values.Should().NotContain(value => value.Contains("secret-value", StringComparison.Ordinal));
        JsonSerializer.Deserialize<List<string>>(request.Parameters["desiredProviderIds"])
            .Should().Equal("local", "remote");
    }

    [Fact]
    public async Task ExecuteAsync_WhenLocalIsDesired_WritesLocalBeforeRemote()
    {
        var registry = CreateRegistry();
        var tasks = new RecordingBackgroundTasks();
        var adapter = new FakeAdapter
        {
            LocalPayload = CreatePayload("same-value")
        };
        var sut = CreateCoordinator(registry, tasks, adapter);
        var item = adapter.LocalPayload.Item;
        var request = CreateSyncRequest(item, ["remote", "local"]);

        var context = new RecordingTaskContext();
        await sut.ExecuteAsync(request, context);

        adapter.WriteOrder.Should().Equal("local", "remote");
        context.Steps.Select(step => step.Id).Should().Equal("sync:local", "sync:remote");
        context.Steps.Should().OnlyContain(step => step.State == BackgroundTaskStepState.Succeeded);
        context.StepTransitions.Take(4).Should().Equal(
            ("sync:local", BackgroundTaskStepState.Running),
            ("sync:local", BackgroundTaskStepState.Succeeded),
            ("sync:remote", BackgroundTaskStepState.Running),
            ("sync:remote", BackgroundTaskStepState.Succeeded));
        adapter.ProviderPayloads.Keys.Should().BeEquivalentTo("local", "remote");
        registry.Providers["local"].Values.Keys.Should()
            .ContainSingle(key => key.StartsWith(GetManifestPrefix(), StringComparison.Ordinal));
        registry.Providers["remote"].Values.Keys.Should()
            .ContainSingle(key => key.StartsWith(GetManifestPrefix(), StringComparison.Ordinal));
        registry.Providers["local"].Values.Should().ContainKey("sherpa-sync-catalog-v1");
        registry.Providers["remote"].Values.Should().ContainKey("sherpa-sync-catalog-v1");
        registry.Providers["remote"].MissingSecretReadCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WhenLocalWriteFails_DoesNotAttemptRemoteWrite()
    {
        var registry = CreateRegistry();
        var adapter = new FakeAdapter
        {
            LocalPayload = CreatePayload("same-value")
        };
        adapter.FailedWrites.Add("local");
        var sut = CreateCoordinator(registry, new RecordingBackgroundTasks(), adapter);
        var context = new RecordingTaskContext();
        var request = CreateSyncRequest(adapter.LocalPayload.Item, ["remote", "local"]);

        Func<Task> act = () => sut.ExecuteAsync(request, context);

        await act.Should().ThrowAsync<BackgroundTaskTransientException>();
        adapter.WriteOrder.Should().Equal("local");
        context.Steps.Single(step => step.Id == "sync:local").State
            .Should().Be(BackgroundTaskStepState.Failed);
        context.Steps.Single(step => step.Id == "sync:remote").State
            .Should().Be(BackgroundTaskStepState.Pending);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRemovingCopies_RemovesLocalBeforeRemote()
    {
        var registry = new FakeProviderRegistry(
            new CloudSecretsProviderConfig(
                "remote",
                "Remote",
                CloudSecretsProviderType.AzureKeyVault,
                new Dictionary<string, string>()),
            new CloudSecretsProviderConfig(
                "local",
                "Local",
                CloudSecretsProviderType.Local,
                new Dictionary<string, string>()));
        var adapter = new FakeAdapter
        {
            LocalPayload = CreatePayload("same-value")
        };
        var sut = CreateCoordinator(registry, new RecordingBackgroundTasks(), adapter);
        var item = adapter.LocalPayload.Item;
        await sut.ExecuteAsync(
            CreateSyncRequest(item, ["remote", "local"]),
            new RecordingTaskContext());
        adapter.DeleteOrder.Clear();

        var context = new RecordingTaskContext();
        await sut.ExecuteAsync(CreateSyncRequest(item, []), context);

        adapter.DeleteOrder.Should().Equal("local", "remote");
        context.Steps.Select(step => step.Id).Should().Equal("remove:local", "remove:remote");
    }

    [Fact]
    public async Task ExecuteAsync_PublishesEachProviderPlacementAsItChanges()
    {
        var registry = CreateRegistry();
        var adapter = new FakeAdapter
        {
            LocalPayload = CreatePayload("same-value")
        };
        var events = new List<SecretProviderPlacementChangedEvent>();
        var mediator = CreateMediator(events);
        var sut = CreateCoordinator(
            registry,
            new RecordingBackgroundTasks(),
            adapter,
            mediator);

        await sut.ExecuteAsync(
            CreateSyncRequest(adapter.LocalPayload.Item, ["remote", "local"]),
            new RecordingTaskContext());

        events.Select(@event => (@event.Placement.ProviderId, @event.Placement.Status))
            .Should().Equal(
                ("local", SecretPlacementStatus.Pending),
                ("local", SecretPlacementStatus.Synced),
                ("remote", SecretPlacementStatus.Pending),
                ("remote", SecretPlacementStatus.Synced));
    }

    [Fact]
    public async Task GetStateAsync_WithDivergentProviderPayloads_ReportsConflict()
    {
        var registry = CreateRegistry();
        var adapter = new FakeAdapter();
        adapter.ProviderPayloads["local"] = CreatePayload("local-value");
        adapter.ProviderPayloads["remote"] = CreatePayload("remote-value");
        var sut = CreateCoordinator(registry, new RecordingBackgroundTasks(), adapter);

        var state = await sut.GetStateAsync(CreatePayload("local-value").Item);

        state.HasConflict.Should().BeTrue();
        state.Providers.Where(provider => provider.Observed)
            .Should().OnlyContain(provider => provider.Status == SecretPlacementStatus.Conflict);
    }

    [Fact]
    public async Task GetStateAsync_AfterFailedWrite_KeepsRequestedProviderDesired()
    {
        var registry = CreateRegistry();
        var adapter = new FakeAdapter();
        adapter.ProviderPayloads["local"] = CreatePayload("same-value");
        var item = adapter.ProviderPayloads["local"].Item;
        var tasks = new RecordingBackgroundTasks();
        await tasks.EnqueueAsync(CreateSyncRequest(item, ["local", "remote"]));
        tasks.SetLatestState(BackgroundTaskState.Failed, "Remote provider unavailable");
        var sut = CreateCoordinator(registry, tasks, adapter);

        var state = await sut.GetStateAsync(item);

        state.Providers.Single(provider => provider.ProviderId == "remote").Desired.Should().BeTrue();
        state.Providers.Single(provider => provider.ProviderId == "remote").Status
            .Should().Be(SecretPlacementStatus.Failed);
    }

    [Fact]
    public async Task GetStateProgressivelyAsync_ReportsEachProviderAsItCompletes()
    {
        var registry = CreateRegistry();
        var adapter = new FakeAdapter();
        adapter.ProviderPayloads["local"] = CreatePayload("same-value");
        var progress = new RecordingProgress<ProviderPlacementState>();
        var sut = CreateCoordinator(registry, new RecordingBackgroundTasks(), adapter);

        var state = await sut.GetStateProgressivelyAsync(
            CreatePayload("same-value").Item,
            progress);

        progress.Values.Select(value => value.ProviderId)
            .Should().BeEquivalentTo("local", "remote");
        progress.Values.Should().OnlyContain(value =>
            value.Status != SecretPlacementStatus.Loading);
        state.Providers.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateDesiredProvidersAsync_QueuesOnlyChangedProviders()
    {
        var registry = CreateRegistry();
        var tasks = new RecordingBackgroundTasks();
        var sut = CreateCoordinator(registry, tasks, new FakeAdapter());
        var item = new SecretItemRef(SecretItemKind.ManagedSecret, "api-key", "API key");

        await sut.UpdateDesiredProvidersAsync(
            item,
            new Dictionary<string, bool> { ["remote"] = true });

        var request = tasks.Tasks.Should().ContainSingle().Subject.Request;
        request.Parameters.Should().NotContainKey("desiredProviderIds");
        JsonSerializer.Deserialize<Dictionary<string, bool>>(
                request.Parameters["providerSelectionChanges"])
            .Should().Contain("remote", true);
    }

    [Fact]
    public async Task GetStateAsync_ReusesProviderSnapshotsAcrossVisibleRows()
    {
        var registry = CreateRegistry();
        var adapter = new FakeAdapter();
        adapter.ProviderPayloads["local"] = CreatePayload("same-value");
        var sut = CreateCoordinator(registry, new RecordingBackgroundTasks(), adapter);
        var item = adapter.ProviderPayloads["local"].Item;

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => sut.GetStateProgressivelyAsync(item)));

        adapter.PresenceScanCount.Should().Be(2);
        registry.Providers.Values.Sum(provider => provider.GetSecretCallCount)
            .Should().Be(0);
        registry.Providers.Values.Sum(provider => provider.ListSecretsCallCount)
            .Should().Be(2);
    }

    private static SecretSyncCoordinator CreateCoordinator(
        FakeProviderRegistry registry,
        RecordingBackgroundTasks tasks,
        FakeAdapter adapter,
        Mock<IMediator>? mediator = null) =>
        new(
            registry,
            tasks,
            (mediator ?? CreateMediator()).Object,
            [adapter],
            new Mock<ILoggingService>().Object);

    private static Mock<IMediator> CreateMediator(
        List<SecretProviderPlacementChangedEvent>? events = null)
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(instance => instance.Publish(
                It.IsAny<SecretProviderPlacementChangedEvent>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>(),
                It.IsAny<Action<IMediatorContext>?>()))
            .Callback<SecretProviderPlacementChangedEvent, CancellationToken, bool, Action<IMediatorContext>?>(
                (@event, _, _, _) => events?.Add(@event))
            .ReturnsAsync(Mock.Of<IMediatorContext>());
        return mediator;
    }

    private static FakeProviderRegistry CreateRegistry() => new(
        new CloudSecretsProviderConfig(
            "local",
            "Local",
            CloudSecretsProviderType.Local,
            new Dictionary<string, string>()),
        new CloudSecretsProviderConfig(
            "remote",
            "Remote",
            CloudSecretsProviderType.AzureKeyVault,
            new Dictionary<string, string>()));

    private static SecretItemPayload CreatePayload(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return new SecretItemPayload(
            new SecretItemRef(SecretItemKind.ManagedSecret, "api-key", "API key"),
            0,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            [new SecretArtifact("sherpa-secrets/api-key", bytes)]);
    }

    private static BackgroundTaskRequest CreateSyncRequest(
        SecretItemRef item,
        IReadOnlyCollection<string> providers) =>
        new(
            SecretSyncCoordinator.TaskType,
            $"Sync {item.DisplayName}",
            "Sync item",
            new Dictionary<string, string>
            {
                ["kind"] = item.Kind.ToString(),
                ["itemId"] = item.Id,
                ["displayName"] = item.DisplayName,
                ["desiredProviderIds"] = JsonSerializer.Serialize(providers)
            },
            $"secret-sync:{item.Kind}:{item.Id}");

    private static string GetManifestPrefix() => "sherpa-sync-manifests/";

    private sealed class FakeProviderRegistry : ISecretsProviderRegistry
    {
        private readonly List<CloudSecretsProviderConfig> _configs;

        public FakeProviderRegistry(params CloudSecretsProviderConfig[] configs)
        {
            _configs = configs.ToList();
            Providers = configs.ToDictionary(
                config => config.Id,
                config => new InMemoryProvider(config.ProviderType, config.Name),
                StringComparer.Ordinal);
        }

        public Dictionary<string, InMemoryProvider> Providers { get; }
        public event Action? ProvidersChanged;

        public Task<IReadOnlyList<CloudSecretsProviderConfig>> GetProvidersAsync() =>
            Task.FromResult<IReadOnlyList<CloudSecretsProviderConfig>>(_configs);

        public Task<CloudSecretsProviderConfig?> GetProviderConfigAsync(string providerId) =>
            Task.FromResult(_configs.FirstOrDefault(provider => provider.Id == providerId));

        public Task<ICloudSecretsProvider?> GetProviderAsync(string providerId) =>
            Task.FromResult<ICloudSecretsProvider?>(
                Providers.TryGetValue(providerId, out var provider) ? provider : null);

        public Task SaveProviderAsync(CloudSecretsProviderConfig provider) =>
            throw new NotSupportedException();

        public Task DeleteProviderAsync(string providerId) =>
            throw new NotSupportedException();

        public Task<bool> TestProviderConnectionAsync(string providerId) =>
            Task.FromResult(Providers.ContainsKey(providerId));
    }

    private sealed class InMemoryProvider(
        CloudSecretsProviderType providerType,
        string displayName) : ICloudSecretsProvider
    {
        public Dictionary<string, byte[]> Values { get; } = new(StringComparer.Ordinal);
        public CloudSecretsProviderType ProviderType { get; } = providerType;
        public string DisplayName { get; } = displayName;
        public int GetSecretCallCount { get; private set; }
        public int ListSecretsCallCount { get; private set; }
        public int MissingSecretReadCount { get; private set; }

        public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> StoreSecretAsync(
            string key,
            byte[] value,
            Dictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            Values[key] = value.ToArray();
            return Task.FromResult(true);
        }

        public Task<byte[]?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
        {
            GetSecretCallCount++;
            if (!Values.TryGetValue(key, out var value))
            {
                MissingSecretReadCount++;
                return Task.FromResult<byte[]?>(null);
            }

            return Task.FromResult<byte[]?>(value.ToArray());
        }

        public Task<Dictionary<string, string>?> GetSecretMetadataAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Dictionary<string, string>?>([]);

        public Task<bool> SetSecretMetadataAsync(
            string key,
            Dictionary<string, string> metadata,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> DeleteSecretAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.Remove(key));

        public Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.ContainsKey(key));

        public Task<IReadOnlyList<string>> ListSecretsAsync(
            string? prefix = null,
            CancellationToken cancellationToken = default)
        {
            ListSecretsCallCount++;
            return Task.FromResult<IReadOnlyList<string>>(Values.Keys
                .Where(key => prefix is null || key.StartsWith(prefix, StringComparison.Ordinal))
                .ToList());
        }
    }

    private sealed class FakeAdapter : ISecretItemAdapter
    {
        public SecretItemKind Kind => SecretItemKind.ManagedSecret;
        public bool IsProviderOwned => true;
        public SecretItemPayload? LocalPayload { get; set; }
        public Dictionary<string, SecretItemPayload> ProviderPayloads { get; } = new(StringComparer.Ordinal);
        public List<string> WriteOrder { get; } = [];
        public List<string> DeleteOrder { get; } = [];
        public HashSet<string> FailedWrites { get; } = new(StringComparer.Ordinal);
        public int PresenceScanCount { get; private set; }

        public Task<IReadOnlyList<SecretItemRef>> ListLocalItemsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SecretItemRef>>(
                LocalPayload is null ? [] : [LocalPayload.Item]);

        public Task<IReadOnlyList<SecretItemRef>> ListProviderItemsAsync(
            string providerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SecretItemRef>>(
                ProviderPayloads.TryGetValue(providerId, out var payload) ? [payload.Item] : []);

        public Task<IReadOnlySet<string>> ListProviderItemIdsAsync(
            string providerId,
            CancellationToken cancellationToken = default)
        {
            PresenceScanCount++;
            return Task.FromResult<IReadOnlySet<string>>(
                ProviderPayloads.TryGetValue(providerId, out var payload)
                    ? new HashSet<string>([payload.Item.Id], StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal));
        }

        public IReadOnlySet<string> ExtractProviderItemIds(IReadOnlyList<string> providerKeys)
        {
            PresenceScanCount++;
            return ProviderPayloads.Values
                .Select(payload => payload.Item.Id)
                .ToHashSet(StringComparer.Ordinal);
        }

        public Task<SecretItemPayload?> ReadLocalAsync(
            SecretItemRef item,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LocalPayload);

        public Task<SecretItemPayload?> ReadProviderAsync(
            SecretItemRef item,
            string providerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                ProviderPayloads.TryGetValue(providerId, out var payload) ? payload : null);

        public Task<bool> WriteProviderAsync(
            SecretItemPayload payload,
            string providerId,
            CancellationToken cancellationToken = default)
        {
            WriteOrder.Add(providerId);
            if (FailedWrites.Contains(providerId))
                return Task.FromResult(false);
            ProviderPayloads[providerId] = payload;
            return Task.FromResult(true);
        }

        public Task<bool> DeleteProviderAsync(
            SecretItemRef item,
            string providerId,
            CancellationToken cancellationToken = default)
        {
            DeleteOrder.Add(providerId);
            return Task.FromResult(ProviderPayloads.Remove(providerId));
        }
    }

    private sealed class RecordingBackgroundTasks : IBackgroundTaskService
    {
        private readonly List<BackgroundTaskInfo> _tasks = [];

        public bool IsPersistent => false;
        public IReadOnlyList<BackgroundTaskInfo> Tasks => _tasks;
        public event Action? TasksChanged;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<string> EnqueueAsync(
            BackgroundTaskRequest request,
            CancellationToken cancellationToken = default)
        {
            var id = Guid.NewGuid().ToString("N");
            _tasks.Add(new BackgroundTaskInfo(
                id,
                request,
                BackgroundTaskState.Pending,
                null,
                "Queued",
                null,
                DateTime.UtcNow,
                null,
                null,
                [],
                0));
            TasksChanged?.Invoke();
            return Task.FromResult(id);
        }

        public void SetLatestState(BackgroundTaskState state, string? error)
        {
            var latest = _tasks[^1];
            _tasks[^1] = latest with { State = state, Error = error };
            TasksChanged?.Invoke();
        }

        public Task RetryAsync(string taskId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task CancelAsync(string taskId) => Task.CompletedTask;

        public Task DismissAsync(string taskId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ClearCompletedAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingTaskContext : IBackgroundTaskContext
    {
        public List<BackgroundTaskStepInfo> Steps { get; private set; } = [];
        public List<(string StepId, BackgroundTaskStepState State)> StepTransitions { get; } = [];
        public CancellationToken CancellationToken => CancellationToken.None;
        public void SetStatus(string status) { }
        public void SetProgress(int? progress) { }
        public void SetSteps(IReadOnlyList<BackgroundTaskStepInfo> steps) =>
            Steps = steps.ToList();

        public void UpdateStep(
            string stepId,
            BackgroundTaskStepState state,
            string? status = null,
            string? error = null)
        {
            var index = Steps.FindIndex(step => step.Id == stepId);
            index.Should().BeGreaterThanOrEqualTo(0);
            Steps[index] = Steps[index] with
            {
                State = state,
                Status = status ?? Steps[index].Status,
                Error = error
            };
            StepTransitions.Add((stepId, state));
        }

        public void Log(string message, OperationLogLevel level = OperationLogLevel.Info) { }
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
