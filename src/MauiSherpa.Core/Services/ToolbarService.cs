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
    private ToolbarFilter[] _filters = [];
    private string? _searchPlaceholder;
    private string _searchText = "";
    private readonly HashSet<string> _disabledItems = new();

    public IReadOnlyList<ToolbarAction> CurrentItems => _items;
    public string? SearchPlaceholder => _searchPlaceholder;
    public string SearchText => _searchText;
    public IReadOnlyList<ToolbarFilter> CurrentFilters => _filters;

    public event Action? ToolbarChanged;
    public event Action<string>? ToolbarItemClicked;
    public event Action<string>? RouteChanged;
    public event Action<string>? SearchTextChanged;
    public event Action<string, int>? FilterChanged;

    public void SetItems(params ToolbarAction[] items)
    {
        _items = items;
        ToolbarChanged?.Invoke();
    }

    public void SetSearch(string placeholder)
    {
        _searchPlaceholder = placeholder;
        _searchText = "";
        ToolbarChanged?.Invoke();
    }

    public void SetFilters(params ToolbarFilter[] filters)
    {
        _filters = filters;
        ToolbarChanged?.Invoke();
    }

    public void ClearItems()
    {
        _items = [];
        _filters = [];
        _searchPlaceholder = null;
        _searchText = "";
        _disabledItems.Clear();
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

    public void NotifySearchTextChanged(string text)
    {
        _searchText = text;
        SearchTextChanged?.Invoke(text);
    }

    public void NotifyFilterChanged(string filterId, int selectedIndex)
    {
        // Update the stored filter
        for (int i = 0; i < _filters.Length; i++)
        {
            if (_filters[i].Id == filterId)
            {
                _filters[i] = _filters[i] with { SelectedIndex = selectedIndex };
                break;
            }
        }
        FilterChanged?.Invoke(filterId, selectedIndex);
    }

    public void SetItemEnabled(string actionId, bool enabled)
    {
        if (enabled)
            _disabledItems.Remove(actionId);
        else
            _disabledItems.Add(actionId);
        ToolbarChanged?.Invoke();
    }

    public bool IsItemEnabled(string actionId) => !_disabledItems.Contains(actionId);
}
