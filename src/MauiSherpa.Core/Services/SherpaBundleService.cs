using System.Text.Json;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public sealed class SherpaBundleService(ICloudSecretsService cloudSecretsService) : ISherpaBundleService
{
    private const string KeyPrefix = "sherpa-bundles/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public event Action? OnBundlesChanged;

    public async Task<IReadOnlyList<SherpaBundleDefinition>> GetBundlesAsync(
        CancellationToken cancellationToken = default)
    {
        await cloudSecretsService.InitializeAsync();
        if (cloudSecretsService.ActiveProvider is null)
            return [];

        var definitions = new List<SherpaBundleDefinition>();
        foreach (var key in await cloudSecretsService.ListSecretsAsync(KeyPrefix, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await cloudSecretsService.GetSecretAsync(key, cancellationToken);
            if (bytes is null)
                continue;
            var definition = JsonSerializer.Deserialize<SherpaBundleDefinition>(bytes, JsonOptions);
            if (definition is not null)
                definitions.Add(definition);
        }
        return definitions.OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<SherpaBundleDefinition?> GetBundleAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return (await GetBundlesAsync(cancellationToken))
            .FirstOrDefault(definition => definition.Id == id);
    }

    public async Task SaveBundleAsync(
        SherpaBundleDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        await cloudSecretsService.InitializeAsync();
        if (cloudSecretsService.ActiveProvider is null)
            throw new InvalidOperationException("No secrets provider is configured.");

        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            definition with { UpdatedAt = DateTime.UtcNow },
            JsonOptions);
        if (!await cloudSecretsService.StoreSecretAsync(
                KeyPrefix + definition.Id,
                bytes,
                cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException($"Failed to save Expedition Pack '{definition.Name}'.");
        }
        OnBundlesChanged?.Invoke();
    }

    public async Task DeleteBundleAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        cancellationToken.ThrowIfCancellationRequested();
        await cloudSecretsService.InitializeAsync();
        if (cloudSecretsService.ActiveProvider is null)
            throw new InvalidOperationException("No secrets provider is configured.");

        if (!await cloudSecretsService.DeleteSecretAsync(KeyPrefix + id, cancellationToken))
            throw new InvalidOperationException($"Failed to delete Expedition Pack '{id}'.");
        OnBundlesChanged?.Invoke();
    }
}
