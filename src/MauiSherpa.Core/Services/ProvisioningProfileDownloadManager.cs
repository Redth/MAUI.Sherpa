using AppleDev;
using MauiSherpa.Core.Interfaces;
using System.Security.Cryptography;

namespace MauiSherpa.Core.Services;

public enum ProvisioningProfileDownloadConflict
{
    None,
    ExistingFile,
    NewerProfile
}

public sealed record ProvisioningProfileDownloadPlan(
    string TargetPath,
    IReadOnlyList<string> AutomaticReplacementPaths,
    IReadOnlyList<string> ConfirmedReplacementPaths,
    IReadOnlyDictionary<string, string> ExpectedFileHashes,
    bool TargetExisted,
    ProvisioningProfileDownloadConflict Conflict)
{
    public bool RequiresConfirmation => Conflict != ProvisioningProfileDownloadConflict.None;
}

public static class ProvisioningProfileDownloadManager
{
    public static async Task<ProvisioningProfileDownloadPlan> PlanAsync(
        string directory,
        string fileName,
        AppleProfile profile,
        byte[] incomingContent,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(incomingContent);

        var targetPath = Path.Combine(directory, fileName);
        if (!Directory.Exists(directory))
        {
            return new ProvisioningProfileDownloadPlan(
                targetPath,
                [],
                [],
                new Dictionary<string, string>(GetPathComparer()),
                TargetExisted: false,
                Conflict: ProvisioningProfileDownloadConflict.None);
        }

        var incoming = await ProvisioningProfiles.ParseAsync(incomingContent).ConfigureAwait(false);
        var incomingKind = GetProfileKind(
            incoming,
            ProvisioningProfileMetadata.ProvisionsAllDevices(incomingContent));
        var incomingPlatform = GetPlatform(incoming);
        var candidates = GetNamedCopies(directory, fileName);
        var automaticReplacements = new List<string>();
        var confirmedReplacements = new List<string>();
        var expectedFileHashes = new Dictionary<string, string>(GetPathComparer());
        var hasNewerProfile = false;
        var currentTime = now ?? DateTimeOffset.UtcNow;

        foreach (var candidatePath in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existingContent = await File.ReadAllBytesAsync(candidatePath, cancellationToken).ConfigureAwait(false);
            expectedFileHashes[candidatePath] = Convert.ToHexString(SHA256.HashData(existingContent));

            ProvisioningProfileInfo? existing = null;
            var existingProvisionsAllDevices = false;
            try
            {
                existing = await ProvisioningProfiles.ParseAsync(existingContent).ConfigureAwait(false);
                existingProvisionsAllDevices = ProvisioningProfileMetadata.ProvisionsAllDevices(existingContent);
            }
            catch
            {
                // An unreadable exact-name collision must be confirmed before it is overwritten.
            }

            if (existing is not null &&
                IsSameLogicalProfile(
                    profile,
                    incoming,
                    incomingKind,
                    incomingPlatform,
                    existing,
                    existingProvisionsAllDevices))
            {
                var isSameUuid = string.Equals(existing.Uuid, incoming.Uuid, StringComparison.OrdinalIgnoreCase);
                var isExpired = existing.ExpirationDate <= currentTime;
                var isOlder = existing.CreationDate <= incoming.CreationDate;

                if (isSameUuid || isExpired || isOlder)
                {
                    automaticReplacements.Add(candidatePath);
                }
                else
                {
                    hasNewerProfile = true;
                    confirmedReplacements.Add(candidatePath);
                }
            }
            else if (string.Equals(candidatePath, targetPath, PathComparison))
            {
                confirmedReplacements.Add(candidatePath);
            }
        }

        var conflict = hasNewerProfile
            ? ProvisioningProfileDownloadConflict.NewerProfile
            : confirmedReplacements.Count > 0
                ? ProvisioningProfileDownloadConflict.ExistingFile
                : ProvisioningProfileDownloadConflict.None;

        return new ProvisioningProfileDownloadPlan(
            targetPath,
            automaticReplacements,
            confirmedReplacements,
            expectedFileHashes,
            expectedFileHashes.Keys.Any(path =>
                string.Equals(path, targetPath, PathComparison)),
            conflict);
    }

    public static async Task SaveAsync(
        ProvisioningProfileDownloadPlan plan,
        byte[] content,
        bool replaceConfirmedConflicts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(content);

        if (plan.RequiresConfirmation && !replaceConfirmedConflicts)
            throw new InvalidOperationException("The existing provisioning profile must be confirmed before replacement.");

        var directory = Path.GetDirectoryName(plan.TargetPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await ValidatePlanAsync(plan, cancellationToken).ConfigureAwait(false);

        var tempPath = Path.Combine(
            directory!,
            $".{Path.GetFileName(plan.TargetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, plan.TargetPath, overwrite: true);

            foreach (var path in plan.AutomaticReplacementPaths
                         .Concat(plan.ConfirmedReplacementPaths)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!string.Equals(path, plan.TargetPath, PathComparison) &&
                    File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static IReadOnlyList<string> GetNamedCopies(string directory, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);

        return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(
                Path.GetExtension(path),
                extension,
                StringComparison.OrdinalIgnoreCase))
            .Where(path => IsNamedCopy(Path.GetFileNameWithoutExtension(path), baseName))
            .ToList();
    }

    private static bool IsNamedCopy(string candidateName, string baseName)
    {
        if (string.Equals(candidateName, baseName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!candidateName.StartsWith($"{baseName} (", StringComparison.OrdinalIgnoreCase) ||
            !candidateName.EndsWith(')'))
        {
            return false;
        }

        var suffix = candidateName.AsSpan(baseName.Length + 2, candidateName.Length - baseName.Length - 3);
        return int.TryParse(suffix, out var copyNumber) && copyNumber > 0;
    }

    private static async Task ValidatePlanAsync(
        ProvisioningProfileDownloadPlan plan,
        CancellationToken cancellationToken)
    {
        if (!plan.TargetExisted && File.Exists(plan.TargetPath))
        {
            throw new IOException(
                $"'{plan.TargetPath}' was created after the replacement review. Download again to review the new conflict.");
        }

        foreach (var (path, expectedHash) in plan.ExpectedFileHashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
                continue;

            var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var currentHash = Convert.ToHexString(SHA256.HashData(content));
            if (!string.Equals(currentHash, expectedHash, StringComparison.Ordinal))
            {
                throw new IOException(
                    $"'{path}' changed after the replacement review. Download again to review the updated file.");
            }
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static StringComparer GetPathComparer() =>
        PathComparison == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static bool IsSameLogicalProfile(
        AppleProfile profile,
        ProvisioningProfileInfo incoming,
        string incomingKind,
        string incomingPlatform,
        ProvisioningProfileInfo existing,
        bool existingProvisionsAllDevices)
    {
        if (!string.IsNullOrWhiteSpace(incoming.Uuid) &&
            string.Equals(incoming.Uuid, existing.Uuid, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(incoming.Name, existing.Name, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(
                incomingKind,
                GetProfileKind(existing, existingProvisionsAllDevices),
                StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(incomingPlatform, GetPlatform(existing), StringComparison.OrdinalIgnoreCase))
            return false;

        var expectedBundleId = profile.BundleId;
        var incomingBundleId = GetBundleIdentifier(incoming);
        var existingBundleId = GetBundleIdentifier(existing);

        if (!string.IsNullOrWhiteSpace(expectedBundleId))
        {
            return string.Equals(incomingBundleId, expectedBundleId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(existingBundleId, expectedBundleId, StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(incomingBundleId) &&
               string.Equals(incomingBundleId, existingBundleId, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProfileKind(
        ProvisioningProfileInfo profile,
        bool provisionsAllDevices)
    {
        if (profile.Entitlements.TryGetValue("get-task-allow", out var getTaskAllow) &&
            getTaskAllow is true)
        {
            return "Development";
        }

        if (profile.ProvisionedDevices.Length > 0)
            return "Ad Hoc";

        return provisionsAllDevices ? "Direct" : "App Store";
    }

    private static string GetPlatform(ProvisioningProfileInfo profile) =>
        profile.Platform.FirstOrDefault()?.ToUpperInvariant() switch
        {
            "OSX" or "MACOS" or "MAC_OS" => "macOS",
            "TVOS" => "tvOS",
            _ => "iOS"
        };

    private static string? GetBundleIdentifier(ProvisioningProfileInfo profile)
    {
        if (!TryGetEntitlementString(profile, "application-identifier", out var applicationIdentifier) &&
            !TryGetEntitlementString(profile, "com.apple.application-identifier", out applicationIdentifier))
        {
            return null;
        }

        var teamPrefix = profile.ApplicationIdentifierPrefix.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(teamPrefix) &&
            applicationIdentifier.StartsWith($"{teamPrefix}.", StringComparison.OrdinalIgnoreCase))
        {
            return applicationIdentifier[(teamPrefix.Length + 1)..];
        }

        var separator = applicationIdentifier.IndexOf('.');
        return separator >= 0 ? applicationIdentifier[(separator + 1)..] : applicationIdentifier;
    }

    private static bool TryGetEntitlementString(
        ProvisioningProfileInfo profile,
        string key,
        out string value)
    {
        if (profile.Entitlements.TryGetValue(key, out var entitlement) &&
            entitlement is string stringValue &&
            !string.IsNullOrWhiteSpace(stringValue))
        {
            value = stringValue;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
