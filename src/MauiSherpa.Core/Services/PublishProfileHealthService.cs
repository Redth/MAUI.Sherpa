using System.Collections.Concurrent;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;
using MauiSherpa.Core.Requests.Apple;
using Shiny.Mediator;

namespace MauiSherpa.Core.Services;

public class PublishProfileHealthService : IPublishProfileHealthService
{
    internal const string AppleSource = "apple";
    internal const string CertificatesSource = "apple-certificates";
    internal const string ProfilesSource = "apple-profiles";
    internal const string KeystoresSource = "android-keystores";
    internal const string SecretsSource = "managed-secrets";
    internal const string AppleIdentitiesSource = "apple-identities";
    internal const string GoogleIdentitiesSource = "google-identities";

    static readonly TimeSpan SnapshotLifetime = TimeSpan.FromMinutes(2);

    readonly IMediator _mediator;
    readonly IAppleIdentityStateService _appleIdentityState;
    readonly IAppleIdentityService _appleIdentities;
    readonly IGoogleIdentityService _googleIdentities;
    readonly IManagedSecretsService _managedSecrets;
    readonly ICloudSecretsService _cloudSecrets;
    readonly IPublishProfileService _publishProfiles;
    readonly ILoggingService _logger;
    readonly Func<DateTime> _clock;

    readonly SemaphoreSlim _snapshotGate = new(1, 1);
    PublishProfileHealthSnapshot? _snapshot;
    DateTime _snapshotLoadedAt;

    public PublishProfileHealthService(
        IMediator mediator,
        IAppleIdentityStateService appleIdentityState,
        IAppleIdentityService appleIdentities,
        IGoogleIdentityService googleIdentities,
        IManagedSecretsService managedSecrets,
        ICloudSecretsService cloudSecrets,
        IPublishProfileService publishProfiles,
        ILoggingService logger)
        : this(mediator, appleIdentityState, appleIdentities, googleIdentities, managedSecrets,
            cloudSecrets, publishProfiles, logger, () => DateTime.UtcNow)
    {
    }

    internal PublishProfileHealthService(
        IMediator mediator,
        IAppleIdentityStateService appleIdentityState,
        IAppleIdentityService appleIdentities,
        IGoogleIdentityService googleIdentities,
        IManagedSecretsService managedSecrets,
        ICloudSecretsService cloudSecrets,
        IPublishProfileService publishProfiles,
        ILoggingService logger,
        Func<DateTime> clock)
    {
        _mediator = mediator;
        _appleIdentityState = appleIdentityState;
        _appleIdentities = appleIdentities;
        _googleIdentities = googleIdentities;
        _managedSecrets = managedSecrets;
        _cloudSecrets = cloudSecrets;
        _publishProfiles = publishProfiles;
        _logger = logger;
        _clock = clock;
    }

    // ------------------------------------------------------------------
    // Snapshot
    // ------------------------------------------------------------------

    readonly ConcurrentDictionary<string, string> _recordedReplacements = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, string> _recordedProfileReplacements = new(StringComparer.Ordinal);

    public void Invalidate()
    {
        _snapshot = null;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // The snapshot reads through the mediator caches, so dropping only the snapshot
        // would re-evaluate against the same stale certificates, profiles and keystores.
        var keys = new List<string> { new GetKeystoresRequest().GetKey() };
        if (_appleIdentityState.SelectedIdentity is { } identity)
        {
            keys.Add(new GetCertificatesRequest(identity.Id).GetKey());
            keys.Add(new GetProfilesRequest(identity.Id).GetKey());
        }

        foreach (var key in keys)
        {
            try
            {
                await _mediator.FlushStores(key, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Publish profile health: could not flush cache '{key}': {ex.Message}");
            }
        }

        Invalidate();
    }

    public void RecordCertificateReplacement(string oldSerialNumber, string newSerialNumber)
    {
        var oldSerial = NormalizeSerial(oldSerialNumber);
        var newSerial = NormalizeSerial(newSerialNumber);
        if (oldSerial.Length == 0 || newSerial.Length == 0 || oldSerial == newSerial)
            return;

        _recordedReplacements[oldSerial] = newSerial;
        Invalidate();
    }

    public void RecordProvisioningProfileReplacement(string oldProfileId, string newProfileId)
    {
        if (string.IsNullOrWhiteSpace(oldProfileId) ||
            string.IsNullOrWhiteSpace(newProfileId) ||
            string.Equals(oldProfileId, newProfileId, StringComparison.Ordinal))
        {
            return;
        }

        _recordedProfileReplacements[oldProfileId] = newProfileId;
        Invalidate();
    }

    public async Task<PublishProfileHealthSnapshot> LoadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var unavailable = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        var identity = _appleIdentityState.SelectedIdentity;

        Task<IReadOnlyList<AppleCertificate>?> certificatesTask;
        Task<IReadOnlyList<AppleProfile>?> profilesTask;
        if (identity is null)
        {
            unavailable[AppleSource] = "No Apple identity is selected, so certificates and provisioning profiles weren't checked";
            certificatesTask = Task.FromResult<IReadOnlyList<AppleCertificate>?>(null);
            profilesTask = Task.FromResult<IReadOnlyList<AppleProfile>?>(null);
        }
        else
        {
            certificatesTask = TryLoadAsync(
                CertificatesSource,
                "Apple certificates",
                unavailable,
                async () => (await _mediator.Request(new GetCertificatesRequest(identity.Id), cancellationToken)).Result);
            profilesTask = TryLoadAsync(
                ProfilesSource,
                "Provisioning profiles",
                unavailable,
                async () => (await _mediator.Request(new GetProfilesRequest(identity.Id), cancellationToken)).Result);
        }

        var keystoresTask = TryLoadAsync(
            KeystoresSource,
            "Android keystores",
            unavailable,
            async () => (await _mediator.Request(new GetKeystoresRequest(), cancellationToken)).Result);

        Task<IReadOnlyCollection<string>?> secretsTask;
        if (_cloudSecrets.ActiveProvider is null)
        {
            unavailable[SecretsSource] = "Managed secrets weren't checked because no secrets provider is configured";
            secretsTask = Task.FromResult<IReadOnlyCollection<string>?>(null);
        }
        else
        {
            secretsTask = TryLoadAsync<IReadOnlyCollection<string>>(
                SecretsSource,
                "Managed secrets",
                unavailable,
                async () =>
                {
                    // A partial list would report secrets held by an unreachable provider as missing.
                    var listing = await _managedSecrets.ListWithStatusAsync(cancellationToken);
                    if (listing.UnreadableProviders.Count > 0)
                        throw new InvalidOperationException($"{string.Join(", ", listing.UnreadableProviders)} couldn't be read");

                    return listing.Secrets
                        .Select(secret => secret.Key)
                        .ToHashSet(StringComparer.Ordinal);
                });
        }

        var appleIdentitiesTask = TryLoadAsync<IReadOnlyCollection<string>>(
            AppleIdentitiesSource,
            "Apple identities",
            unavailable,
            async () => (await _appleIdentities.GetIdentitiesAsync())
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal));
        var googleIdentitiesTask = TryLoadAsync<IReadOnlyCollection<string>>(
            GoogleIdentitiesSource,
            "Google identities",
            unavailable,
            async () => (await _googleIdentities.GetIdentitiesAsync())
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal));

        await Task.WhenAll(
            certificatesTask, profilesTask, keystoresTask, secretsTask,
            appleIdentitiesTask, googleIdentitiesTask);

        return new PublishProfileHealthSnapshot(
            identity?.Id,
            identity?.Name,
            await certificatesTask,
            await profilesTask,
            await keystoresTask,
            await secretsTask,
            await appleIdentitiesTask,
            await googleIdentitiesTask,
            unavailable,
            _clock());
    }

    async Task<T?> TryLoadAsync<T>(
        string source,
        string displayName,
        ConcurrentDictionary<string, string> unavailable,
        Func<Task<T>> load)
        where T : class
    {
        try
        {
            return await load();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Publish profile health: could not load {displayName}: {ex.Message}");
            unavailable[source] = $"{displayName} couldn't be checked: {ex.Message}";
            return null;
        }
    }

    async Task<PublishProfileHealthSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        await _snapshotGate.WaitAsync(cancellationToken);
        try
        {
            var current = _snapshot;
            var identityId = _appleIdentityState.SelectedIdentity?.Id;
            if (current is not null &&
                _clock() - _snapshotLoadedAt < SnapshotLifetime &&
                string.Equals(current.AppleIdentityId, identityId, StringComparison.Ordinal))
            {
                return current;
            }

            current = await LoadSnapshotAsync(cancellationToken);
            _snapshot = current;
            _snapshotLoadedAt = _clock();
            return current;
        }
        finally
        {
            _snapshotGate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, PublishProfileHealthReport>> EvaluateAsync(
        IEnumerable<PublishProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var list = profiles.ToList();
        if (list.Count == 0)
            return new Dictionary<string, PublishProfileHealthReport>();

        var snapshot = await GetSnapshotAsync(cancellationToken);
        return list
            .GroupBy(profile => profile.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Evaluate(group.First(), snapshot), StringComparer.Ordinal);
    }

    // ------------------------------------------------------------------
    // Evaluation
    // ------------------------------------------------------------------

    public PublishProfileHealthReport Evaluate(PublishProfile profile, PublishProfileHealthSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(snapshot);

        var issues = new List<PublishProfileHealthIssue>();
        var uncheckedSources = new List<string>();

        void Unchecked(string source, string fallback)
        {
            var reason = snapshot.UnavailableSources.TryGetValue(source, out var value) ? value : fallback;
            if (!uncheckedSources.Contains(reason))
                uncheckedSources.Add(reason);
        }

        for (var index = 0; index < profile.AppleConfigs.Count; index++)
            EvaluateAppleConfig(profile.AppleConfigs[index], index, snapshot, issues, Unchecked);

        var appleIdentities = profile.AppleIdentities ?? [];
        for (var index = 0; index < appleIdentities.Count; index++)
        {
            var item = appleIdentities[index];
            if (string.IsNullOrWhiteSpace(item.IdentityId))
                continue;
            if (snapshot.AppleIdentityIds is null)
            {
                Unchecked(AppleIdentitiesSource, "Apple identities couldn't be checked");
                continue;
            }
            if (!snapshot.AppleIdentityIds.Contains(item.IdentityId))
            {
                issues.Add(new PublishProfileHealthIssue(
                    PublishProfileHealthIssueKind.MissingIdentity,
                    $"Apple identity for '{item.Label}' no longer exists",
                    PublishProfileConfigKind.AppleIdentity,
                    index,
                    item.Label,
                    item.IdentityId));
            }
        }

        for (var index = 0; index < profile.AndroidConfigs.Count; index++)
        {
            var android = profile.AndroidConfigs[index];
            if (string.IsNullOrWhiteSpace(android.KeystoreId))
                continue;
            if (snapshot.Keystores is null)
            {
                Unchecked(KeystoresSource, "Android keystores couldn't be checked");
                continue;
            }
            if (!snapshot.Keystores.Any(keystore => keystore.Id == android.KeystoreId))
            {
                issues.Add(new PublishProfileHealthIssue(
                    PublishProfileHealthIssueKind.MissingKeystore,
                    $"Keystore for '{android.Label}' is not registered on this machine",
                    PublishProfileConfigKind.Android,
                    index,
                    android.Label,
                    android.KeystoreId));
            }
        }

        var googleIdentities = profile.GoogleIdentities ?? [];
        for (var index = 0; index < googleIdentities.Count; index++)
        {
            var item = googleIdentities[index];
            if (string.IsNullOrWhiteSpace(item.IdentityId))
                continue;
            if (snapshot.GoogleIdentityIds is null)
            {
                Unchecked(GoogleIdentitiesSource, "Google identities couldn't be checked");
                continue;
            }
            if (!snapshot.GoogleIdentityIds.Contains(item.IdentityId))
            {
                issues.Add(new PublishProfileHealthIssue(
                    PublishProfileHealthIssueKind.MissingIdentity,
                    $"Google identity for '{item.Label}' no longer exists",
                    PublishProfileConfigKind.GoogleIdentity,
                    index,
                    item.Label,
                    item.IdentityId));
            }
        }

        EvaluateSecrets(profile, snapshot, issues, Unchecked);

        return new PublishProfileHealthReport(profile.Id, issues, uncheckedSources);
    }

    void EvaluateAppleConfig(
        PublishProfileAppleConfig apple,
        int index,
        PublishProfileHealthSnapshot snapshot,
        List<PublishProfileHealthIssue> issues,
        Action<string, string> markUnchecked)
    {
        if (!string.IsNullOrWhiteSpace(apple.IdentityId) && snapshot.AppleIdentityIds is not null &&
            !snapshot.AppleIdentityIds.Contains(apple.IdentityId))
        {
            issues.Add(new PublishProfileHealthIssue(
                PublishProfileHealthIssueKind.MissingIdentity,
                $"Apple identity for '{apple.Label}' no longer exists",
                PublishProfileConfigKind.Apple,
                index,
                apple.Label,
                apple.IdentityId));
            // Its certificates and profiles belong to an account Sherpa can no longer reach.
            return;
        }

        var certificateRefs = GetCertificateReferences(apple);
        var profileRefs = apple.GetProvisioningProfiles();
        if (certificateRefs.Count == 0 && profileRefs.Count == 0)
            return;

        if (snapshot.AppleIdentityId is null)
        {
            markUnchecked(AppleSource, "Certificates and provisioning profiles couldn't be checked");
            return;
        }

        if (!string.IsNullOrWhiteSpace(apple.IdentityId) &&
            !string.Equals(apple.IdentityId, snapshot.AppleIdentityId, StringComparison.Ordinal))
        {
            markUnchecked(
                $"{AppleSource}:{apple.IdentityId}",
                $"Certificates and provisioning profiles in '{apple.Label}' belong to another Apple identity; select it to check them");
            return;
        }

        if (certificateRefs.Count > 0)
        {
            if (snapshot.Certificates is null)
                markUnchecked(CertificatesSource, "Apple certificates couldn't be checked");
            else
                EvaluateCertificates(apple, index, certificateRefs, ResolveProfiles(profileRefs, snapshot), snapshot, issues);
        }

        if (profileRefs.Count > 0)
        {
            if (snapshot.ProvisioningProfiles is null)
                markUnchecked(ProfilesSource, "Provisioning profiles couldn't be checked");
            else
                EvaluateProvisioningProfiles(apple, index, profileRefs, snapshot, issues, markUnchecked);
        }
    }

    /// <summary>
    /// The provisioning profiles a config builds with once its profile fixes are applied, or
    /// null when they can't all be known. Profiles that no longer exist are left out.
    /// </summary>
    IReadOnlyList<AppleProfile>? ResolveProfiles(
        IReadOnlyList<PublishProfileProvisioningProfileSelection> references,
        PublishProfileHealthSnapshot snapshot)
    {
        if (references.Count == 0)
            return [];
        if (snapshot.ProvisioningProfiles is not { } profiles)
            return null;

        var referencedIds = references.Select(reference => reference.Id).ToHashSet(StringComparer.Ordinal);
        var resolved = new List<AppleProfile>();
        foreach (var reference in references)
        {
            var profile = profiles.FirstOrDefault(item => string.Equals(item.Id, reference.Id, StringComparison.Ordinal))
                ?? FindRegenerated(reference, profiles, referencedIds, snapshot.Now);
            if (profile is not null)
                resolved.Add(profile);
            else if (IsTruncated(profiles))
                return null;
        }

        return resolved;
    }

    static bool IsTruncated(IReadOnlyList<AppleProfile> profiles) =>
        profiles.Count >= IAppleConnectService.ProfileListLimit;

    /// <summary>
    /// Whether <paramref name="candidateId"/> can replace a signing certificate without
    /// breaking the config: some profile must authorize it, and every profile must still
    /// authorize one of the config's signing certificates afterwards.
    /// </summary>
    internal static bool ProfilesAuthorize(
        IReadOnlyList<AppleProfile>? profiles,
        string candidateId,
        IEnumerable<string> otherCertificateIds)
    {
        if (profiles is null)
            return false;
        if (profiles.Count == 0)
            return true;

        var resulting = otherCertificateIds.Append(candidateId).ToHashSet(StringComparer.Ordinal);
        return profiles.Any(profile => profile.CertificateIds?.Contains(candidateId, StringComparer.Ordinal) == true) &&
            profiles.All(profile => profile.CertificateIds?.Any(resulting.Contains) == true);
    }

    sealed record CertificateReference(string SerialNumber, string Name, bool IsInstaller);

    static IReadOnlyList<CertificateReference> GetCertificateReferences(PublishProfileAppleConfig apple)
    {
        var references = apple.GetSigningCertificates()
            .Where(selection => !string.IsNullOrWhiteSpace(selection.SerialNumber))
            .Select(selection => new CertificateReference(selection.SerialNumber, selection.Name, false))
            .ToList();
        if (!string.IsNullOrWhiteSpace(apple.InstallerCertSerialNumber))
        {
            references.Add(new CertificateReference(
                apple.InstallerCertSerialNumber,
                apple.InstallerCertSerialNumber,
                true));
        }

        return references
            .GroupBy(reference => NormalizeSerial(reference.SerialNumber), StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    // A certificate this close to expiry is treated as due for renewal, matching the
    // "Expiring Soon" window on the Certificates page.
    static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(30);

    void EvaluateCertificates(
        PublishProfileAppleConfig apple,
        int index,
        IReadOnlyList<CertificateReference> references,
        IReadOnlyList<AppleProfile>? provisioningProfiles,
        PublishProfileHealthSnapshot snapshot,
        List<PublishProfileHealthIssue> issues)
    {
        var certificates = snapshot.Certificates!;
        var referencedSerials = references
            .Select(reference => NormalizeSerial(reference.SerialNumber))
            .ToHashSet(StringComparer.Ordinal);

        bool IsUsable(AppleCertificate item) =>
            item.ExpirationDate > snapshot.Now &&
            !referencedSerials.Contains(NormalizeSerial(item.SerialNumber));

        AppleCertificate? Recorded(string serialNumber) =>
            _recordedReplacements.TryGetValue(NormalizeSerial(serialNumber), out var replacementSerial)
                ? certificates.FirstOrDefault(item => NormalizeSerial(item.SerialNumber) == replacementSerial && IsUsable(item))
                : null;

        AppleCertificate? Find(string serialNumber) =>
            certificates.FirstOrDefault(item => NormalizeSerial(item.SerialNumber) == NormalizeSerial(serialNumber));

        // Installer certificates sign the package, not the app, so no provisioning profile lists them.
        bool Fits(CertificateReference reference, AppleCertificate replacement) =>
            reference.IsInstaller ||
            ProfilesAuthorize(
                provisioningProfiles,
                replacement.Id,
                references
                    .Where(other => !other.IsInstaller && !ReferenceEquals(other, reference))
                    .Select(other => Find(other.SerialNumber)?.Id)
                    .OfType<string>());

        const string RegenerateFirst = "; regenerate its provisioning profiles with that certificate to update it";

        foreach (var reference in references)
        {
            var certificate = Find(reference.SerialNumber);
            var displayName = certificate?.Name ?? DisplayName(reference);

            if (certificate is null)
            {
                // A revoked certificate disappears from App Store Connect. Its renewal is
                // either recorded by the renewal flow or carries the same common name.
                var replacement = Recorded(reference.SerialNumber)
                    ?? (HasRealName(reference)
                        ? certificates
                            .Where(item => string.Equals(item.Name, reference.Name, StringComparison.OrdinalIgnoreCase))
                            .Where(IsUsable)
                            .OrderByDescending(item => item.ExpirationDate)
                            .FirstOrDefault()
                        : null);
                var where = reference.IsInstaller
                    ? "isn't in App Store Connect or this Mac's Keychain"
                    : "no longer exists in App Store Connect";
                var replacementFits = replacement is not null && Fits(reference, replacement);
                issues.Add(new PublishProfileHealthIssue(
                    PublishProfileHealthIssueKind.MissingCertificate,
                    replacement is null
                        ? $"{CertificateNoun(reference)} '{displayName}' {where}"
                        : $"{CertificateNoun(reference)} '{displayName}' {where}; '{replacement.Name}' (expires {replacement.ExpirationDate:MMM d, yyyy}) can replace it{(replacementFits ? "" : RegenerateFirst)}",
                    PublishProfileConfigKind.Apple,
                    index,
                    apple.Label,
                    reference.SerialNumber,
                    replacementFits ? replacement!.SerialNumber : null,
                    replacementFits ? replacement!.Name : null));
                continue;
            }

            var sameType = certificates
                .Where(item => !ReferenceEquals(item, certificate))
                .Where(item => string.Equals(item.CertificateType, certificate.CertificateType, StringComparison.OrdinalIgnoreCase))
                .Where(item => item.IsLocalOnly == certificate.IsLocalOnly)
                .Where(item => item.ExpirationDate > certificate.ExpirationDate)
                .Where(IsUsable)
                .OrderByDescending(item => item.ExpirationDate)
                .ToList();
            var sameName = sameType.FirstOrDefault(item =>
                string.Equals(item.Name, certificate.Name, StringComparison.OrdinalIgnoreCase));
            var recorded = Recorded(certificate.SerialNumber);

            if (certificate.ExpirationDate <= snapshot.Now)
            {
                var newer = recorded ?? sameName ?? sameType.FirstOrDefault();
                var newerFits = newer is not null && Fits(reference, newer);
                issues.Add(new PublishProfileHealthIssue(
                    PublishProfileHealthIssueKind.ExpiredCertificate,
                    newer is null
                        ? $"{CertificateNoun(reference)} '{certificate.Name}' expired on {certificate.ExpirationDate:MMM d, yyyy}"
                        : $"{CertificateNoun(reference)} '{certificate.Name}' expired on {certificate.ExpirationDate:MMM d, yyyy}; '{newer.Name}' expires {newer.ExpirationDate:MMM d, yyyy}{(newerFits ? "" : RegenerateFirst)}",
                    PublishProfileConfigKind.Apple,
                    index,
                    apple.Label,
                    reference.SerialNumber,
                    newerFits ? newer!.SerialNumber : null,
                    newerFits ? newer!.Name : null));
                continue;
            }

            // Teams often hold several valid certificates of one type (development
            // certificates are per person), so a newer one alone isn't a replacement.
            // It is when the renewal flow recorded it, it shares the common name, or the
            // referenced certificate is about to expire.
            var replacedBy = recorded
                ?? sameName
                ?? (certificate.ExpirationDate - snapshot.Now <= RenewalWindow ? sameType.FirstOrDefault() : null);
            if (replacedBy is not null)
            {
                var replacedByFits = Fits(reference, replacedBy);
                issues.Add(new PublishProfileHealthIssue(
                    PublishProfileHealthIssueKind.CertificateReplaced,
                    $"'{certificate.Name}' (expires {certificate.ExpirationDate:MMM d, yyyy}) has a newer {CertificateNoun(reference).ToLowerInvariant()} of the same type: '{replacedBy.Name}' (expires {replacedBy.ExpirationDate:MMM d, yyyy}){(replacedByFits ? "" : RegenerateFirst)}",
                    PublishProfileConfigKind.Apple,
                    index,
                    apple.Label,
                    reference.SerialNumber,
                    replacedByFits ? replacedBy.SerialNumber : null,
                    replacedByFits ? replacedBy.Name : null));
            }
        }
    }

    void EvaluateProvisioningProfiles(
        PublishProfileAppleConfig apple,
        int index,
        IReadOnlyList<PublishProfileProvisioningProfileSelection> references,
        PublishProfileHealthSnapshot snapshot,
        List<PublishProfileHealthIssue> issues,
        Action<string, string> markUnchecked)
    {
        var profiles = snapshot.ProvisioningProfiles!;
        var referencedIds = references.Select(reference => reference.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var reference in references)
        {
            var name = HasRealName(reference) ? reference.Name : "Provisioning profile";
            var profile = profiles.FirstOrDefault(item => string.Equals(item.Id, reference.Id, StringComparison.Ordinal));

            if (profile is not null)
            {
                if (!string.IsNullOrWhiteSpace(reference.Uuid) &&
                    !string.IsNullOrWhiteSpace(profile.Uuid) &&
                    !string.Equals(reference.Uuid, profile.Uuid, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(UpdatedIssue(apple, index, reference, profile,
                        $"'{profile.Name}' was regenerated with a new UUID"));
                }
                else if (IsExpired(profile, snapshot.Now))
                {
                    issues.Add(new PublishProfileHealthIssue(
                        PublishProfileHealthIssueKind.ExpiredProvisioningProfile,
                        $"'{profile.Name}' expired on {profile.ExpirationDate:MMM d, yyyy}",
                        PublishProfileConfigKind.Apple,
                        index,
                        apple.Label,
                        reference.Id));
                }
                continue;
            }

            var regenerated = FindRegenerated(reference, profiles, referencedIds, snapshot.Now);
            if (regenerated is not null)
            {
                issues.Add(UpdatedIssue(apple, index, reference, regenerated,
                    $"'{regenerated.Name}' was regenerated; this publish profile still points to the old copy"));
                continue;
            }

            // Profiles past the first page weren't read, so absence proves nothing.
            if (IsTruncated(profiles))
            {
                markUnchecked(
                    ProfilesSource,
                    $"Only the first {IAppleConnectService.ProfileListLimit} provisioning profiles could be read, so missing ones weren't checked");
                continue;
            }

            issues.Add(new PublishProfileHealthIssue(
                PublishProfileHealthIssueKind.MissingProvisioningProfile,
                $"{(HasRealName(reference) ? $"'{name}'" : "A provisioning profile")} no longer exists in App Store Connect",
                PublishProfileConfigKind.Apple,
                index,
                apple.Label,
                reference.Id));
        }
    }

    AppleProfile? FindRegenerated(
        PublishProfileProvisioningProfileSelection reference,
        IReadOnlyList<AppleProfile> profiles,
        IReadOnlySet<string> referencedIds,
        DateTime now)
    {
        // Renewal records old-to-new IDs. That's the only link for legacy selections, which
        // store the ID in the name slot and have no UUID. Follow repeated renewals to the end.
        var recordedId = reference.Id;
        var visited = new HashSet<string>(StringComparer.Ordinal) { recordedId };
        while (_recordedProfileReplacements.TryGetValue(recordedId, out var nextId) && visited.Add(nextId))
            recordedId = nextId;
        if (!string.Equals(recordedId, reference.Id, StringComparison.Ordinal) &&
            profiles.FirstOrDefault(item => string.Equals(item.Id, recordedId, StringComparison.Ordinal)) is { } recorded)
        {
            return recorded;
        }

        return (!string.IsNullOrWhiteSpace(reference.Uuid)
                ? profiles.FirstOrDefault(item => string.Equals(item.Uuid, reference.Uuid, StringComparison.OrdinalIgnoreCase))
                : null)
            ?? (HasRealName(reference)
                ? profiles
                    .Where(item => string.Equals(item.Name, reference.Name, StringComparison.OrdinalIgnoreCase))
                    .Where(item => !referencedIds.Contains(item.Id))
                    .OrderBy(item => IsExpired(item, now) ? 1 : 0)
                    .ThenByDescending(item => item.ExpirationDate)
                    .FirstOrDefault()
                : null);
    }

    static PublishProfileHealthIssue UpdatedIssue(
        PublishProfileAppleConfig apple,
        int index,
        PublishProfileProvisioningProfileSelection reference,
        AppleProfile replacement,
        string label) => new(
            PublishProfileHealthIssueKind.ProvisioningProfileUpdated,
            label,
            PublishProfileConfigKind.Apple,
            index,
            apple.Label,
            reference.Id,
            replacement.Id,
            replacement.Name,
            replacement.Uuid);

    static void EvaluateSecrets(
        PublishProfile profile,
        PublishProfileHealthSnapshot snapshot,
        List<PublishProfileHealthIssue> issues,
        Action<string, string> markUnchecked)
    {
        var references = new List<(string Key, PublishProfileConfigKind Kind, int? Index, string Label)>();
        foreach (var mapping in profile.SecretMappings)
        {
            if (!string.IsNullOrWhiteSpace(mapping.SourceKey))
                references.Add((mapping.SourceKey, PublishProfileConfigKind.Secrets, null, "Secrets"));
        }

        for (var index = 0; index < profile.AppleConfigs.Count; index++)
        {
            var apple = profile.AppleConfigs[index];
            if (!apple.IncludeNotarization)
                continue;

            void AddNotarization(string? key, string? manualValue)
            {
                if (!string.IsNullOrWhiteSpace(key) && string.IsNullOrEmpty(manualValue))
                    references.Add((key, PublishProfileConfigKind.Apple, index, apple.Label));
            }

            AddNotarization(apple.NotarizationAppleIdSecretKey, apple.NotarizationAppleIdManualValue);
            AddNotarization(apple.NotarizationPasswordSecretKey, apple.NotarizationPasswordManualValue);
            AddNotarization(apple.NotarizationTeamIdSecretKey, apple.NotarizationTeamIdManualValue);
        }

        if (references.Count == 0)
            return;

        // An empty list with secrets referenced is far more likely a provider that failed
        // to enumerate than every secret having been deleted.
        if (snapshot.ManagedSecretKeys is null || snapshot.ManagedSecretKeys.Count == 0)
        {
            markUnchecked(SecretsSource, "Managed secrets couldn't be checked");
            return;
        }

        foreach (var group in references.GroupBy(reference => (reference.Key, reference.Kind, reference.Index)))
        {
            var reference = group.First();
            if (snapshot.ManagedSecretKeys.Contains(reference.Key))
                continue;

            issues.Add(new PublishProfileHealthIssue(
                PublishProfileHealthIssueKind.MissingSecret,
                reference.Kind == PublishProfileConfigKind.Secrets
                    ? $"Secret '{reference.Key}' doesn't exist"
                    : $"Notarization secret '{reference.Key}' in '{reference.Label}' doesn't exist",
                reference.Kind,
                reference.Index,
                reference.Label,
                reference.Key));
        }
    }

    static bool IsExpired(AppleProfile profile, DateTime now) =>
        profile.ExpirationDate <= now ||
        string.Equals(profile.State, "INVALID", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(profile.State, "EXPIRED", StringComparison.OrdinalIgnoreCase);

    static string CertificateNoun(CertificateReference reference) =>
        reference.IsInstaller ? "Installer certificate" : "Certificate";

    static string DisplayName(CertificateReference reference) =>
        HasRealName(reference) ? reference.Name : $"serial {reference.SerialNumber}";

    // Legacy selections store the serial or ID in the name slot.
    static bool HasRealName(CertificateReference reference) =>
        !string.IsNullOrWhiteSpace(reference.Name) &&
        NormalizeSerial(reference.Name) != NormalizeSerial(reference.SerialNumber);

    static bool HasRealName(PublishProfileProvisioningProfileSelection reference) =>
        !string.IsNullOrWhiteSpace(reference.Name) &&
        !string.Equals(reference.Name, reference.Id, StringComparison.Ordinal);

    internal static string NormalizeSerial(string? serialNumber) =>
        new string((serialNumber ?? "")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray())
        .TrimStart('0');

    // ------------------------------------------------------------------
    // Fixes
    // ------------------------------------------------------------------

    sealed record CertificateTarget(string SerialNumber, string Name);
    sealed record ProfileTarget(string Id, string Uuid, string Name);

    public PublishProfile ApplyFixes(PublishProfile profile, IEnumerable<PublishProfileHealthIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(issues);

        var fixes = issues
            .Where(issue => issue.CanFix && issue.ConfigKind == PublishProfileConfigKind.Apple && issue.ConfigIndex is not null)
            .ToList();
        if (fixes.Count == 0)
            return profile;

        var changed = false;
        var configs = new List<PublishProfileAppleConfig>(profile.AppleConfigs.Count);
        for (var index = 0; index < profile.AppleConfigs.Count; index++)
        {
            var certificateMap = new Dictionary<string, CertificateTarget>(StringComparer.Ordinal);
            var profileMap = new Dictionary<string, ProfileTarget>(StringComparer.Ordinal);
            foreach (var fix in fixes.Where(fix => fix.ConfigIndex == index))
            {
                switch (fix.Kind)
                {
                    case PublishProfileHealthIssueKind.MissingCertificate:
                    case PublishProfileHealthIssueKind.ExpiredCertificate:
                    case PublishProfileHealthIssueKind.CertificateReplaced:
                        certificateMap[NormalizeSerial(fix.Reference)] = new CertificateTarget(
                            fix.ReplacementReference!,
                            fix.ReplacementName ?? fix.ReplacementReference!);
                        break;
                    case PublishProfileHealthIssueKind.ProvisioningProfileUpdated:
                        profileMap[fix.Reference] = new ProfileTarget(
                            fix.ReplacementReference!,
                            fix.ReplacementUuid ?? "",
                            fix.ReplacementName ?? fix.ReplacementReference!);
                        break;
                }
            }

            var updated = RepointConfig(profile.AppleConfigs[index], certificateMap, profileMap, out var configChanged);
            changed |= configChanged;
            configs.Add(updated);
        }

        return changed
            ? profile with { AppleConfigs = configs, UpdatedAt = _clock() }
            : profile;
    }

    public PublishProfile Repoint(
        PublishProfile profile,
        IReadOnlyDictionary<string, AppleCertificate> certificateReplacements,
        IReadOnlyDictionary<string, AppleProfile> profileReplacements,
        IReadOnlyList<AppleProfile>? knownProfiles,
        IReadOnlyList<AppleCertificate> knownCertificates)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(certificateReplacements);
        ArgumentNullException.ThrowIfNull(profileReplacements);
        ArgumentNullException.ThrowIfNull(knownCertificates);

        var replacements = certificateReplacements
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .GroupBy(pair => NormalizeSerial(pair.Key), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);
        var profileMap = profileReplacements
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => new ProfileTarget(pair.Value.Id, pair.Value.Uuid, pair.Value.Name),
                StringComparer.Ordinal);

        var changed = false;
        var configs = profile.AppleConfigs
            .Select(config =>
            {
                var certificateMap = FittingReplacements(config, replacements, profileReplacements, knownProfiles, knownCertificates);
                var updated = RepointConfig(config, certificateMap, profileMap, out var configChanged);
                changed |= configChanged;
                return updated;
            })
            .ToList();

        return changed
            ? profile with { AppleConfigs = configs, UpdatedAt = _clock() }
            : profile;
    }

    /// <summary>
    /// The certificate replacements a config can take: a signing certificate only moves when
    /// the config's provisioning profiles, after their own replacements, authorize the new one.
    /// </summary>
    static Dictionary<string, CertificateTarget> FittingReplacements(
        PublishProfileAppleConfig config,
        IReadOnlyDictionary<string, AppleCertificate> replacements,
        IReadOnlyDictionary<string, AppleProfile> profileReplacements,
        IReadOnlyList<AppleProfile>? knownProfiles,
        IReadOnlyList<AppleCertificate> knownCertificates)
    {
        var map = new Dictionary<string, CertificateTarget>(StringComparer.Ordinal);
        if (replacements.Count == 0)
            return map;

        List<AppleProfile>? profiles = [];
        foreach (var reference in config.GetProvisioningProfiles())
        {
            var resolved = profileReplacements.GetValueOrDefault(reference.Id)
                ?? knownProfiles?.FirstOrDefault(item => string.Equals(item.Id, reference.Id, StringComparison.Ordinal));
            if (resolved is not null)
                profiles?.Add(resolved);
            else if (knownProfiles is null || IsTruncated(knownProfiles))
                profiles = null;
        }

        string? IdAfterReplacement(string serialNumber) =>
            replacements.TryGetValue(NormalizeSerial(serialNumber), out var replacement)
                ? replacement.Id
                : knownCertificates.FirstOrDefault(item => NormalizeSerial(item.SerialNumber) == NormalizeSerial(serialNumber))?.Id;

        var signing = config.GetSigningCertificates()
            .Select(selection => selection.SerialNumber)
            .Where(serial => !string.IsNullOrWhiteSpace(serial))
            .ToList();
        var installer = NormalizeSerial(config.InstallerCertSerialNumber);

        foreach (var (serial, replacement) in replacements)
        {
            var target = new CertificateTarget(replacement.SerialNumber, replacement.Name);
            if (serial == installer)
            {
                map[serial] = target;
                continue;
            }

            var others = signing
                .Where(other => NormalizeSerial(other) != serial)
                .Select(IdAfterReplacement)
                .OfType<string>();
            if (ProfilesAuthorize(profiles, replacement.Id, others))
                map[serial] = target;
        }

        return map;
    }

    static PublishProfileAppleConfig RepointConfig(
        PublishProfileAppleConfig config,
        IReadOnlyDictionary<string, CertificateTarget> certificateMap,
        IReadOnlyDictionary<string, ProfileTarget> profileMap,
        out bool changed)
    {
        changed = false;
        if (certificateMap.Count == 0 && profileMap.Count == 0)
            return config;

        var anyChange = false;

        string? MapSerial(string? serial)
        {
            if (string.IsNullOrWhiteSpace(serial) ||
                !certificateMap.TryGetValue(NormalizeSerial(serial), out var target))
            {
                return serial;
            }
            anyChange = true;
            return target.SerialNumber;
        }

        var signing = config.SigningCertificates
            .Select(selection =>
                certificateMap.TryGetValue(NormalizeSerial(selection.SerialNumber), out var target)
                    ? MarkChanged(new PublishProfileCertificateSelection(target.SerialNumber, target.Name))
                    : selection)
            .GroupBy(selection => NormalizeSerial(selection.SerialNumber), StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        var certificateSerial = MapSerial(config.CertificateSerialNumber);
        var installerSerial = MapSerial(config.InstallerCertSerialNumber);

        var provisioning = config.ProvisioningProfiles
            .Select(selection =>
                profileMap.TryGetValue(selection.Id, out var target)
                    ? MarkChanged(new PublishProfileProvisioningProfileSelection(target.Id, target.Uuid, target.Name))
                    : selection)
            .GroupBy(selection => selection.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var profileId = config.ProfileId;
        var profileUuid = config.ProfileUuid;
        if (!string.IsNullOrWhiteSpace(profileId) && profileMap.TryGetValue(profileId, out var legacyTarget))
        {
            profileId = legacyTarget.Id;
            profileUuid = legacyTarget.Uuid;
            anyChange = true;
        }

        changed = anyChange;
        if (!anyChange)
            return config;

        return config with
        {
            CertificateSerialNumber = certificateSerial,
            InstallerCertSerialNumber = installerSerial,
            ProfileId = profileId,
            ProfileUuid = profileUuid,
            SigningCertificates = signing,
            ProvisioningProfiles = provisioning,
        };

        T MarkChanged<T>(T value)
        {
            anyChange = true;
            return value;
        }
    }

    public async Task<PublishProfile?> ApplyFixesAsync(
        PublishProfile profile,
        IEnumerable<PublishProfileHealthIssue> issues,
        CancellationToken cancellationToken = default)
    {
        var updated = ApplyFixes(profile, issues);
        if (ReferenceEquals(updated, profile))
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        await _publishProfiles.SaveProfileAsync(updated);
        return updated;
    }
}
