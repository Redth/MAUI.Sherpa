using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class AppleIdentityStateService : IAppleIdentityStateService
{
    private AppleIdentity? _selectedIdentity;

    public AppleIdentity? SelectedIdentity => _selectedIdentity;

    public event Action? OnSelectionChanged;

    public void SetSelectedIdentity(AppleIdentity? identity)
    {
        if (_selectedIdentity != identity)
        {
            _selectedIdentity = identity;
            OnSelectionChanged?.Invoke();
        }
    }
}
