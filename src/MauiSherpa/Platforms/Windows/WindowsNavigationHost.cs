using MauiSherpa.Core.Interfaces;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;
using Visibility = Microsoft.UI.Xaml.Visibility;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinUIAutomationProperties = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace MauiSherpa.Platforms.Windows;

public sealed class WindowsNavigationHost : ContentView
{
    private bool _toggleBeforeReady;

    public WindowsNavigationHost(IToolbarService toolbarService, IThemeService themeService)
    {
        ToolbarService = toolbarService;
        ThemeService = themeService;
    }

    internal IToolbarService ToolbarService { get; }
    internal IThemeService ThemeService { get; }
    internal event Action<double>? PaneWidthChanged;

    internal void TogglePane()
    {
        if (Handler is WindowsNavigationHostHandler { NavigationView: { } navigationView })
            navigationView.IsPaneOpen = !navigationView.IsPaneOpen;
        else
            _toggleBeforeReady = !_toggleBeforeReady;
    }

    internal void NotifyPaneWidthChanged(double width) => PaneWidthChanged?.Invoke(width);

    internal void ApplyPendingToggle(WinUI.NavigationView navigationView)
    {
        if (_toggleBeforeReady)
        {
            navigationView.IsPaneOpen = !navigationView.IsPaneOpen;
            _toggleBeforeReady = false;
        }
    }
}

public sealed class WindowsNavigationHostHandler : ViewHandler<WindowsNavigationHost, WinUI.Grid>
{
    private const string PaneWidthPreference = "sidebar_width";
    private const double DefaultPaneWidth = 250;
    private const double MinPaneWidth = 180;
    private const double MaxPaneWidth = 480;
    private const double MinContentWidth = 320;
    private const double ResizeHandleWidth = 8;

    private readonly List<(string Route, WinUI.NavigationViewItem Item, bool Exact)> _items = [];
    private bool _connected;
    private long _paneOpenChangeRegistration;
    private SidebarResizeHandle _resizeHandle = null!;
    private WinUI.Primitives.Thumb _resizeThumb = null!;
    private WinUI.Border _resizeIndicator = null!;
    private double _preferredPaneWidth;
    private bool _hasCustomPaneWidth;
    private bool _resizeHovered;
    private bool _resizeFocused;
    private bool _resizing;
    private double _resizeStartWidth;
    private double _resizeStartPreferredWidth;
    private double _resizeStartPointerX;
    private uint? _resizePointerId;
    private bool _resizeHadCustomWidth;
    private WinUI.ScrollViewer? _menuScrollViewer;
    private WinUI.Primitives.ScrollBar? _menuScrollBar;
    private bool _pointerOverMenu;
    private bool _menuHasFocus;

    internal WinUI.NavigationView? NavigationView { get; private set; }

    public static readonly IPropertyMapper<WindowsNavigationHost, WindowsNavigationHostHandler> Mapper =
        new PropertyMapper<WindowsNavigationHost, WindowsNavigationHostHandler>(ViewMapper)
        {
            [nameof(IContentView.Content)] = MapContent
        };

    public WindowsNavigationHostHandler() : base(Mapper)
    {
    }

    protected override WinUI.Grid CreatePlatformView()
    {
        var navigationView = new WinUI.NavigationView
        {
            PaneDisplayMode = WinUI.NavigationViewPaneDisplayMode.Auto,
            IsPaneOpen = true,
            IsPaneToggleButtonVisible = false,
            IsBackButtonVisible = WinUI.NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsTitleBarAutoPaddingEnabled = false,
            AlwaysShowHeader = false
        };
        WinUIAutomationProperties.SetAutomationId(navigationView, "windows-sidebar");

        // WinUI otherwise reserves space above the content for its hidden pane toggle.
        navigationView.Resources["NavigationViewContentMargin"] = new Microsoft.UI.Xaml.Thickness(0);
        navigationView.Resources["NavigationViewMinimalContentMargin"] = new Microsoft.UI.Xaml.Thickness(0);
        navigationView.Resources.ThemeDictionaries["Default"] = CreateThemeResources(dark: true);
        navigationView.Resources.ThemeDictionaries["Light"] = CreateThemeResources(dark: false);
        NavigationView = navigationView;

        _resizeIndicator = new WinUI.Border
        {
            Width = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        _resizeThumb = new WinUI.Primitives.Thumb
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            IsTabStop = true
        };
        _resizeHandle = new SidebarResizeHandle
        {
            Width = ResizeHandleWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Content = _resizeThumb,
            Visibility = Visibility.Collapsed,
            IsTabStop = false
        };
        WinUIAutomationProperties.SetAutomationId(_resizeThumb, "sidebar-resize");
        WinUIAutomationProperties.SetName(_resizeThumb, "Resize sidebar");
        WinUIAutomationProperties.SetHelpText(_resizeThumb, "Drag or use the left and right arrow keys to resize the sidebar.");
        WinUI.ToolTipService.SetToolTip(_resizeThumb, "Resize sidebar");

        var root = new WinUI.Grid();
        root.Children.Add(navigationView);
        root.Children.Add(_resizeIndicator);
        root.Children.Add(_resizeHandle);
        return root;
    }

    protected override void ConnectHandler(WinUI.Grid platformView)
    {
        base.ConnectHandler(platformView);
        _connected = true;
        var navigationView = NavigationView!;
        VirtualView.ApplyPendingToggle(navigationView);

        AddItem(navigationView, "Dashboard", "/", WinUI.Symbol.Home, exact: true);

        AddHeader(navigationView, "Android");
        AddItem(navigationView, "Devices & Emulators", "/devices", WinUI.Symbol.CellPhone);
        AddItem(navigationView, "SDK Packages", "/android-sdk", WinUI.Symbol.Library);
        AddItem(navigationView, "Keystores", "/keystores", WinUI.Symbol.Permissions);
        AddItem(navigationView, "Push Testing", "/firebase-push", WinUI.Symbol.Send);

        AddHeader(navigationView, "Apple");
        AddItem(navigationView, "Registered Devices", "/apple-devices", WinUI.Symbol.Phone);
        AddItem(navigationView, "Bundle IDs", "/bundle-ids", WinUI.Symbol.Tag);
        AddItem(navigationView, "Certificates", "/certificates", WinUI.Symbol.ProtectedDocument);
        AddItem(navigationView, "Provisioning Profiles", "/profiles", WinUI.Symbol.ContactInfo);
        AddItem(navigationView, "Push Testing", "/push-testing", WinUI.Symbol.Send);

        AddHeader(navigationView, "Secrets");
        AddItem(navigationView, "Secrets", "/secrets", WinUI.Symbol.Permissions, exact: true);
        AddItem(navigationView, "Publish", "/secrets/publish", WinUI.Symbol.Upload);

        AddHeader(navigationView, "Tools");
        AddItem(navigationView, ".NET SDK Manager", "/dotnet-sdk", WinUI.Symbol.Library);
        AddItem(navigationView, "Profiling", "/profiling", WinUI.Symbol.ShowResults);
        AddItem(navigationView, "App Inspector", "/devflow", WinUI.Symbol.Find);
#if DEBUG
        AddItem(navigationView, "Debug UI", "/debug", WinUI.Symbol.Repair);
#endif

        var savedWidth = Preferences.Default.Get(PaneWidthPreference, 0d);
        _hasCustomPaneWidth = double.IsFinite(savedWidth) && savedWidth > 0;
        _preferredPaneWidth = _hasCustomPaneWidth ? savedWidth : DefaultPaneWidth;

        _paneOpenChangeRegistration = navigationView.RegisterPropertyChangedCallback(
            WinUI.NavigationView.IsPaneOpenProperty, OnPaneOpenChanged);
        navigationView.Loaded += OnNavigationLoaded;
        navigationView.ItemInvoked += OnItemInvoked;
        navigationView.DisplayModeChanged += OnDisplayModeChanged;
        platformView.SizeChanged += OnHostSizeChanged;
        _resizeThumb.DragStarted += OnResizeStarted;
        _resizeThumb.DragCompleted += OnResizeCompleted;
        _resizeThumb.AddHandler(UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnResizePointerPressed), true);
        _resizeThumb.AddHandler(UIElement.PointerMovedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnResizePointerMoved), true);
        _resizeThumb.KeyDown += OnResizeKeyDown;
        _resizeThumb.PointerEntered += OnResizePointerEntered;
        _resizeThumb.PointerExited += OnResizePointerExited;
        _resizeThumb.GotFocus += OnResizeGotFocus;
        _resizeThumb.LostFocus += OnResizeLostFocus;
        VirtualView.ToolbarService.RouteChanged += OnRouteChanged;
        VirtualView.ThemeService.ThemeChanged += OnThemeChanged;
        UpdateHeaderHeights();
        SelectRoute(VirtualView.ToolbarService.CurrentRoute);
        ApplyTheme();
    }

    protected override void DisconnectHandler(WinUI.Grid platformView)
    {
        _connected = false;
        var navigationView = NavigationView!;
        VirtualView.ToolbarService.RouteChanged -= OnRouteChanged;
        VirtualView.ThemeService.ThemeChanged -= OnThemeChanged;
        navigationView.Loaded -= OnNavigationLoaded;
        navigationView.ItemInvoked -= OnItemInvoked;
        navigationView.DisplayModeChanged -= OnDisplayModeChanged;
        navigationView.UnregisterPropertyChangedCallback(
            WinUI.NavigationView.IsPaneOpenProperty, _paneOpenChangeRegistration);
        platformView.SizeChanged -= OnHostSizeChanged;
        _resizeThumb.DragStarted -= OnResizeStarted;
        _resizeThumb.DragCompleted -= OnResizeCompleted;
        _resizeThumb.RemoveHandler(UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnResizePointerPressed));
        _resizeThumb.RemoveHandler(UIElement.PointerMovedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnResizePointerMoved));
        _resizeThumb.KeyDown -= OnResizeKeyDown;
        _resizeThumb.PointerEntered -= OnResizePointerEntered;
        _resizeThumb.PointerExited -= OnResizePointerExited;
        _resizeThumb.GotFocus -= OnResizeGotFocus;
        _resizeThumb.LostFocus -= OnResizeLostFocus;
        if (_menuScrollViewer != null)
        {
            _menuScrollViewer.PointerEntered -= OnMenuPointerEntered;
            _menuScrollViewer.PointerExited -= OnMenuPointerExited;
            _menuScrollViewer.GotFocus -= OnMenuGotFocus;
            _menuScrollViewer.LostFocus -= OnMenuLostFocus;
            _menuScrollViewer = null;
            _menuScrollBar = null;
        }
        navigationView.Content = null;
        _items.Clear();
        NavigationView = null;
        base.DisconnectHandler(platformView);
    }

    private static void MapContent(WindowsNavigationHostHandler handler, WindowsNavigationHost view)
    {
        var context = handler.MauiContext
            ?? throw new InvalidOperationException("The Windows navigation host requires a MAUI context.");
        handler.NavigationView!.Content = view.Content is IView content ? content.ToPlatform(context) : null;
    }

    private void AddHeader(WinUI.NavigationView navigationView, string title)
    {
        navigationView.MenuItems.Add(new WinUI.NavigationViewItemSeparator());
        navigationView.MenuItems.Add(new WinUI.NavigationViewItemHeader { Content = title });
    }

    private void AddItem(WinUI.NavigationView navigationView, string title, string route, WinUI.Symbol icon, bool exact = false)
    {
        var item = new WinUI.NavigationViewItem
        {
            Content = title,
            Tag = route,
            Icon = new WinUI.SymbolIcon(icon)
        };
        var id = route == "/" ? "dashboard" : route.Trim('/').Replace('/', '-');
        WinUIAutomationProperties.SetAutomationId(item, $"sidebar-{id}");
        navigationView.MenuItems.Add(item);
        _items.Add((route, item, exact));
    }

    private void OnPaneOpenChanged(DependencyObject sender, DependencyProperty property)
    {
        UpdateHeaderHeights();
        UpdatePaneWidth();
    }

    private void UpdateHeaderHeights()
    {
        var navigationView = NavigationView!;
        foreach (var header in navigationView.MenuItems.OfType<WinUI.NavigationViewItemHeader>())
            header.Height = navigationView.IsPaneOpen ? 32 * Math.Max(1, VirtualView.ThemeService.FontScale) : double.NaN;
    }

    private double ConstrainPaneWidth(double width)
    {
        var minimum = MinPaneWidth * Math.Max(1, VirtualView.ThemeService.FontScale);
        var maximum = PlatformView.ActualWidth > 0
            ? Math.Max(minimum, Math.Min(MaxPaneWidth, PlatformView.ActualWidth - MinContentWidth))
            : MaxPaneWidth;
        return Math.Clamp(width, minimum, maximum);
    }

    private void UpdatePaneWidth()
    {
        var navigationView = NavigationView!;
        var width = ConstrainPaneWidth(_preferredPaneWidth);
        navigationView.OpenPaneLength = width;
        _resizeHandle.Margin = new Microsoft.UI.Xaml.Thickness(width - ResizeHandleWidth / 2, 0, 0, 0);
        _resizeIndicator.Margin = new Microsoft.UI.Xaml.Thickness(width - 1, 0, 0, 0);
        _resizeHandle.Visibility = navigationView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        UpdateResizeIndicator();

        var visibleWidth = navigationView.IsPaneOpen
            ? width
            : navigationView.DisplayMode == WinUI.NavigationViewDisplayMode.Minimal ? 0 : navigationView.CompactPaneLength;
        VirtualView.NotifyPaneWidthChanged(visibleWidth);
    }

    private void UpdateResizeIndicator()
    {
        var accent = VirtualView.ThemeService.IsDarkMode ? WindowsAccentTheme.Dark : WindowsAccentTheme.Light;
        _resizeIndicator.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(WithAlpha(accent, 0x99));
        _resizeIndicator.Visibility = NavigationView!.IsPaneOpen && (_resizeHovered || _resizeFocused || _resizing)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs args) => UpdatePaneWidth();

    private void OnDisplayModeChanged(WinUI.NavigationView sender, WinUI.NavigationViewDisplayModeChangedEventArgs args) =>
        UpdatePaneWidth();

    private void OnResizeStarted(object sender, WinUI.Primitives.DragStartedEventArgs args)
    {
        _resizeStartWidth = NavigationView!.OpenPaneLength;
        _resizeStartPreferredWidth = _preferredPaneWidth;
        _resizeHadCustomWidth = _hasCustomPaneWidth;
        _resizing = true;
        UpdateResizeIndicator();
    }

    private void OnResizePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _resizePointerId = args.Pointer.PointerId;
        _resizeStartPointerX = args.GetCurrentPoint(PlatformView).Position.X;
    }

    private void OnResizePointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (!_resizing || args.Pointer.PointerId != _resizePointerId)
            return;

        var distance = args.GetCurrentPoint(PlatformView).Position.X - _resizeStartPointerX;
        var width = ConstrainPaneWidth(_resizeStartWidth + distance);
        if (width == NavigationView!.OpenPaneLength)
            return;

        _preferredPaneWidth = width;
        _hasCustomPaneWidth = true;
        UpdatePaneWidth();
    }

    private void OnResizeCompleted(object sender, WinUI.Primitives.DragCompletedEventArgs args)
    {
        _resizing = false;
        _resizePointerId = null;
        if (args.Canceled)
        {
            _preferredPaneWidth = _resizeStartPreferredWidth;
            _hasCustomPaneWidth = _resizeHadCustomWidth;
            UpdatePaneWidth();
        }
        else if (_hasCustomPaneWidth)
        {
            Preferences.Default.Set(PaneWidthPreference, _preferredPaneWidth);
        }
        UpdateResizeIndicator();
    }

    private void OnResizeKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        var delta = args.Key switch
        {
            global::Windows.System.VirtualKey.Left => -10,
            global::Windows.System.VirtualKey.Right => 10,
            _ => 0
        };
        if (delta == 0)
            return;

        _preferredPaneWidth = ConstrainPaneWidth(NavigationView!.OpenPaneLength + delta);
        _hasCustomPaneWidth = true;
        UpdatePaneWidth();
        Preferences.Default.Set(PaneWidthPreference, _preferredPaneWidth);
        args.Handled = true;
    }

    private void OnResizePointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _resizeHovered = true;
        UpdateResizeIndicator();
    }

    private void OnResizePointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _resizeHovered = false;
        UpdateResizeIndicator();
    }

    private void OnResizeGotFocus(object sender, RoutedEventArgs args)
    {
        _resizeFocused = true;
        UpdateResizeIndicator();
    }

    private void OnResizeLostFocus(object sender, RoutedEventArgs args)
    {
        _resizeFocused = false;
        UpdateResizeIndicator();
    }

    private sealed class SidebarResizeHandle : WinUI.ContentControl
    {
        public SidebarResizeHandle()
        {
            DefaultStyleKey = typeof(WinUI.ContentControl);
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
        }
    }

    private void OnNavigationLoaded(object sender, RoutedEventArgs args)
    {
        if (_menuScrollViewer != null)
            return;

        var logger = MauiContext?.Services.GetService(typeof(ILoggingService)) as ILoggingService;
        if (FindNamedDescendant<WinUI.SplitView>(NavigationView!, "RootSplitView") is { } splitView)
            splitView.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(0);
        else
            logger?.LogWarning("Could not find the Windows navigation pane to remove its rounded corners.");

        var viewer = FindNamedDescendant<WinUI.ScrollViewer>(NavigationView!, "MenuItemsScrollViewer");
        viewer?.ApplyTemplate();
        var scrollBar = viewer == null ? null : FindNamedDescendant<WinUI.Primitives.ScrollBar>(viewer, "VerticalScrollBar");
        if (viewer == null || scrollBar == null)
        {
            logger?.LogWarning("Could not find the Windows navigation scrollbar to enable auto-hide.");
            return;
        }

        _menuScrollViewer = viewer;
        _menuScrollBar = scrollBar;
        viewer.PointerEntered += OnMenuPointerEntered;
        viewer.PointerExited += OnMenuPointerExited;
        viewer.GotFocus += OnMenuGotFocus;
        viewer.LostFocus += OnMenuLostFocus;
        UpdateScrollbarVisibility();
    }

    private void OnMenuPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _pointerOverMenu = true;
        UpdateScrollbarVisibility();
    }

    private void OnMenuPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        _pointerOverMenu = false;
        UpdateScrollbarVisibility();
    }

    private void OnMenuGotFocus(object sender, RoutedEventArgs args)
    {
        _menuHasFocus = true;
        UpdateScrollbarVisibility();
    }

    private void OnMenuLostFocus(object sender, RoutedEventArgs args)
    {
        _menuHasFocus = false;
        UpdateScrollbarVisibility();
    }

    private void UpdateScrollbarVisibility()
    {
        if (_menuScrollBar != null)
        {
            var visible = _pointerOverMenu || _menuHasFocus;
            _menuScrollBar.Opacity = visible ? 1 : 0;
            _menuScrollBar.IsHitTestVisible = visible;
        }
    }

    private static T? FindNamedDescendant<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T named && named.Name == name)
                return named;
            if (FindNamedDescendant<T>(child, name) is { } descendant)
                return descendant;
        }
        return null;
    }

    private void OnItemInvoked(WinUI.NavigationView sender, WinUI.NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is WinUI.NavigationViewItem { Tag: string route })
            VirtualView.ToolbarService.RequestNavigation(route);
    }

    private void OnRouteChanged(string route)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_connected)
                SelectRoute(route);
        });
    }

    private void SelectRoute(string route)
    {
        NavigationView!.SelectedItem = _items
            .Where(item => string.Equals(item.Route, route, StringComparison.OrdinalIgnoreCase)
                || (!item.Exact && route.StartsWith(item.Route + "/", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.Route.Length)
            .Select(item => item.Item)
            .FirstOrDefault();
    }

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_connected)
                ApplyTheme();
        });
    }

    private void ApplyTheme()
    {
        var theme = VirtualView.ThemeService;
        var navigationView = NavigationView!;
        navigationView.RequestedTheme = theme.CurrentTheme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        if (!_hasCustomPaneWidth)
            _preferredPaneWidth = DefaultPaneWidth * Math.Max(1, theme.FontScale);
        UpdateHeaderHeights();
        UpdatePaneWidth();

        foreach (var item in navigationView.MenuItems)
        {
            if (item is WinUI.NavigationViewItem navigationItem)
            {
                navigationItem.FontSize = 14 * theme.FontScale;
                navigationItem.MinHeight = 44 * Math.Max(1, theme.FontScale);
            }
            else if (item is WinUI.NavigationViewItemHeader header)
                header.FontSize = 12 * theme.FontScale;
        }
    }

    private static Microsoft.UI.Xaml.ResourceDictionary CreateThemeResources(bool dark)
    {
        var resources = new Microsoft.UI.Xaml.ResourceDictionary();
        var paneBackground = dark ? Color(0x2C, 0x2C, 0x2C) : Color(0xF4, 0xF4, 0xF4);
        var accent = dark ? WindowsAccentTheme.Dark : WindowsAccentTheme.Light;
        AddBrush(resources, "NavigationViewDefaultPaneBackground", paneBackground);
        AddBrush(resources, "NavigationViewExpandedPaneBackground", paneBackground);
        AddBrush(resources, "NavigationViewSelectionIndicatorForeground", accent);
        AddBrush(resources, "NavigationViewItemBackgroundPointerOver", WithAlpha(accent, 0x08));
        AddBrush(resources, "NavigationViewItemBackgroundPressed", WithAlpha(accent, 0x12));
        AddBrush(resources, "NavigationViewItemBackgroundSelected", WithAlpha(accent, 0x10));
        AddBrush(resources, "NavigationViewItemBackgroundSelectedPointerOver", WithAlpha(accent, 0x16));
        AddBrush(resources, "NavigationViewItemBackgroundSelectedPressed", WithAlpha(accent, 0x1B));
        return resources;
    }

    private static void AddBrush(Microsoft.UI.Xaml.ResourceDictionary resources, string key, global::Windows.UI.Color color) =>
        resources[key] = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);

    private static global::Windows.UI.Color Color(byte red, byte green, byte blue) =>
        global::Windows.UI.Color.FromArgb(255, red, green, blue);

    private static global::Windows.UI.Color WithAlpha(global::Windows.UI.Color color, byte alpha) =>
        global::Windows.UI.Color.FromArgb(alpha, color.R, color.G, color.B);
}
