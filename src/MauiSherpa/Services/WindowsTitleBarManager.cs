using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

/// <summary>
/// Manages the Windows TitleBar, rebuilding its content when the toolbar service state changes.
/// Subscribes to IToolbarService.ToolbarChanged and maps ToolbarActions/Filters/Search
/// to native MAUI TitleBar Content and TrailingContent controls.
/// </summary>
public class WindowsTitleBarManager
{
    private readonly IToolbarService _toolbarService;
    private TitleBar? _titleBar;
    private SearchBar? _searchBar;

    public WindowsTitleBarManager(IToolbarService toolbarService)
    {
        _toolbarService = toolbarService;
        _toolbarService.ToolbarChanged += OnToolbarChanged;
    }

    public TitleBar CreateTitleBar()
    {
        _titleBar = new TitleBar
        {
            Title = "MAUI Sherpa",
            BackgroundColor = Color.FromArgb("#1e1a2e"),
            ForegroundColor = Colors.White,
            HeightRequest = 48,
        };

        RebuildContent();
        return _titleBar;
    }

    private void OnToolbarChanged()
    {
        if (_titleBar == null) return;

        if (Application.Current?.Dispatcher is { } dispatcher)
            dispatcher.Dispatch(RebuildContent);
        else
            RebuildContent();
    }

    private void RebuildContent()
    {
        if (_titleBar == null) return;

        // Content: Search bar (centered)
        if (!string.IsNullOrEmpty(_toolbarService.SearchPlaceholder))
        {
            _searchBar = new SearchBar
            {
                Placeholder = _toolbarService.SearchPlaceholder,
                Text = _toolbarService.SearchText,
                MaximumWidthRequest = 350,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Center,
                HeightRequest = 32,
                BackgroundColor = Color.FromArgb("#2a2540"),
                TextColor = Colors.White,
                PlaceholderColor = Color.FromArgb("#8888aa"),
            };
            _searchBar.TextChanged += (s, e) =>
            {
                _toolbarService.NotifySearchTextChanged(e.NewTextValue ?? "");
            };
            _titleBar.Content = _searchBar;

            // SearchBar needs passthrough so it receives input instead of window drag
            _titleBar.PassthroughElements.Clear();
            _titleBar.PassthroughElements.Add(_searchBar);
        }
        else
        {
            _searchBar = null;
            _titleBar.Content = null;
            _titleBar.PassthroughElements.Clear();
        }

        // TrailingContent: Filters + Action buttons
        var trailing = new HorizontalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
        };

        // Add filter dropdowns
        foreach (var filter in _toolbarService.CurrentFilters)
        {
            var filterBtn = CreateFilterButton(filter);
            trailing.Children.Add(filterBtn);
            _titleBar.PassthroughElements.Add(filterBtn);
        }

        // Add action buttons
        foreach (var action in _toolbarService.CurrentItems)
        {
            var btn = CreateActionButton(action);
            trailing.Children.Add(btn);
            _titleBar.PassthroughElements.Add(btn);
        }

        if (trailing.Children.Count > 0)
        {
            _titleBar.TrailingContent = trailing;
            _titleBar.PassthroughElements.Add(trailing);
        }
        else
        {
            _titleBar.TrailingContent = null;
        }
    }

    private Button CreateFilterButton(ToolbarFilter filter)
    {
        var selectedLabel = filter.SelectedIndex >= 0 && filter.SelectedIndex < filter.Options.Length
            ? filter.Options[filter.SelectedIndex]
            : filter.Label;

        var btn = new Button
        {
            Text = selectedLabel == filter.Options[0] ? filter.Label : selectedLabel,
            FontSize = 12,
            HeightRequest = 30,
            Padding = new Thickness(10, 0),
            BackgroundColor = Color.FromArgb("#2a2540"),
            TextColor = Colors.White,
            BorderColor = Color.FromArgb("#3a3555"),
            BorderWidth = 1,
            CornerRadius = 4,
        };

        var menuFlyout = new MenuFlyout();
        for (int i = 0; i < filter.Options.Length; i++)
        {
            var index = i;
            var option = filter.Options[i];
            var item = new MenuFlyoutItem
            {
                Text = option,
            };
            item.Clicked += (s, e) =>
            {
                _toolbarService.NotifyFilterChanged(filter.Id, index);
                // Update button text
                btn.Text = index == 0 ? filter.Label : option;
            };
            menuFlyout.Add(item);
        }

        FlyoutBase.SetContextFlyout(btn, menuFlyout);

        return btn;
    }

    private Button CreateActionButton(ToolbarAction action)
    {
        var icon = MapSfSymbolToText(action.SfSymbol);

        var btn = new Button
        {
            Text = icon,
            FontSize = 14,
            HeightRequest = 30,
            WidthRequest = action.IsPrimary ? -1 : 30,
            MinimumWidthRequest = 30,
            Padding = action.IsPrimary ? new Thickness(10, 0) : new Thickness(0),
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.White,
            BorderWidth = 0,
            CornerRadius = 4,
        };
        ToolTipProperties.SetText(btn, action.Label);

        // For primary actions, show label too
        if (action.IsPrimary && !string.IsNullOrEmpty(action.Label) && action.Label != "Refresh")
        {
            btn.Text = $"{icon} {action.Label}";
        }

        btn.Clicked += (s, e) =>
        {
            _toolbarService.InvokeToolbarItemClicked(action.Id);
        };

        return btn;
    }

    private static string MapSfSymbolToText(string sfSymbol) => sfSymbol switch
    {
        "arrow.clockwise" => "↻",
        "plus" => "+",
        "plus.circle" => "+",
        "square.and.arrow.down" => "↓",
        "square.and.arrow.up" => "↑",
        "trash" => "🗑",
        "pencil" => "✏",
        "xmark" => "✕",
        "checkmark" => "✓",
        "magnifyingglass" => "🔍",
        "gear" => "⚙",
        "doc.on.doc" => "📋",
        "arrow.triangle.2.circlepath" => "⟳",
        _ => "•",
    };
}
