using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

public sealed class IdentitySelectionStore : IIdentitySelectionStore
{
    private const string AppleIdentityKey = "last_selected_apple_identity_id";
    private const string GoogleIdentityKey = "last_selected_google_identity_id";

    private readonly IPreferences _preferences;

    public IdentitySelectionStore(IPreferences preferences)
    {
        _preferences = preferences;
    }

    public string? GetLastAppleIdentityId() =>
        GetIdentityId(AppleIdentityKey);

    public void SetLastAppleIdentityId(string identityId) =>
        SetIdentityId(AppleIdentityKey, identityId);

    public string? GetLastGoogleIdentityId() =>
        GetIdentityId(GoogleIdentityKey);

    public void SetLastGoogleIdentityId(string identityId) =>
        SetIdentityId(GoogleIdentityKey, identityId);

    private string? GetIdentityId(string key)
    {
        var identityId = _preferences.Get(key, string.Empty);
        return string.IsNullOrWhiteSpace(identityId) ? null : identityId;
    }

    private void SetIdentityId(string key, string identityId)
    {
        if (!string.IsNullOrWhiteSpace(identityId))
            _preferences.Set(key, identityId);
    }
}
