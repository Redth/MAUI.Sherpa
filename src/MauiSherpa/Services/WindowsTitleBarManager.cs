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

    private static readonly Color BgDark = Color.FromArgb("#1e1a2e");
    private static readonly Color BgControl = Color.FromArgb("#2a2540");
    private static readonly Color BgControlHover = Color.FromArgb("#352f50");
    private static readonly Color BorderColor = Color.FromArgb("#3a3555");
    private static readonly Color TextMuted = Color.FromArgb("#8888aa");
    private static readonly Color Accent = Color.FromArgb("#8b5cf6");

    public WindowsTitleBarManager(IToolbarService toolbarService)
    {
        _toolbarService = toolbarService;
        _toolbarService.ToolbarChanged += OnToolbarChanged;
    }

    public TitleBar CreateTitleBar()
    {
        _titleBar = new TitleBar
        {
            Icon = "sherpalogo.png",
            BackgroundColor = BgDark,
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

        _titleBar.PassthroughElements.Clear();

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
                BackgroundColor = BgControl,
                TextColor = Colors.White,
                PlaceholderColor = TextMuted,
            };
            _searchBar.TextChanged += (s, e) =>
            {
                _toolbarService.NotifySearchTextChanged(e.NewTextValue ?? "");
            };
            _titleBar.Content = _searchBar;
            _titleBar.PassthroughElements.Add(_searchBar);
        }
        else
        {
            _searchBar = null;
            _titleBar.Content = null;
        }

        // TrailingContent: Filters + Action buttons
        var trailing = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(0, 0, 8, 0),
        };

        // Add filter pickers
        foreach (var filter in _toolbarService.CurrentFilters)
        {
            var picker = CreateFilterPicker(filter);
            trailing.Children.Add(picker);
            _titleBar.PassthroughElements.Add(picker);
        }

        // Add separator if we have both filters and actions
        if (_toolbarService.CurrentFilters.Count > 0 && _toolbarService.CurrentItems.Count > 0)
        {
            trailing.Children.Add(new BoxView
            {
                WidthRequest = 1,
                HeightRequest = 24,
                Color = BorderColor,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(2, 0),
            });
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

    private Picker CreateFilterPicker(ToolbarFilter filter)
    {
        var picker = new Picker
        {
            Title = filter.Label,
            HeightRequest = 32,
            MinimumWidthRequest = 100,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = BgControl,
            TextColor = Colors.White,
            TitleColor = TextMuted,
            FontSize = 12,
        };

        foreach (var option in filter.Options)
            picker.Items.Add(option);

        picker.SelectedIndex = filter.SelectedIndex;

        var filterId = filter.Id;
        picker.SelectedIndexChanged += (s, e) =>
        {
            if (picker.SelectedIndex >= 0)
                _toolbarService.NotifyFilterChanged(filterId, picker.SelectedIndex);
        };

        return picker;
    }

    private Button CreateActionButton(ToolbarAction action)
    {
        var icon = MapSfSymbolToText(action.SfSymbol);
        var showLabel = action.IsPrimary && action.SfSymbol != "arrow.clockwise";

        var btn = new Button
        {
            Text = showLabel ? $"{icon}  {action.Label}" : icon,
            FontSize = showLabel ? 13 : 16,
            HeightRequest = 34,
            MinimumWidthRequest = 36,
            Padding = showLabel ? new Thickness(12, 0) : new Thickness(8, 0),
            BackgroundColor = BgControl,
            TextColor = Colors.White,
            BorderColor = BorderColor,
            BorderWidth = 1,
            CornerRadius = 6,
        };
        ToolTipProperties.SetText(btn, action.Label);

        btn.Clicked += (s, e) =>
        {
            _toolbarService.InvokeToolbarItemClicked(action.Id);
        };

        return btn;
    }

    private static string MapSfSymbolToText(string sfSymbol) => sfSymbol switch
    {
        "arrow.clockwise" => "⟳",
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
