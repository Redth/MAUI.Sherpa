using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class AppleIdentityStateService : IAppleIdentityStateService
{
    private readonly IIdentitySelectionStore? _selectionStore;
    private AppleIdentity? _selectedIdentity;

    public AppleIdentityStateService(IIdentitySelectionStore? selectionStore = null)
    {
        _selectionStore = selectionStore;
        LastSelectedIdentityId = selectionStore?.GetLastAppleIdentityId();
    }

    public AppleIdentity? SelectedIdentity => _selectedIdentity;
    public string? LastSelectedIdentityId { get; private set; }

    public event Action? OnSelectionChanged;

    public void SetSelectedIdentity(AppleIdentity? identity)
    {
        if (identity is not null && !string.Equals(LastSelectedIdentityId, identity.Id, StringComparison.Ordinal))
        {
            LastSelectedIdentityId = identity.Id;
            _selectionStore?.SetLastAppleIdentityId(identity.Id);
        }

        if (!EqualityComparer<AppleIdentity?>.Default.Equals(_selectedIdentity, identity))
        {
            _selectedIdentity = identity;
            OnSelectionChanged?.Invoke();
        }
    }
}
