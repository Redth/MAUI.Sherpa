using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests;
using Shiny.Mediator;

namespace MauiSherpa.Core.Services;

public sealed class SecretSyncCoordinator : ISecretSyncCoordinator, IBackgroundTaskHandler
{
    public const string TaskType = "secret-sync";
    private const string ManifestPrefix = "sherpa-sync-manifests/";
    private const string CatalogKey = "sherpa-sync-catalog-v1";
    private const string PlatformSourceId = "$platform";
    private static readonly TimeSpan StatusSnapshotLifetime = TimeSpan.FromSeconds(15);

    private readonly ISecretsProviderRegistry _providerRegistry;
    private readonly IBackgroundTaskService _backgroundTasks;
    private readonly IMediator _mediator;
    private readonly ILoggingService _logger;
    private readonly Dictionary<SecretItemKind, ISecretItemAdapter> _adapters;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, TimedSnapshot<ProviderSyncCatalog?>> _catalogSnapshots =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TimedSnapshot<IReadOnlyList<string>>> _providerKeySnapshots =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TimedSnapshot<IReadOnlySet<string>>> _presenceSnapshots =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _catalogWriteLocks =
        new(StringComparer.Ordinal);

    public SecretSyncCoordinator(
        ISecretsProviderRegistry providerRegistry,
        IBackgroundTaskService backgroundTasks,
        IMediator mediator,
        IEnumerable<ISecretItemAdapter> adapters,
        ILoggingService logger)
    {
        _providerRegistry = providerRegistry;
        _backgroundTasks = backgroundTasks;
        _mediator = mediator;
        _logger = logger;
        _adapters = adapters.ToDictionary(adapter => adapter.Kind);
        _providerRegistry.ProvidersChanged += InvalidateAllStatusSnapshots;
    }

    public string Type => TaskType;

    public event Action<SecretItemRef>? ItemStateChanged;

    public async Task<IReadOnlyList<SecretItemSyncState>> ListItemsAsync(
        SecretItemKind kind,
        CancellationToken cancellationToken = default)
    {
        var adapter = GetAdapter(kind);
        var items = new Dictionary<string, SecretItemRef>(StringComparer.Ordinal);

        foreach (var item in await adapter.ListLocalItemsAsync(cancellationToken))
            items[item.Id] = item;

        var providers = (await _providerRegistry.GetProvidersAsync())
            .OrderBy(provider =>
                string.Equals(
                    provider.Id,
                    CloudSecretsService.DefaultLocalProviderId,
                    StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(provider => provider.Id, StringComparer.Ordinal)
            .ToList();
        foreach (var provider in providers)
        {
            try
            {
                foreach (var item in await adapter.ListProviderItemsAsync(provider.Id, cancellationToken))
                    items.TryAdd(item.Id, item);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    $"Could not enumerate {kind} items from provider '{provider.Name}': {ex.Message}");
            }
        }

        var states = new List<SecretItemSyncState>();
        foreach (var item in items.Values.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
            states.Add(await GetStateAsync(item, cancellationToken));

        return states;
    }

    public async Task<SecretItemSyncState> GetStateAsync(
        SecretItemRef item,
        CancellationToken cancellationToken = default)
    {
        var adapter = GetAdapter(item.Kind);
        var providers = await _providerRegistry.GetProvidersAsync();
        var observations = new Dictionary<string, ProviderObservation>(StringComparer.Ordinal);
        var manifests = new List<(string ProviderId, SecretSyncManifest Manifest)>();

        foreach (var provider in providers)
        {
            try
            {
                var payload = await adapter.ReadProviderAsync(item, provider.Id, cancellationToken);
                var manifest = await ReadManifestAsync(provider.Id, item, cancellationToken);
                observations[provider.Id] = new ProviderObservation(
                    payload is not null,
                    payload?.ContentHash,
                    manifest?.Revision ?? payload?.Revision,
                    manifest?.DesiredProviderIds,
                    Error: null);
                if (manifest is not null)
                    manifests.Add((provider.Id, manifest));
            }
            catch (Exception ex)
            {
                observations[provider.Id] = new ProviderObservation(
                    Observed: false,
                    ContentHash: null,
                    Revision: null,
                    DesiredProviderIds: null,
                    ex.Message);
            }
        }

        var task = GetLatestTask(item);
        var desiredProviderIds = task is not null
            ? TryParseDesiredProviders(task.Request.Parameters)
            : null;
        var selectionChanges = task is not null
            ? ParseProviderSelectionChanges(task.Request.Parameters)
            : new Dictionary<string, bool>(StringComparer.Ordinal);
        desiredProviderIds ??= SelectManifest(manifests)?
            .DesiredProviderIds
            .ToHashSet(StringComparer.Ordinal)
            ?? observations
                .Where(pair => pair.Value.Observed)
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal);
        ApplyProviderSelectionChanges(desiredProviderIds, selectionChanges);

        var hasConflict = observations.Values
            .Where(observation => observation.Observed &&
                !string.IsNullOrEmpty(observation.ContentHash))
            .Select(observation => observation.ContentHash)
            .Distinct(StringComparer.Ordinal)
            .Skip(1)
            .Any();
        var selectedManifest = SelectManifest(manifests);
        var selectedEntry = selectedManifest is null
            ? null
            : new ProviderSyncCatalogEntry(
                selectedManifest.Kind,
                selectedManifest.ItemId,
                selectedManifest.DisplayName,
                selectedManifest.Revision,
                selectedManifest.ContentHash,
                selectedManifest.DesiredProviderIds,
                selectedManifest.UpdatedAtUtc);
        var placements = providers.Select(provider =>
        {
            var observation = observations[provider.Id];
            var desired = desiredProviderIds.Contains(provider.Id);
            return new ProviderPlacementState(
                provider.Id,
                desired,
                observation.Observed,
                GetPlacementStatus(
                    desired,
                    observation.Observed,
                    observation,
                    selectedEntry,
                    hasConflict,
                    task),
                observation.Revision,
                observation.ContentHash,
                observation.Error);
        }).ToList();

        return new SecretItemSyncState(
            item,
            placements,
            hasConflict,
            task is { State: BackgroundTaskState.Pending or BackgroundTaskState.Running or BackgroundTaskState.Failed }
                ? task.Id
                : null);
    }

    public async Task<SecretItemSyncState> GetStateProgressivelyAsync(
        SecretItemRef item,
        IProgress<ProviderPlacementState>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var adapter = GetAdapter(item.Kind);
        var providers = await _providerRegistry.GetProvidersAsync();
        var observations = new Dictionary<string, ProviderObservation>(StringComparer.Ordinal);
        var task = GetLatestTask(item);
        var requestedProviderIds = task is not null
            ? TryParseDesiredProviders(task.Request.Parameters)
            : null;
        var selectionChanges = task is not null
            ? ParseProviderSelectionChanges(task.Request.Parameters)
            : new Dictionary<string, bool>(StringComparer.Ordinal);
        var pending = providers
            .Select(provider => ObserveProviderAsync(adapter, item, provider, cancellationToken))
            .ToList();

        while (pending.Count > 0)
        {
            var completedTask = await Task.WhenAny(pending);
            pending.Remove(completedTask);
            var completed = await completedTask;
            observations[completed.Provider.Id] = completed.Observation;

            progress?.Report(CreateProgressPlacement(
                completed.Provider,
                completed.Observation,
                requestedProviderIds,
                selectionChanges,
                task));
        }

        var desiredProviderIds = requestedProviderIds is not null
            ? requestedProviderIds
            : SelectCatalogEntry(observations)?.DesiredProviderIds.ToHashSet(StringComparer.Ordinal)
                ?? observations
                    .Where(pair => pair.Value.Observed)
                    .Select(pair => pair.Key)
                    .ToHashSet(StringComparer.Ordinal);
        ApplyProviderSelectionChanges(desiredProviderIds, selectionChanges);

        var observedHashes = observations.Values
            .Where(observation => observation.Observed &&
                !string.IsNullOrEmpty(observation.ContentHash))
            .Select(observation => observation.ContentHash!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var hasConflict = observedHashes.Count > 1;
        var selectedEntry = SelectCatalogEntry(observations);

        var placementStates = providers.Select(provider =>
        {
            var observation = observations[provider.Id];
            var desired = desiredProviderIds.Contains(provider.Id);
            var observed = observation.Observed;
            var status = GetPlacementStatus(
                desired,
                observed,
                observation,
                selectedEntry,
                hasConflict,
                task);

            return new ProviderPlacementState(
                provider.Id,
                desired,
                observed,
                status,
                observation.Revision,
                observation.ContentHash,
                observation.Error ?? (status == SecretPlacementStatus.Failed ? task?.Error : null));
        }).ToList();
        foreach (var missingProviderId in desiredProviderIds
            .Where(providerId => !observations.ContainsKey(providerId))
            .Order(StringComparer.Ordinal))
        {
            placementStates.Add(new ProviderPlacementState(
                missingProviderId,
                Desired: true,
                Observed: false,
                SecretPlacementStatus.Unavailable,
                Error: "This provider is no longer configured."));
        }

        return new SecretItemSyncState(
            item,
            placementStates,
            hasConflict,
            task is { State: BackgroundTaskState.Pending or BackgroundTaskState.Running or BackgroundTaskState.Failed }
                ? task.Id
                : null);
    }

    private async Task<CompletedProviderObservation> ObserveProviderAsync(
        ISecretItemAdapter adapter,
        SecretItemRef item,
        CloudSecretsProviderConfig provider,
        CancellationToken cancellationToken)
    {
        try
        {
            var catalogTask = GetProviderCatalogSnapshotAsync(
                provider.Id,
                cancellationToken);
            var presenceTask = GetProviderPresenceSnapshotAsync(
                adapter,
                provider.Id,
                cancellationToken);
            await Task.WhenAll(catalogTask, presenceTask);
            var catalog = await catalogTask;
            var presentItemIds = await presenceTask;
            var observed = presentItemIds.Contains(item.Id) ||
                presentItemIds.Any(candidate =>
                    SecretItemAdapterHelper.StorageKeysEqual(candidate, item.Id));
            ProviderSyncCatalogEntry? entry = null;
            catalog?.Entries.TryGetValue(GetCatalogEntryKey(item), out entry);
            return new CompletedProviderObservation(
                provider,
                new ProviderObservation(
                    observed,
                    observed ? entry?.ContentHash : null,
                    observed ? entry?.Revision : null,
                    entry?.DesiredProviderIds,
                    Error: null));
        }
        catch (Exception ex)
        {
            return new CompletedProviderObservation(
                provider,
                new ProviderObservation(
                    Observed: false,
                    ContentHash: null,
                    Revision: null,
                    DesiredProviderIds: null,
                    ex.Message));
        }
    }

    private static ProviderPlacementState CreateProgressPlacement(
        CloudSecretsProviderConfig provider,
        ProviderObservation observation,
        HashSet<string>? requestedProviderIds,
        IReadOnlyDictionary<string, bool> selectionChanges,
        BackgroundTaskInfo? task)
    {
        var desired = requestedProviderIds?.Contains(provider.Id)
            ?? observation.DesiredProviderIds?.Contains(provider.Id)
            ?? observation.Observed;
        if (selectionChanges.TryGetValue(provider.Id, out var changedDesired))
            desired = changedDesired;
        var observed = observation.Observed;
        var status = observation.Error is not null
            ? SecretPlacementStatus.Unavailable
            : (task?.State is BackgroundTaskState.Pending or BackgroundTaskState.Running) &&
                desired != observed
                ? SecretPlacementStatus.Pending
                : desired == observed
                    ? observed
                        ? SecretPlacementStatus.Synced
                        : SecretPlacementStatus.NotStored
                    : SecretPlacementStatus.Failed;

        return new ProviderPlacementState(
            provider.Id,
            desired,
            observed,
            status,
            observation.Revision,
            observation.ContentHash,
            observation.Error ?? (status == SecretPlacementStatus.Failed ? task?.Error : null));
    }

    public async Task<string?> SetDesiredProvidersAsync(
        SecretItemRef item,
        IReadOnlyCollection<string> providerIds,
        CancellationToken cancellationToken = default)
    {
        var configured = (await _providerRegistry.GetProvidersAsync())
            .Select(provider => provider.Id)
            .ToHashSet(StringComparer.Ordinal);
        var desired = providerIds
            .Where(configured.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var request = CreateRequest(item, desired, sourceProviderId: null, previousItem: null);
        var taskId = await _backgroundTasks.EnqueueAsync(request, cancellationToken);
        NotifyItemStateChanged(item);
        return taskId;
    }

    public async Task<string?> UpdateDesiredProvidersAsync(
        SecretItemRef item,
        IReadOnlyDictionary<string, bool> changes,
        CancellationToken cancellationToken = default)
    {
        var configured = (await _providerRegistry.GetProvidersAsync())
            .Select(provider => provider.Id)
            .ToHashSet(StringComparer.Ordinal);
        var normalizedChanges = changes
            .Where(change => configured.Contains(change.Key) || !change.Value)
            .ToDictionary(change => change.Key, change => change.Value, StringComparer.Ordinal);
        if (normalizedChanges.Count == 0)
            return null;

        var latestPending = _backgroundTasks.Tasks
            .Where(task =>
                task.State == BackgroundTaskState.Pending &&
                string.Equals(task.Request.CoalesceKey, GetCoalesceKey(item), StringComparison.Ordinal))
            .OrderByDescending(task => task.CreatedAtUtc)
            .FirstOrDefault();
        if (latestPending is not null)
        {
            var previousChanges = ParseProviderSelectionChanges(latestPending.Request.Parameters);
            foreach (var (providerId, desired) in previousChanges)
                normalizedChanges.TryAdd(providerId, desired);

            var previousDesired = TryParseDesiredProviders(latestPending.Request.Parameters);
            if (previousDesired is not null)
            {
                ApplyProviderSelectionChanges(previousDesired, normalizedChanges);
                return await SetDesiredProvidersAsync(item, previousDesired, cancellationToken);
            }
        }

        var request = CreateSelectionChangeRequest(item, normalizedChanges);
        var taskId = await _backgroundTasks.EnqueueAsync(request, cancellationToken);
        NotifyItemStateChanged(item);
        return taskId;
    }

    public async Task<string?> SetDefaultProvidersAsync(
        SecretItemRef item,
        CancellationToken cancellationToken = default)
    {
        var providerIds = (await _providerRegistry.GetProvidersAsync())
            .Where(provider => provider.StoresByDefault(item.Kind))
            .Select(provider => provider.Id)
            .ToList();
        if (providerIds.Count == 0 && GetAdapter(item.Kind).IsProviderOwned)
        {
            var current = await GetStateAsync(item, cancellationToken);
            providerIds = current.Providers
                .Where(provider => provider.Observed)
                .Select(provider => provider.ProviderId)
                .ToList();
        }
        return await SetDesiredProvidersAsync(item, providerIds, cancellationToken);
    }

    public async Task<string?> ResolveConflictAsync(
        SecretItemRef item,
        string sourceProviderId,
        CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(item, cancellationToken);
        var desired = state.Providers
            .Where(provider => provider.Desired)
            .Select(provider => provider.ProviderId)
            .ToList();

        var request = CreateRequest(item, desired, sourceProviderId, previousItem: null);
        var taskId = await _backgroundTasks.EnqueueAsync(request, cancellationToken);
        NotifyItemStateChanged(item);
        return taskId;
    }

    public async Task<string?> MoveAsync(
        SecretItemRef previousItem,
        SecretItemRef item,
        IReadOnlyCollection<string> providerIds,
        string sourceProviderId,
        CancellationToken cancellationToken = default)
    {
        if (previousItem.Kind != item.Kind)
            throw new ArgumentException("A synced item cannot change kind while moving.", nameof(item));

        var configured = (await _providerRegistry.GetProvidersAsync())
            .Select(provider => provider.Id)
            .ToHashSet(StringComparer.Ordinal);
        var desired = providerIds
            .Where(configured.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var request = CreateRequest(item, desired, sourceProviderId, previousItem);
        var taskId = await _backgroundTasks.EnqueueAsync(request, cancellationToken);
        NotifyItemStateChanged(previousItem);
        NotifyItemStateChanged(item);
        return taskId;
    }

    public async Task ExecuteAsync(
        BackgroundTaskRequest request,
        IBackgroundTaskContext context)
    {
        var item = ParseItem(request.Parameters);
        var desiredProviderIds = TryParseDesiredProviders(request.Parameters);
        var selectionChanges = ParseProviderSelectionChanges(request.Parameters);
        request.Parameters.TryGetValue("sourceProviderId", out var sourceProviderId);
        var adapter = GetAdapter(item.Kind);

        try
        {
            context.SetStatus("Inspecting provider copies");
            context.SetProgress(5);

            var providers = (await _providerRegistry.GetProvidersAsync())
                .OrderBy(provider =>
                    string.Equals(
                        provider.Id,
                        CloudSecretsService.DefaultLocalProviderId,
                        StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(provider => provider.Id, StringComparer.Ordinal)
                .ToList();
            var providerById = providers.ToDictionary(provider => provider.Id, StringComparer.Ordinal);
            var payloads = new Dictionary<string, SecretItemPayload>(StringComparer.Ordinal);
            var manifests = new List<SecretSyncManifest>();
            var unreadableProviderIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var provider in providers)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var providerInstance = await _providerRegistry.GetProviderAsync(provider.Id);
                    if (providerInstance is null)
                        throw new InvalidOperationException($"Provider '{provider.Id}' is not configured.");

                    var providerKeys = await providerInstance.ListSecretsAsync(
                        prefix: null,
                        cancellationToken: context.CancellationToken);
                    var presentIds = adapter.ExtractProviderItemIds(providerKeys);
                    var itemExists = presentIds.Contains(item.Id) ||
                        presentIds.Any(candidate =>
                            SecretItemAdapterHelper.StorageKeysEqual(candidate, item.Id));
                    if (itemExists)
                    {
                        var payload = await adapter.ReadProviderAsync(
                            item,
                            provider.Id,
                            context.CancellationToken);
                        if (payload is not null)
                            payloads[provider.Id] = payload;
                    }

                    var manifestKey = GetManifestKey(item);
                    if (providerKeys.Any(key =>
                        SecretItemAdapterHelper.StorageKeysEqual(key, manifestKey)))
                    {
                        var manifest = await ReadManifestAsync(
                            provider.Id,
                            item,
                            context.CancellationToken);
                        if (manifest is not null)
                            manifests.Add(manifest);
                    }
                }
                catch (Exception ex)
                {
                    unreadableProviderIds.Add(provider.Id);
                    if (desiredProviderIds?.Contains(provider.Id) == true ||
                        selectionChanges.GetValueOrDefault(provider.Id))
                    {
                        throw new BackgroundTaskTransientException(
                            $"Could not read provider '{provider.Name}': {ex.Message}",
                            ex);
                    }
                }

            }

            desiredProviderIds ??= SelectManifest(manifests
                    .Select(manifest => (ProviderId: string.Empty, Manifest: manifest)))?
                .DesiredProviderIds
                .ToHashSet(StringComparer.Ordinal)
                ?? payloads.Keys.ToHashSet(StringComparer.Ordinal);
            if (TryParseDesiredProviders(request.Parameters) is null)
            {
                foreach (var providerId in unreadableProviderIds.Where(providerId =>
                    !selectionChanges.TryGetValue(providerId, out var desired) || desired))
                {
                    desiredProviderIds.Add(providerId);
                }
            }
            ApplyProviderSelectionChanges(desiredProviderIds, selectionChanges);

            SecretItemPayload? sourcePayload = null;
            if (desiredProviderIds.Count > 0)
            {
                var distinctHashes = payloads.Values
                    .Select(payload => payload.ContentHash)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (distinctHashes.Count > 1 && string.IsNullOrWhiteSpace(sourceProviderId))
                    throw new InvalidOperationException(
                        "Provider copies contain different values. Choose the source copy before retrying.");

                if (string.Equals(sourceProviderId, PlatformSourceId, StringComparison.Ordinal))
                {
                    sourcePayload = await adapter.ReadLocalAsync(item, context.CancellationToken);
                }
                else if (!string.IsNullOrWhiteSpace(sourceProviderId))
                {
                    payloads.TryGetValue(sourceProviderId, out sourcePayload);
                    sourcePayload ??= await adapter.ReadProviderAsync(
                        item,
                        sourceProviderId,
                        context.CancellationToken);
                }
                else
                {
                    if (item.Kind == SecretItemKind.Certificate && payloads.Count > 0)
                    {
                        if (payloads.TryGetValue(CloudSecretsService.DefaultLocalProviderId, out var localVaultPayload))
                            sourcePayload = localVaultPayload;
                        sourcePayload ??= payloads.Values.FirstOrDefault();
                    }

                    sourcePayload ??= await adapter.ReadLocalAsync(item, context.CancellationToken);
                    if (sourcePayload is null &&
                        payloads.TryGetValue(CloudSecretsService.DefaultLocalProviderId, out var localProviderPayload))
                    {
                        sourcePayload = localProviderPayload;
                    }
                    sourcePayload ??= payloads.Values.FirstOrDefault();
                }

                if (sourcePayload is null)
                {
                    throw new InvalidOperationException(item.Kind == SecretItemKind.Certificate
                        ? "No certificate private key source is available. Import it into Keychain or install it from another provider before syncing."
                        : "No readable source copy is available for this item.");
                }
            }

            var nextRevision = Math.Max(
                sourcePayload?.Revision ?? 0,
                manifests.Count == 0 ? 0 : manifests.Max(manifest => manifest.Revision)) + 1;
            SecretSyncManifest? nextManifest = sourcePayload is null
                ? null
                : new SecretSyncManifest(
                    SecretSyncManifest.CurrentSchemaVersion,
                    item.Kind,
                    item.Id,
                    item.DisplayName,
                    nextRevision,
                    sourcePayload.ContentHash,
                    desiredProviderIds.Order(StringComparer.Ordinal).ToList(),
                    DateTime.UtcNow);
            var nextPayload = sourcePayload is null
                ? null
                : sourcePayload with { Revision = nextRevision };

            var orderedDesired = desiredProviderIds
                .OrderBy(providerId =>
                    string.Equals(providerId, CloudSecretsService.DefaultLocalProviderId, StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(providerId => providerId, StringComparer.Ordinal)
                .ToList();
            var orderedRemovals = providers
                .Where(provider => !desiredProviderIds.Contains(provider.Id))
                .ToList();
            SecretItemRef? previousItemToRemove = null;
            if (TryParsePreviousItem(request.Parameters, out var previousItem) &&
                !string.Equals(previousItem.Id, item.Id, StringComparison.Ordinal))
            {
                previousItemToRemove = previousItem;
            }

            var steps = new List<BackgroundTaskStepInfo>();
            foreach (var providerId in orderedDesired)
            {
                if (!providerById.TryGetValue(providerId, out var config))
                    throw new InvalidOperationException($"Provider '{providerId}' is no longer configured.");

                steps.Add(new BackgroundTaskStepInfo(
                    $"sync:{providerId}",
                    config.Name,
                    BackgroundTaskStepState.Pending,
                    "Waiting to sync"));
            }
            steps.AddRange(orderedRemovals.Select(provider =>
                new BackgroundTaskStepInfo(
                    $"remove:{provider.Id}",
                    provider.Name,
                    BackgroundTaskStepState.Pending,
                    "Waiting to remove")));
            if (previousItemToRemove is not null)
            {
                steps.AddRange(providers.Select(provider =>
                    new BackgroundTaskStepInfo(
                        $"cleanup:{provider.Id}",
                        provider.Name,
                        BackgroundTaskStepState.Pending,
                        $"Waiting to remove old copy of {previousItemToRemove.DisplayName}")));
            }
            context.SetSteps(steps);

            async Task RunProviderStepAsync(
                string stepId,
                string runningStatus,
                string successStatus,
                Func<Task> operation)
            {
                context.SetStatus(runningStatus);
                context.UpdateStep(stepId, BackgroundTaskStepState.Running, runningStatus);
                try
                {
                    await operation();
                    context.UpdateStep(stepId, BackgroundTaskStepState.Succeeded, successStatus);
                }
                catch (OperationCanceledException)
                {
                    context.UpdateStep(stepId, BackgroundTaskStepState.Cancelled, "Cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    context.UpdateStep(stepId, BackgroundTaskStepState.Failed, "Failed", ex.Message);
                    throw;
                }
            }

            async Task PublishCurrentPlacementAsync(
                string providerId,
                bool desired,
                SecretPlacementStatus status,
                string? error = null)
            {
                payloads.TryGetValue(providerId, out var existingPayload);
                var observed = status switch
                {
                    SecretPlacementStatus.Synced => true,
                    SecretPlacementStatus.NotStored => false,
                    _ => existingPayload is not null
                };
                var placement = new ProviderPlacementState(
                    providerId,
                    desired,
                    observed,
                    status,
                    status == SecretPlacementStatus.Synced
                        ? nextPayload?.Revision
                        : existingPayload?.Revision,
                    status == SecretPlacementStatus.Synced
                        ? nextPayload?.ContentHash
                        : existingPayload?.ContentHash,
                    error);
                await PublishPlacementChangedAsync(item, placement, context.CancellationToken);
            }

            var totalSteps = Math.Max(1, steps.Count);
            var completedSteps = 0;

            foreach (var providerId in orderedDesired)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (!providerById.TryGetValue(providerId, out var config))
                    throw new InvalidOperationException($"Provider '{providerId}' is no longer configured.");

                await PublishCurrentPlacementAsync(
                    providerId,
                    desired: true,
                    SecretPlacementStatus.Pending);
                try
                {
                    await RunProviderStepAsync(
                        $"sync:{providerId}",
                        $"Syncing to {config.Name}",
                        "Synced",
                        async () =>
                        {
                            var existingMatches = payloads.TryGetValue(providerId, out var existing) &&
                                string.Equals(existing.ContentHash, nextPayload!.ContentHash, StringComparison.Ordinal);
                            if (!existingMatches &&
                                !await adapter.WriteProviderAsync(nextPayload!, providerId, context.CancellationToken))
                            {
                                throw new BackgroundTaskTransientException(
                                    $"Failed to write item to provider '{config.Name}'.");
                            }

                            if (!await WriteManifestAsync(providerId, nextManifest!, context.CancellationToken))
                            {
                                throw new BackgroundTaskTransientException(
                                    $"Failed to write sync metadata to provider '{config.Name}'.");
                            }
                            await TryUpdateProviderCatalogAsync(
                                providerId,
                                nextManifest,
                                removeItem: null,
                                context.CancellationToken);

                            context.Log(
                                $"Synced {item.DisplayName} to {config.Name}",
                                OperationLogLevel.Success);
                        });
                    await PublishCurrentPlacementAsync(
                        providerId,
                        desired: true,
                        SecretPlacementStatus.Synced);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await PublishCurrentPlacementAsync(
                        providerId,
                        desired: true,
                        SecretPlacementStatus.Failed,
                        ex.Message);
                    throw;
                }
                completedSteps++;
                context.SetProgress(completedSteps * 100 / totalSteps);
            }

            foreach (var provider in orderedRemovals)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                await PublishCurrentPlacementAsync(
                    provider.Id,
                    desired: false,
                    SecretPlacementStatus.Pending);
                try
                {
                    await RunProviderStepAsync(
                        $"remove:{provider.Id}",
                        $"Removing from {provider.Name}",
                        "Removed",
                        async () =>
                        {
                            var providerInstance = await _providerRegistry.GetProviderAsync(provider.Id);
                            if (providerInstance is null ||
                                !await providerInstance.TestConnectionAsync(context.CancellationToken))
                            {
                                throw new BackgroundTaskTransientException(
                                    $"Provider '{provider.Name}' is unavailable; removal was not confirmed.");
                            }

                            if (!await adapter.DeleteProviderAsync(item, provider.Id, context.CancellationToken))
                            {
                                throw new BackgroundTaskTransientException(
                                    $"Failed to remove item from provider '{provider.Name}'.");
                            }

                            if (!await DeleteManifestAsync(provider.Id, item, context.CancellationToken))
                            {
                                throw new BackgroundTaskTransientException(
                                    $"Failed to remove sync metadata from provider '{provider.Name}'.");
                            }
                            await TryUpdateProviderCatalogAsync(
                                provider.Id,
                                manifest: null,
                                removeItem: item,
                                context.CancellationToken);
                            context.Log(
                                $"Removed {item.DisplayName} from {provider.Name}",
                                OperationLogLevel.Success);
                        });
                    await PublishCurrentPlacementAsync(
                        provider.Id,
                        desired: false,
                        SecretPlacementStatus.NotStored);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await PublishCurrentPlacementAsync(
                        provider.Id,
                        desired: false,
                        SecretPlacementStatus.Failed,
                        ex.Message);
                    throw;
                }
                completedSteps++;
                context.SetProgress(completedSteps * 100 / totalSteps);
            }

            if (previousItemToRemove is not null)
            {
                foreach (var provider in providers)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    await RunProviderStepAsync(
                        $"cleanup:{provider.Id}",
                        $"Removing old copy from {provider.Name}",
                        "Old copy removed",
                        async () =>
                        {
                            var providerInstance = await _providerRegistry.GetProviderAsync(provider.Id);
                            if (providerInstance is null ||
                                !await providerInstance.TestConnectionAsync(context.CancellationToken))
                            {
                                throw new BackgroundTaskTransientException(
                                    $"Provider '{provider.Name}' is unavailable; the old item was retained.");
                            }

                            if (!await adapter.DeleteProviderAsync(
                                    previousItemToRemove,
                                    provider.Id,
                                    context.CancellationToken) ||
                                !await DeleteManifestAsync(
                                    provider.Id,
                                    previousItemToRemove,
                                    context.CancellationToken))
                            {
                                throw new BackgroundTaskTransientException(
                                    $"Failed to remove the old item from provider '{provider.Name}'.");
                            }
                            await TryUpdateProviderCatalogAsync(
                                provider.Id,
                                manifest: null,
                                removeItem: previousItemToRemove,
                                context.CancellationToken);
                        });
                    await PublishPlacementChangedAsync(
                        previousItemToRemove,
                        new ProviderPlacementState(
                            provider.Id,
                            Desired: false,
                            Observed: false,
                            SecretPlacementStatus.NotStored),
                        context.CancellationToken);
                    completedSteps++;
                    context.SetProgress(completedSteps * 100 / totalSteps);
                }

                context.Log(
                    $"Removed old provider copies of {previousItemToRemove.DisplayName}",
                    OperationLogLevel.Success);
                NotifyItemStateChanged(previousItemToRemove);
            }

            context.SetStatus("Placement is up to date");
            context.SetProgress(100);
        }
        finally
        {
            NotifyItemStateChanged(item);
        }
    }

    private BackgroundTaskRequest CreateRequest(
        SecretItemRef item,
        IReadOnlyCollection<string> desiredProviderIds,
        string? sourceProviderId,
        SecretItemRef? previousItem)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = item.Kind.ToString(),
            ["itemId"] = item.Id,
            ["displayName"] = item.DisplayName,
            ["desiredProviderIds"] = JsonSerializer.Serialize(desiredProviderIds, _jsonOptions)
        };
        if (!string.IsNullOrWhiteSpace(sourceProviderId))
            parameters["sourceProviderId"] = sourceProviderId;
        if (previousItem is not null)
        {
            parameters["previousKind"] = previousItem.Kind.ToString();
            parameters["previousItemId"] = previousItem.Id;
            parameters["previousDisplayName"] = previousItem.DisplayName;
        }

        return new BackgroundTaskRequest(
            TaskType,
            $"Sync {item.DisplayName}",
            $"Update secrets-provider placement for {item.DisplayName}.",
            parameters,
            CoalesceKey: GetCoalesceKey(item),
            ItemRoute: GetItemRoute(item.Kind));
    }

    private BackgroundTaskRequest CreateSelectionChangeRequest(
        SecretItemRef item,
        IReadOnlyDictionary<string, bool> changes)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = item.Kind.ToString(),
            ["itemId"] = item.Id,
            ["displayName"] = item.DisplayName,
            ["providerSelectionChanges"] = JsonSerializer.Serialize(changes, _jsonOptions)
        };
        return new BackgroundTaskRequest(
            TaskType,
            $"Sync {item.DisplayName}",
            $"Update secrets-provider placement for {item.DisplayName}.",
            parameters,
            CoalesceKey: GetCoalesceKey(item),
            ItemRoute: GetItemRoute(item.Kind));
    }

    private async Task<ProviderSyncCatalog?> GetProviderCatalogSnapshotAsync(
        string providerId,
        CancellationToken cancellationToken) =>
        await GetSnapshotAsync(
            _catalogSnapshots,
            providerId,
            () => ReadProviderCatalogUncachedAsync(providerId),
            cancellationToken);

    private async Task<IReadOnlySet<string>> GetProviderPresenceSnapshotAsync(
        ISecretItemAdapter adapter,
        string providerId,
        CancellationToken cancellationToken)
    {
        var key = GetPresenceSnapshotKey(providerId, adapter.Kind);
        return await GetSnapshotAsync(
            _presenceSnapshots,
            key,
            async () => adapter.ExtractProviderItemIds(
                await GetProviderKeySnapshotAsync(providerId, CancellationToken.None)),
            cancellationToken);
    }

    private async Task<IReadOnlyList<string>> GetProviderKeySnapshotAsync(
        string providerId,
        CancellationToken cancellationToken) =>
        await GetSnapshotAsync(
            _providerKeySnapshots,
            providerId,
            async () =>
            {
                var provider = await _providerRegistry.GetProviderAsync(providerId);
                if (provider is null)
                    throw new InvalidOperationException($"Provider '{providerId}' is not configured.");

                return await provider.ListSecretsAsync(
                    prefix: null,
                    cancellationToken: CancellationToken.None);
            },
            cancellationToken);

    private async Task<T> GetSnapshotAsync<T>(
        ConcurrentDictionary<string, TimedSnapshot<T>> snapshots,
        string key,
        Func<Task<T>> load,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var now = DateTime.UtcNow;
            if (snapshots.TryGetValue(key, out var current) &&
                current.ExpiresAtUtc > now)
            {
                return await current.Value.Value.WaitAsync(cancellationToken);
            }

            var replacement = new TimedSnapshot<T>(
                now + StatusSnapshotLifetime,
                new Lazy<Task<T>>(
                    load,
                    LazyThreadSafetyMode.ExecutionAndPublication));
            var accepted = current is null
                ? snapshots.TryAdd(key, replacement)
                : snapshots.TryUpdate(key, replacement, current);
            if (!accepted)
                continue;

            try
            {
                return await replacement.Value.Value.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                snapshots.TryRemove(
                    new KeyValuePair<string, TimedSnapshot<T>>(key, replacement));
                throw;
            }
        }
    }

    private async Task<ProviderSyncCatalog?> ReadProviderCatalogUncachedAsync(string providerId)
    {
        var provider = await _providerRegistry.GetProviderAsync(providerId);
        if (provider is null)
            throw new InvalidOperationException($"Provider '{providerId}' is not configured.");

        var keys = await GetProviderKeySnapshotAsync(providerId, CancellationToken.None);
        if (!keys.Any(key => SecretItemAdapterHelper.StorageKeysEqual(key, CatalogKey)))
            return null;

        var bytes = await provider.GetSecretAsync(CatalogKey, CancellationToken.None);
        if (bytes is null)
            return null;

        return JsonSerializer.Deserialize<ProviderSyncCatalog>(bytes, _jsonOptions);
    }

    private async Task TryUpdateProviderCatalogAsync(
        string providerId,
        SecretSyncManifest? manifest,
        SecretItemRef? removeItem,
        CancellationToken cancellationToken)
    {
        var writeLock = _catalogWriteLocks.GetOrAdd(providerId, _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            var provider = await _providerRegistry.GetProviderAsync(providerId);
            if (provider is null)
                return;

            ProviderSyncCatalog? catalog = null;
            try
            {
                if (await provider.SecretExistsAsync(CatalogKey, cancellationToken))
                {
                    var bytes = await provider.GetSecretAsync(CatalogKey, cancellationToken);
                    if (bytes is not null)
                        catalog = JsonSerializer.Deserialize<ProviderSyncCatalog>(bytes, _jsonOptions);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    $"Could not read sync catalog from provider '{providerId}': {ex.Message}");
                return;
            }

            var entries = catalog?.Entries is null
                ? new Dictionary<string, ProviderSyncCatalogEntry>(StringComparer.Ordinal)
                : new Dictionary<string, ProviderSyncCatalogEntry>(
                    catalog.Entries,
                    StringComparer.Ordinal);
            if (manifest is not null)
            {
                var item = new SecretItemRef(
                    manifest.Kind,
                    manifest.ItemId,
                    manifest.DisplayName);
                entries[GetCatalogEntryKey(item)] = new ProviderSyncCatalogEntry(
                    manifest.Kind,
                    manifest.ItemId,
                    manifest.DisplayName,
                    manifest.Revision,
                    manifest.ContentHash,
                    manifest.DesiredProviderIds.ToList(),
                    manifest.UpdatedAtUtc);
            }
            if (removeItem is not null)
                entries.Remove(GetCatalogEntryKey(removeItem));

            var updated = new ProviderSyncCatalog(
                ProviderSyncCatalog.CurrentSchemaVersion,
                entries,
                DateTime.UtcNow);
            var stored = await provider.StoreSecretAsync(
                CatalogKey,
                JsonSerializer.SerializeToUtf8Bytes(updated, _jsonOptions),
                new Dictionary<string, string>
                {
                    ["SherpaKind"] = "SyncCatalog",
                    ["SchemaVersion"] = ProviderSyncCatalog.CurrentSchemaVersion.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                },
                cancellationToken);
            if (!stored)
                _logger.LogWarning($"Provider '{providerId}' rejected the sync catalog update.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not update sync catalog for provider '{providerId}': {ex.Message}");
        }
        finally
        {
            InvalidateProviderStatusSnapshots(providerId);
            writeLock.Release();
        }
    }

    private void InvalidateProviderStatusSnapshots(string providerId)
    {
        _catalogSnapshots.TryRemove(providerId, out _);
        _providerKeySnapshots.TryRemove(providerId, out _);
        var prefix = providerId + "|";
        foreach (var key in _presenceSnapshots.Keys.Where(key =>
            key.StartsWith(prefix, StringComparison.Ordinal)))
        {
            _presenceSnapshots.TryRemove(key, out _);
        }
    }

    private void InvalidateAllStatusSnapshots()
    {
        _catalogSnapshots.Clear();
        _providerKeySnapshots.Clear();
        _presenceSnapshots.Clear();
    }

    private static string GetPresenceSnapshotKey(
        string providerId,
        SecretItemKind kind) =>
        $"{providerId}|{kind}";

    private static string GetCatalogEntryKey(SecretItemRef item) =>
        $"{item.Kind}:{item.Id}";

    private BackgroundTaskInfo? GetLatestTask(SecretItemRef item) =>
        _backgroundTasks.Tasks
            .Where(task => string.Equals(task.Request.CoalesceKey, GetCoalesceKey(item), StringComparison.Ordinal))
            .OrderByDescending(task => task.CreatedAtUtc)
            .FirstOrDefault();

    private static SecretPlacementStatus GetPlacementStatus(
        bool desired,
        bool observed,
        ProviderObservation observation,
        ProviderSyncCatalogEntry? selectedEntry,
        bool hasConflict,
        BackgroundTaskInfo? task)
    {
        if (observation.Error is not null)
            return SecretPlacementStatus.Unavailable;
        if (hasConflict && observed)
            return SecretPlacementStatus.Conflict;
        if (task?.State is BackgroundTaskState.Pending or BackgroundTaskState.Running &&
            desired != observed)
        {
            return SecretPlacementStatus.Pending;
        }
        if (task?.State == BackgroundTaskState.Failed && desired != observed)
            return SecretPlacementStatus.Failed;
        if (!desired && !observed)
            return SecretPlacementStatus.NotStored;
        if (!desired && observed)
            return SecretPlacementStatus.Failed;
        if (desired && !observed)
            return SecretPlacementStatus.Failed;
        if (selectedEntry is not null &&
            !string.IsNullOrEmpty(observation.ContentHash) &&
            !string.Equals(
                selectedEntry.ContentHash,
                observation.ContentHash,
                StringComparison.Ordinal))
        {
            return SecretPlacementStatus.Failed;
        }

        return SecretPlacementStatus.Synced;
    }

    private static ProviderSyncCatalogEntry? SelectCatalogEntry(
        IReadOnlyDictionary<string, ProviderObservation> observations) =>
        observations.Values
            .Where(observation => observation.Observed &&
                observation.Revision.HasValue &&
                observation.DesiredProviderIds is not null)
            .OrderByDescending(observation => observation.Revision)
            .Select(observation => new ProviderSyncCatalogEntry(
                SecretItemKind.ManagedSecret,
                string.Empty,
                string.Empty,
                observation.Revision!.Value,
                observation.ContentHash ?? string.Empty,
                observation.DesiredProviderIds!.ToList(),
                DateTime.MinValue))
            .FirstOrDefault();

    private SecretSyncManifest? SelectManifest(
        IEnumerable<(string ProviderId, SecretSyncManifest Manifest)> manifests) =>
        manifests
            .Select(pair => pair.Manifest)
            .OrderByDescending(manifest => manifest.Revision)
            .ThenByDescending(manifest => manifest.UpdatedAtUtc)
            .FirstOrDefault();

    private async Task<SecretSyncManifest?> ReadManifestAsync(
        string providerId,
        SecretItemRef item,
        CancellationToken cancellationToken)
    {
        var provider = await _providerRegistry.GetProviderAsync(providerId);
        if (provider is null)
            throw new InvalidOperationException($"Provider '{providerId}' is not configured.");

        var bytes = await provider.GetSecretAsync(GetManifestKey(item), cancellationToken);
        return bytes is null
            ? null
            : JsonSerializer.Deserialize<SecretSyncManifest>(bytes, _jsonOptions);
    }

    private async Task<bool> WriteManifestAsync(
        string providerId,
        SecretSyncManifest manifest,
        CancellationToken cancellationToken)
    {
        var provider = await _providerRegistry.GetProviderAsync(providerId);
        if (provider is null)
            return false;

        var item = new SecretItemRef(manifest.Kind, manifest.ItemId, manifest.DisplayName);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, _jsonOptions);
        return await provider.StoreSecretAsync(
            GetManifestKey(item),
            bytes,
            new Dictionary<string, string>
            {
                ["SherpaKind"] = "SyncManifest",
                ["ItemKind"] = manifest.Kind.ToString(),
                ["ItemId"] = manifest.ItemId,
                ["Revision"] = manifest.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ContentHash"] = manifest.ContentHash
            },
            cancellationToken);
    }

    private async Task<bool> DeleteManifestAsync(
        string providerId,
        SecretItemRef item,
        CancellationToken cancellationToken)
    {
        var provider = await _providerRegistry.GetProviderAsync(providerId);
        if (provider is null)
            return false;

        var key = GetManifestKey(item);
        if (!await provider.SecretExistsAsync(key, cancellationToken))
            return true;

        return await provider.DeleteSecretAsync(key, cancellationToken);
    }

    private static string GetManifestKey(SecretItemRef item)
    {
        var itemHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(item.Id)))
            .ToLowerInvariant();
        return $"{ManifestPrefix}{item.Kind}/{itemHash}";
    }

    private static string GetCoalesceKey(SecretItemRef item) =>
        $"{TaskType}:{item.Kind}:{item.Id}";

    private static string GetItemRoute(SecretItemKind kind) => kind switch
    {
        SecretItemKind.ManagedSecret => "/secrets",
        SecretItemKind.AndroidKeystore => "/keystores",
        SecretItemKind.Certificate => "/certificates",
        SecretItemKind.ProvisioningProfile => "/profiles",
        SecretItemKind.PublishProfile => "/secrets/publish",
        _ => "/"
    };

    private static HashSet<string>? TryParseDesiredProviders(
        IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("desiredProviderIds", out var json))
            return null;

        var providerIds = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        return providerIds.ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, bool> ParseProviderSelectionChanges(
        IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("providerSelectionChanges", out var json))
            return new Dictionary<string, bool>(StringComparer.Ordinal);

        return JsonSerializer.Deserialize<Dictionary<string, bool>>(json)
            ?? new Dictionary<string, bool>(StringComparer.Ordinal);
    }

    private static void ApplyProviderSelectionChanges(
        HashSet<string> providerIds,
        IReadOnlyDictionary<string, bool> changes)
    {
        foreach (var (providerId, desired) in changes)
        {
            if (desired)
                providerIds.Add(providerId);
            else
                providerIds.Remove(providerId);
        }
    }

    private static SecretItemRef ParseItem(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("kind", out var kindValue) ||
            !Enum.TryParse<SecretItemKind>(kindValue, out var kind) ||
            !parameters.TryGetValue("itemId", out var itemId) ||
            !parameters.TryGetValue("displayName", out var displayName))
        {
            throw new InvalidOperationException("The sync task is missing its item identity.");
        }

        return new SecretItemRef(kind, itemId, displayName);
    }

    private static bool TryParsePreviousItem(
        IReadOnlyDictionary<string, string> parameters,
        out SecretItemRef item)
    {
        item = default!;
        if (!parameters.TryGetValue("previousKind", out var kindValue) ||
            !Enum.TryParse<SecretItemKind>(kindValue, out var kind) ||
            !parameters.TryGetValue("previousItemId", out var itemId) ||
            !parameters.TryGetValue("previousDisplayName", out var displayName))
        {
            return false;
        }

        item = new SecretItemRef(kind, itemId, displayName);
        return true;
    }

    private ISecretItemAdapter GetAdapter(SecretItemKind kind) =>
        _adapters.TryGetValue(kind, out var adapter)
            ? adapter
            : throw new InvalidOperationException($"No sync adapter is registered for {kind}.");

    private async Task PublishPlacementChangedAsync(
        SecretItemRef item,
        ProviderPlacementState placement,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mediator.Publish(
                new SecretProviderPlacementChangedEvent(item, placement),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                $"Could not publish provider state for '{item.DisplayName}' and '{placement.ProviderId}': {ex.Message}");
        }
    }

    private void NotifyItemStateChanged(SecretItemRef item)
    {
        if (ItemStateChanged is null)
            return;

        foreach (Action<SecretItemRef> subscriber in ItemStateChanged.GetInvocationList())
        {
            try
            {
                subscriber(item);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"A secret sync notification subscriber failed: {ex.Message}");
            }
        }
    }

    private sealed record ProviderObservation(
        bool Observed,
        string? ContentHash,
        long? Revision,
        IReadOnlyList<string>? DesiredProviderIds,
        string? Error
    );

    private sealed record CompletedProviderObservation(
        CloudSecretsProviderConfig Provider,
        ProviderObservation Observation
    );

    private sealed record TimedSnapshot<T>(
        DateTime ExpiresAtUtc,
        Lazy<Task<T>> Value
    );
}
