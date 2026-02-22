using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

/// <summary>
/// Default implementation of IToolbarService.
/// Blazor pages call SetItems() on activation; the macOS host observes ToolbarChanged
/// and rebuilds native toolbar items accordingly.
/// </summary>
public class ToolbarService : IToolbarService
{
    private ToolbarAction[] _items = [];

    public IReadOnlyList<ToolbarAction> CurrentItems => _items;

    public event Action? ToolbarChanged;
    public event Action<string>? ToolbarItemClicked;
    public event Action<string>? RouteChanged;

    public void SetItems(params ToolbarAction[] items)
    {
        _items = items;
        ToolbarChanged?.Invoke();
    }

    public void ClearItems()
    {
        _items = [];
        ToolbarChanged?.Invoke();
    }

    public void InvokeToolbarItemClicked(string actionId)
    {
        ToolbarItemClicked?.Invoke(actionId);
    }

    public void NotifyRouteChanged(string route)
    {
        RouteChanged?.Invoke(route);
    }
}
