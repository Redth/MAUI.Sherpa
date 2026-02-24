using MauiSherpa.Core.Interfaces;
using MauiIcons.Fluent;
using MauiIcons.FontAwesome.Brand;
using System.ComponentModel;
using System.Reflection;

namespace MauiSherpa.Services;

/// <summary>
/// Manages the Windows TitleBar, rebuilding its content when the toolbar service state changes.
/// Subscribes to IToolbarService.ToolbarChanged and maps ToolbarActions/Filters/Search
/// to native MAUI TitleBar Content and TrailingContent controls.
/// </summary>
public class WindowsTitleBarManager
{
    private readonly IToolbarService _toolbarService;
    private readonly IAppleIdentityService _appleIdentityService;
    private readonly IAppleIdentityStateService _appleIdentityState;
    private readonly IGoogleIdentityService _googleIdentityService;
    private readonly IGoogleIdentityStateService _googleIdentityState;
    private readonly ICopilotContextService _copilotContext;
    private readonly IServiceProvider _serviceProvider;
    private TitleBar? _titleBar;
    private SearchBar? _searchBar;
    private string _currentRoute = "";

    private static readonly Color BgDark = Color.FromArgb("#1e1a2e");
    private static readonly Color BgControl = Color.FromArgb("#2a2540");
    private static readonly Color BgControlHover = Color.FromArgb("#352f50");
    private static readonly Color BorderColor = Color.FromArgb("#3a3555");
    private static readonly Color TextMuted = Color.FromArgb("#8888aa");
    private static readonly Color Accent = Color.FromArgb("#8b5cf6");

    private static readonly HashSet<string> AppleRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/certificates", "/profiles", "/apple-devices", "/bundle-ids", "/apple-simulators"
    };

    private static readonly HashSet<string> GoogleRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/firebase-push"
    };

    private IReadOnlyList<AppleIdentity>? _cachedAppleIdentities;
    private IReadOnlyList<GoogleIdentity>? _cachedGoogleIdentities;

    public WindowsTitleBarManager(
        IToolbarService toolbarService,
        IAppleIdentityService appleIdentityService,
        IAppleIdentityStateService appleIdentityState,
        IGoogleIdentityService googleIdentityService,
        IGoogleIdentityStateService googleIdentityState,
        ICopilotContextService copilotContext,
        IServiceProvider serviceProvider)
    {
        _toolbarService = toolbarService;
        _appleIdentityService = appleIdentityService;
        _appleIdentityState = appleIdentityState;
        _googleIdentityService = googleIdentityService;
        _googleIdentityState = googleIdentityState;
        _copilotContext = copilotContext;
        _serviceProvider = serviceProvider;

        _toolbarService.ToolbarChanged += OnToolbarChanged;
        _toolbarService.RouteChanged += OnRouteChanged;
        _appleIdentityState.OnSelectionChanged += OnToolbarChanged;
        _googleIdentityState.OnSelectionChanged += OnToolbarChanged;
    }

    public TitleBar CreateTitleBar()
    {
        _titleBar = new TitleBar
        {
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

    private void OnRouteChanged(string route)
    {
        _currentRoute = route;
        // Invalidate cached identities so they reload on next rebuild
        _cachedAppleIdentities = null;
        _cachedGoogleIdentities = null;
        OnToolbarChanged();
    }

    private void RebuildContent()
    {
        if (_titleBar == null) return;

        _titleBar.PassthroughElements.Clear();

        // Split actions: "add" types go left, "refresh" types go right
        var leadingActions = new List<ToolbarAction>();
        var trailingActions = new List<ToolbarAction>();
        foreach (var action in _toolbarService.CurrentItems)
        {
            if (action.SfSymbol is "arrow.clockwise" or "arrow.triangle.2.circlepath")
                trailingActions.Add(action);
            else
                leadingActions.Add(action);
        }

        // LeadingContent: Logo + add/create buttons
        var leading = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(8, 0, 0, 0),
        };
        leading.Children.Add(new Image
        {
            Source = "sherpalogo.png",
            HeightRequest = 28,
            WidthRequest = 28,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        // Copilot button
        var copilotBtn = CreateCopilotButton();
        leading.Children.Add(copilotBtn);
        _titleBar.PassthroughElements.Add(copilotBtn);

        // Spacer to push remaining items past sidebar width
        leading.Children.Add(new BoxView
        {
            WidthRequest = 155,
            HeightRequest = 1,
            Color = Colors.Transparent,
        });

        // Identity picker (Apple or Google depending on route)
        if (AppleRoutes.Contains(_currentRoute))
        {
            var identityBtn = CreateAppleIdentityButton();
            if (identityBtn != null)
            {
                leading.Children.Add(identityBtn);
                _titleBar.PassthroughElements.Add(identityBtn);
            }
        }
        else if (GoogleRoutes.Contains(_currentRoute))
        {
            var identityBtn = CreateGoogleIdentityButton();
            if (identityBtn != null)
            {
                leading.Children.Add(identityBtn);
                _titleBar.PassthroughElements.Add(identityBtn);
            }
        }

        foreach (var action in leadingActions)
        {
            var btn = CreateActionButton(action);
            leading.Children.Add(btn);
            _titleBar.PassthroughElements.Add(btn);
        }
        _titleBar.LeadingContent = leading;
        _titleBar.PassthroughElements.Add(leading);

        // Content: Search bar + Filter button (centered)
        var hasSearch = !string.IsNullOrEmpty(_toolbarService.SearchPlaceholder);
        var hasFilters = _toolbarService.CurrentFilters.Count > 0;

        if (hasSearch || hasFilters)
        {
            var contentLayout = new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
            };

            if (hasSearch)
            {
                _searchBar = new SearchBar
                {
                    Placeholder = _toolbarService.SearchPlaceholder,
                    Text = _toolbarService.SearchText,
                    MaximumWidthRequest = 350,
                    MinimumWidthRequest = 200,
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
                contentLayout.Children.Add(_searchBar);
                _titleBar.PassthroughElements.Add(_searchBar);
            }
            else
            {
                _searchBar = null;
            }

            if (hasFilters)
            {
                var filterBtn = CreateUnifiedFilterButton();
                contentLayout.Children.Add(filterBtn);
                _titleBar.PassthroughElements.Add(filterBtn);
            }

            _titleBar.Content = contentLayout;
            _titleBar.PassthroughElements.Add(contentLayout);
        }
        else
        {
            _searchBar = null;
            _titleBar.Content = null;
        }

        // TrailingContent: Refresh buttons
        if (trailingActions.Count > 0)
        {
            var trailing = new HorizontalStackLayout
            {
                Spacing = 6,
                VerticalOptions = LayoutOptions.Center,
                Padding = new Thickness(0, 0, 8, 0),
            };
            foreach (var action in trailingActions)
            {
                var btn = CreateActionButton(action);
                trailing.Children.Add(btn);
                _titleBar.PassthroughElements.Add(btn);
            }
            _titleBar.TrailingContent = trailing;
            _titleBar.PassthroughElements.Add(trailing);
        }
        else
        {
            _titleBar.TrailingContent = null;
        }
    }

    private Button CreateUnifiedFilterButton()
    {
        // Check if any filter is active (not on index 0 = "All")
        var hasActiveFilter = _toolbarService.CurrentFilters.Any(f => f.SelectedIndex > 0);

        var filterGlyph = GetEnumDescription(FluentIcons.Filter20);
        var chevronGlyph = GetEnumDescription(FluentIcons.ChevronDown16);

        var btn = new Button
        {
            Text = filterGlyph + " " + chevronGlyph,
            FontFamily = "FluentIcons",
            FontSize = 16,
            HeightRequest = 32,
            WidthRequest = 52,
            MinimumWidthRequest = 52,
            Padding = new Thickness(10, 0),
            BackgroundColor = hasActiveFilter ? Accent : BgControl,
            TextColor = Colors.White,
            BorderColor = hasActiveFilter ? Accent : BorderColor,
            BorderWidth = 1,
            CornerRadius = 6,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.NoWrap,
        };
        ApplyVerticalCentering(btn);
        ToolTipProperties.SetText(btn, "Filter");
        ApplyHoverEffect(btn,
            hasActiveFilter ? Accent : BgControl,
            hasActiveFilter ? Accent : BgControlHover,
            hasActiveFilter ? Accent : BorderColor,
            Accent);

        var menuFlyout = new MenuFlyout();

        foreach (var filter in _toolbarService.CurrentFilters)
        {
            // Add submenu for each filter category
            var sub = new MenuFlyoutSubItem { Text = filter.Label };
            var filterId = filter.Id;

            for (int i = 0; i < filter.Options.Length; i++)
            {
                var index = i;
                var option = filter.Options[i];
                var item = new MenuFlyoutItem
                {
                    Text = (i == filter.SelectedIndex ? "✓ " : "   ") + option,
                };
                item.Clicked += (s, e) =>
                {
                    _toolbarService.NotifyFilterChanged(filterId, index);
                };
                sub.Add(item);
            }

            menuFlyout.Add(sub);
        }

        FlyoutBase.SetContextFlyout(btn, menuFlyout);

        // Open flyout on left-click
        btn.Clicked += (s, e) =>
        {
#if WINDOWS
            if (btn.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement platformView)
            {
                var flyout = platformView.ContextFlyout;
                flyout?.ShowAt(platformView);
            }
#endif
        };

        return btn;
    }

    private Button CreateActionButton(ToolbarAction action)
    {
        var fluentIcon = MapSfSymbolToFluentIcon(action.SfSymbol);
        var glyph = GetEnumDescription(fluentIcon);

        var btn = new Button
        {
            Text = glyph,
            FontFamily = "FluentIcons",
            FontSize = 18,
            HeightRequest = 32,
            WidthRequest = 36,
            MinimumWidthRequest = 36,
            Padding = new Thickness(0),
            BackgroundColor = BgControl,
            TextColor = Colors.White,
            BorderColor = BorderColor,
            BorderWidth = 1,
            CornerRadius = 6,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.NoWrap,
        };
        ApplyVerticalCentering(btn);
        ToolTipProperties.SetText(btn, action.Label);
        ApplyHoverEffect(btn, BgControl, BgControlHover, BorderColor, Accent);

        btn.Clicked += (s, e) =>
        {
            _toolbarService.InvokeToolbarItemClicked(action.Id);
        };

        return btn;
    }

    private View CreateCopilotButton()
    {
        var btn = new Border
        {
            BackgroundColor = Colors.Transparent,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Stroke = Colors.Transparent,
            Padding = new Thickness(6, 4),
            VerticalOptions = LayoutOptions.Center,
            Content = new Image
            {
                Source = "ghcp_icon_white.png",
                HeightRequest = 20,
                WidthRequest = 20,
                VerticalOptions = LayoutOptions.Center,
            }
        };

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += (s, e) => _copilotContext.ToggleOverlay();
        btn.GestureRecognizers.Add(tapGesture);

        // Hover effect via PointerGestureRecognizer
        var pointerEnter = new PointerGestureRecognizer();
        pointerEnter.PointerEntered += (s, e) => btn.BackgroundColor = BgControlHover;
        var pointerExit = new PointerGestureRecognizer();
        pointerExit.PointerExited += (s, e) => btn.BackgroundColor = Colors.Transparent;
        btn.GestureRecognizers.Add(pointerEnter);
        btn.GestureRecognizers.Add(pointerExit);

        return btn;
    }

    private View? CreateAppleIdentityButton()
    {
        if (_cachedAppleIdentities == null)
        {
            _ = Task.Run(async () =>
            {
                _cachedAppleIdentities = await _appleIdentityService.GetIdentitiesAsync();
                if (_cachedAppleIdentities?.Count > 0)
                {
                    if (_appleIdentityState.SelectedIdentity == null)
                        _appleIdentityState.SetSelectedIdentity(_cachedAppleIdentities[0]);
                    OnToolbarChanged();
                }
            });
            return null;
        }

        if (_cachedAppleIdentities.Count == 0) return null;

        var selected = _appleIdentityState.SelectedIdentity;
        if (selected == null && _cachedAppleIdentities.Count > 0)
        {
            selected = _cachedAppleIdentities[0];
            _appleIdentityState.SetSelectedIdentity(selected);
        }
        var displayName = selected?.Name ?? "Select Identity";

        var menuFlyout = new MenuFlyout();
        foreach (var identity in _cachedAppleIdentities)
        {
            var id = identity;
            var item = new MenuFlyoutItem
            {
                Text = (id.Id == selected?.Id ? "✓ " : "   ") + id.Name,
            };
            item.Clicked += (s, e) =>
            {
                _appleIdentityState.SetSelectedIdentity(id);
            };
            menuFlyout.Add(item);
        }

        menuFlyout.Add(new MenuFlyoutSeparator());
        var settingsItem = new MenuFlyoutItem { Text = "Settings…" };
        settingsItem.Clicked += (s, e) =>
        {
            _ = _serviceProvider.GetRequiredService<INavigationService>().NavigateToAsync("/settings");
        };
        menuFlyout.Add(settingsItem);

        return CreateIdentityPickerView(
            GetEnumDescription(FontAwesomeBrandIcons.Apple), "FontAwesomeBrandIcons",
            displayName, menuFlyout);
    }

    private View? CreateGoogleIdentityButton()
    {
        if (_cachedGoogleIdentities == null)
        {
            _ = Task.Run(async () =>
            {
                _cachedGoogleIdentities = await _googleIdentityService.GetIdentitiesAsync();
                if (_cachedGoogleIdentities?.Count > 0)
                {
                    if (_googleIdentityState.SelectedIdentity == null)
                        _googleIdentityState.SetSelectedIdentity(_cachedGoogleIdentities[0]);
                    OnToolbarChanged();
                }
            });
            return null;
        }

        if (_cachedGoogleIdentities.Count == 0) return null;

        var selected = _googleIdentityState.SelectedIdentity;
        if (selected == null && _cachedGoogleIdentities.Count > 0)
        {
            selected = _cachedGoogleIdentities[0];
            _googleIdentityState.SetSelectedIdentity(selected);
        }
        var displayName = selected?.Name ?? "Select Identity";

        var menuFlyout = new MenuFlyout();
        foreach (var identity in _cachedGoogleIdentities)
        {
            var id = identity;
            var item = new MenuFlyoutItem
            {
                Text = (id.Id == selected?.Id ? "✓ " : "   ") + id.Name,
            };
            item.Clicked += (s, e) =>
            {
                _googleIdentityState.SetSelectedIdentity(id);
            };
            menuFlyout.Add(item);
        }

        menuFlyout.Add(new MenuFlyoutSeparator());
        var settingsItem = new MenuFlyoutItem { Text = "Settings…" };
        settingsItem.Clicked += (s, e) =>
        {
            _ = _serviceProvider.GetRequiredService<INavigationService>().NavigateToAsync("/settings");
        };
        menuFlyout.Add(settingsItem);

        return CreateIdentityPickerView(
            GetEnumDescription(FontAwesomeBrandIcons.Google), "FontAwesomeBrandIcons",
            displayName, menuFlyout);
    }

    private Border CreateIdentityPickerView(string iconGlyph, string iconFontFamily, string displayName, MenuFlyout menuFlyout)
    {
        var chevronGlyph = GetEnumDescription(FluentIcons.ChevronDown16);

        var layout = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
        };

        layout.Children.Add(new Label
        {
            Text = iconGlyph,
            FontFamily = iconFontFamily,
            FontSize = 16,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center,
        });

        layout.Children.Add(new Label
        {
            Text = displayName,
            FontSize = 12,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center,
        });

        layout.Children.Add(new Label
        {
            Text = chevronGlyph,
            FontFamily = "FluentIcons",
            FontSize = 12,
            TextColor = TextMuted,
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center,
        });

        var border = new Border
        {
            Content = layout,
            BackgroundColor = BgControl,
            Stroke = BorderColor,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Padding = new Thickness(10, 0),
            HeightRequest = 32,
            VerticalOptions = LayoutOptions.Center,
        };

        FlyoutBase.SetContextFlyout(border, menuFlyout);

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += (s, e) =>
        {
#if WINDOWS
            if (border.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement pv)
                pv.ContextFlyout?.ShowAt(pv);
#endif
        };
        border.GestureRecognizers.Add(tapGesture);

#if WINDOWS
        // Hover effect
        border.HandlerChanged += (s, e) =>
        {
            if (border.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement fe)
            {
                fe.PointerEntered += (_, _) => { border.BackgroundColor = BgControlHover; border.Stroke = Accent; };
                fe.PointerExited += (_, _) => { border.BackgroundColor = BgControl; border.Stroke = BorderColor; };
            }
        };
#endif

        return border;
    }

    private static FluentIcons MapSfSymbolToFluentIcon(string sfSymbol) => sfSymbol switch
    {
        "arrow.clockwise" => FluentIcons.ArrowClockwise20,
        "plus" => FluentIcons.Add20,
        "plus.circle" => FluentIcons.AddCircle20,
        "square.and.arrow.down" => FluentIcons.ArrowDownload20,
        "square.and.arrow.up" => FluentIcons.ArrowUpload20,
        "trash" => FluentIcons.Delete20,
        "pencil" => FluentIcons.Edit20,
        "xmark" => FluentIcons.Dismiss20,
        "checkmark" => FluentIcons.Checkmark20,
        "magnifyingglass" => FluentIcons.Search20,
        "gear" => FluentIcons.Settings20,
        "doc.on.doc" => FluentIcons.DocumentCopy20,
        "arrow.triangle.2.circlepath" => FluentIcons.ArrowSync20,
        _ => FluentIcons.Circle20,
    };

    private static FontImageSource GetFluentIcon(FluentIcons icon, Color color, double size)
    {
        var glyph = GetEnumDescription(icon);
        return new FontImageSource
        {
            Glyph = glyph,
            FontFamily = "FluentIcons",
            Color = color,
            Size = size,
        };
    }

    private static string GetEnumDescription(Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        var attr = field?.GetCustomAttribute<DescriptionAttribute>();
        return attr?.Description ?? string.Empty;
    }

    private static void ApplyHoverEffect(Button btn, Color normalBg, Color hoverBg, Color normalBorder, Color? hoverBorder = null)
    {
#if WINDOWS
        btn.HandlerChanged += (s, e) =>
        {
            if (btn.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Button platformBtn)
            {
                platformBtn.PointerEntered += (_, _) =>
                {
                    btn.BackgroundColor = hoverBg;
                    if (hoverBorder != null) btn.BorderColor = hoverBorder;
                };
                platformBtn.PointerExited += (_, _) =>
                {
                    btn.BackgroundColor = normalBg;
                    btn.BorderColor = normalBorder;
                };
            }
        };
#endif
    }

    private static void ApplyVerticalCentering(Button btn)
    {
#if WINDOWS
        btn.HandlerChanged += (s, e) =>
        {
            if (btn.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Button platformBtn)
            {
                platformBtn.VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center;
                platformBtn.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center;
                platformBtn.Padding = new Microsoft.UI.Xaml.Thickness(0);
            }
        };
#endif
    }
}
