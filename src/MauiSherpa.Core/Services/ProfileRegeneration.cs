using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Shared steps for flows that recreate a provisioning profile in App Store Connect:
/// install the regenerated profile and remove the installed copies of the original.
/// </summary>
internal static class ProfileRegeneration
{
    internal sealed record InstallOutcome(
        string? InstalledPath,
        string? InstallError,
        IReadOnlyList<string> RemovedInstalledPaths);

    /// <summary>
    /// Installs <paramref name="regenerated"/> and, when asked and the install worked, removes
    /// installed copies of <paramref name="original"/>. Never throws for install or cleanup failures.
    /// </summary>
    internal static async Task<InstallOutcome> InstallAsync(
        IAppleConnectService appleService,
        ILoggingService logger,
        AppleProfile original,
        AppleProfile regenerated,
        bool removeOldInstalledCopies,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        string? installedPath = null;
        string? installError = null;
        try
        {
            progress?.Report($"Installing regenerated '{regenerated.Name}'...");
            installedPath = await appleService.InstallProfileAsync(regenerated.Id);
        }
        catch (Exception ex)
        {
            installError = ex.Message;
            logger.LogWarning($"Regenerated profile '{regenerated.Name}' could not be installed: {ex.Message}");
        }

        IReadOnlyList<string> removed = [];
        if (removeOldInstalledCopies && installError is null)
            removed = await RemoveOldInstalledCopiesAsync(appleService, logger, original, regenerated, progress, cancellationToken);

        return new InstallOutcome(installedPath, installError, removed);
    }

    static async Task<IReadOnlyList<string>> RemoveOldInstalledCopiesAsync(
        IAppleConnectService appleService,
        ILoggingService logger,
        AppleProfile original,
        AppleProfile regenerated,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(original.Uuid) ||
            string.Equals(original.Uuid, regenerated.Uuid, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        try
        {
            var paths = await appleService.FindInstalledProfilePathsAsync(original.Uuid);
            if (paths.Count == 0)
                return [];

            await ProvisioningProfileCleanupAnalyzer.DeleteAsync(paths, cancellationToken);
            progress?.Report($"Removed {paths.Count} old installed copy(ies) of '{original.Name}'");
            return paths;
        }
        catch (Exception ex)
        {
            // The new profile is installed; a stale old copy is harmless to leave behind.
            logger.LogWarning($"Could not remove old installed copies of '{original.Name}': {ex.Message}");
            progress?.Report($"Could not remove the old installed copy: {ex.Message}");
            return [];
        }
    }
}
