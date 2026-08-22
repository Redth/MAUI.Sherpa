using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class GoogleIdentityStateService : IGoogleIdentityStateService
{
    private readonly IIdentitySelectionStore? _selectionStore;
    private GoogleIdentity? _selectedIdentity;

    public GoogleIdentityStateService(IIdentitySelectionStore? selectionStore = null)
    {
        _selectionStore = selectionStore;
        LastSelectedIdentityId = selectionStore?.GetLastGoogleIdentityId();
    }

    public GoogleIdentity? SelectedIdentity => _selectedIdentity;
    public string? LastSelectedIdentityId { get; private set; }

    public event Action? OnSelectionChanged;

    public void SetSelectedIdentity(GoogleIdentity? identity)
    {
        if (identity is not null && !string.Equals(LastSelectedIdentityId, identity.Id, StringComparison.Ordinal))
        {
            LastSelectedIdentityId = identity.Id;
            _selectionStore?.SetLastGoogleIdentityId(identity.Id);
        }

        if (_selectedIdentity != identity)
        {
            _selectedIdentity = identity;
            OnSelectionChanged?.Invoke();
        }
    }
}
