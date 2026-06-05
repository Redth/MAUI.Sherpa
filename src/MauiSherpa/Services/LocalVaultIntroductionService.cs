using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

public sealed class LocalVaultIntroductionService : ILocalVaultIntroductionService
{
    private const string VersionKey = "local_vault_intro_version";
    private const string DecisionKey = "local_vault_intro_decision";
    private const string DecidedAtKey = "local_vault_intro_decided_at_utc";

    private readonly IPreferences _preferences;

    public LocalVaultIntroductionService(IPreferences preferences)
    {
        _preferences = preferences;
    }

    public event Action? StateChanged;

    public LocalVaultIntroductionState GetState()
    {
        var version = _preferences.Get(VersionKey, 0);
        var decisionText = _preferences.Get(DecisionKey, string.Empty);
        var decidedAtText = _preferences.Get(DecidedAtKey, string.Empty);

        if (!Enum.TryParse<LocalVaultIntroductionDecision>(decisionText, out var decision))
            decision = LocalVaultIntroductionDecision.NotSet;

        DateTime? decidedAt = null;
        if (DateTime.TryParse(decidedAtText, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            decidedAt = parsed;

        if (version != LocalVaultIntroductionState.CurrentVersion)
            return LocalVaultIntroductionState.NotShown;

        return new LocalVaultIntroductionState(version, decision, decidedAt);
    }

    public Task MarkEnabledAsync(CancellationToken cancellationToken = default)
    {
        SetDecision(LocalVaultIntroductionDecision.Enabled);
        return Task.CompletedTask;
    }

    public Task MarkDeclinedAsync(CancellationToken cancellationToken = default)
    {
        SetDecision(LocalVaultIntroductionDecision.Declined);
        return Task.CompletedTask;
    }

    private void SetDecision(LocalVaultIntroductionDecision decision)
    {
        _preferences.Set(VersionKey, LocalVaultIntroductionState.CurrentVersion);
        _preferences.Set(DecisionKey, decision.ToString());
        _preferences.Set(DecidedAtKey, DateTime.UtcNow.ToString("O"));
        StateChanged?.Invoke();
    }
}
