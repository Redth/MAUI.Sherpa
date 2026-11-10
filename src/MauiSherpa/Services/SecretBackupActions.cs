using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

/// <summary>
/// Shared bulk "Back up to provider" flow for list pages with multi-select.
/// </summary>
public static class SecretBackupActions
{
    /// <summary>
    /// Asks which configured secrets provider to back up to. Skips the prompt when only one
    /// provider exists. Returns null when the user cancels or no provider is configured.
    /// </summary>
    public static async Task<CloudSecretsProviderConfig?> PickProviderAsync(
        ICloudSecretsService cloudSecretsService,
        IAlertService alertService,
        string title)
    {
        var providers = (await cloudSecretsService.GetProvidersAsync()).ToList();
        if (providers.Count == 0)
        {
            await alertService.ShowAlertAsync(
                "No Secrets Provider",
                "Configure a secrets provider in Settings before backing up.");
            return null;
        }

        if (providers.Count == 1)
            return providers[0];

        // Provider names aren't unique (and could even be "Cancel"), so give every choice a
        // distinct label and map it back to its provider by position.
        const string cancel = "Cancel";
        var labels = new List<string>();
        foreach (var provider in providers)
        {
            var label = $"{provider.Name} ({provider.ProviderType})";
            var candidate = label;
            for (var n = 2; candidate == cancel || labels.Contains(candidate); n++)
                candidate = $"{label} #{n}";
            labels.Add(candidate);
        }

        var choice = await alertService.ShowActionSheetAsync(title, cancel, null, labels.ToArray());
        var index = choice is null ? -1 : labels.IndexOf(choice);
        return index >= 0 ? providers[index] : null;
    }

    /// <summary>
    /// Adds the provider to each item's desired placement. The sync coordinator runs the
    /// copies as background tasks, so this returns as soon as they are queued.
    /// </summary>
    public static async Task<int> QueueBackupAsync(
        ISecretSyncCoordinator coordinator,
        IEnumerable<SecretItemRef> items,
        string providerId)
    {
        var changes = new Dictionary<string, bool>(StringComparer.Ordinal) { [providerId] = true };
        var queued = 0;
        foreach (var item in items)
        {
            await coordinator.UpdateDesiredProvidersAsync(item, changes);
            queued++;
        }

        return queued;
    }
}
