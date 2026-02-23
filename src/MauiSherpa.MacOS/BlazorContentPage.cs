using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform.MacOS;
using Microsoft.Maui.Platform.MacOS.Controls;
using MauiSherpa.Core.Interfaces;
using AppKit;
using CoreGraphics;
using Foundation;
using WebKit;

namespace MauiSherpa;

/// <summary>
/// ContentPage hosting a MacOSBlazorWebView for the detail area of the FlyoutPage.
/// Provides methods to navigate the Blazor router from native code.
/// </summary>
public class BlazorContentPage : ContentPage
{
    private readonly MacOSBlazorWebView _blazorWebView;
    private readonly IToolbarService _toolbarService;
    private readonly ISplashService _splashService;
    private readonly IAppleIdentityService _identityService;
    private readonly IAppleIdentityStateService _identityState;
    private AppKit.NSView? _loadingOverlay;
    private string _pendingRoute = "/";
    private string _currentRoute = "/";

    // Routes that show the Apple identity picker
    static readonly HashSet<string> AppleRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/certificates", "/profiles", "/apple-devices", "/bundle-ids"
    };

    public BlazorContentPage(IServiceProvider serviceProvider)
    {
        Title = "";

        _toolbarService = serviceProvider.GetRequiredService<IToolbarService>();
        _toolbarService.ToolbarChanged += OnToolbarChanged;
        _toolbarService.FilterChanged += OnFilterSelectionChanged;

        _identityService = serviceProvider.GetRequiredService<IAppleIdentityService>();
        _identityState = serviceProvider.GetRequiredService<IAppleIdentityStateService>();
        _toolbarService.RouteChanged += route => _currentRoute = route;

        _splashService = serviceProvider.GetRequiredService<ISplashService>();
        _splashService.OnBlazorReady += OnBlazorReady;

        _blazorWebView = new MacOSBlazorWebView
        {
            HostPage = "wwwroot/index.html",
            ContentInsets = new Thickness(0, 52, 0, 0),
        };
        _blazorWebView.RootComponents.Add(new BlazorRootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Components.App),
        });

        Content = _blazorWebView;

        // Always show the Copilot button in the sidebar trailing area
        var initialCopilot = CreateCopilotToolbarItem();
        ToolbarItems.Add(initialCopilot);

        // Add overlay as soon as the native view is connected
        _blazorWebView.HandlerChanged += (s, e) =>
        {
            if (_blazorWebView.Handler != null)
            {
                AddNativeLoadingOverlay();
                MakeWebViewTransparent();
            }
        };

        // Safety timeout for loading overlay
        Dispatcher.StartTimer(TimeSpan.FromSeconds(15), () =>
        {
            if (_loadingOverlay != null)
                HideSplash();
            return false;
        });
    }

    /// <summary>
    /// Add a native NSView overlay on top of the window content once the handler is connected.
    /// This avoids wrapping BlazorWebView in a Grid which breaks safe area layout.
    /// </summary>
    private void AddNativeLoadingOverlay()
    {
        if (_blazorWebView.Handler?.PlatformView is not AppKit.NSView webViewNative) return;
        var superview = webViewNative.Superview ?? webViewNative;

        var bgColor = AppKit.NSColor.WindowBackground;

        _loadingOverlay = new AppKit.NSView(superview.Bounds)
        {
            AutoresizingMask = AppKit.NSViewResizingMask.WidthSizable | AppKit.NSViewResizingMask.HeightSizable,
            WantsLayer = true,
        };
        _loadingOverlay.Layer!.BackgroundColor = bgColor.CGColor;

        // Add a native spinner
        var spinner = new AppKit.NSProgressIndicator(new CoreGraphics.CGRect(0, 0, 24, 24))
        {
            Style = AppKit.NSProgressIndicatorStyle.Spinning,
            IsDisplayedWhenStopped = false,
            ControlSize = AppKit.NSControlSize.Small,
        };
        spinner.StartAnimation(null);
        spinner.TranslatesAutoresizingMaskIntoConstraints = false;
        _loadingOverlay.AddSubview(spinner);
        spinner.CenterXAnchor.ConstraintEqualTo(_loadingOverlay.CenterXAnchor).Active = true;
        spinner.CenterYAnchor.ConstraintEqualTo(_loadingOverlay.CenterYAnchor).Active = true;

        superview.AddSubview(_loadingOverlay, AppKit.NSWindowOrderingMode.Above, webViewNative);
    }

    /// <summary>
    /// Make the WKWebView transparent so the native macOS window background shows through.
    /// </summary>
    private void MakeWebViewTransparent()
    {
        if (_blazorWebView.Handler?.PlatformView is not WebKit.WKWebView webView) return;

        // WKWebView draws an opaque background by default.
        // Use KVC to disable it so the native window background is visible.
        webView.SetValueForKey(NSObject.FromObject(false), new NSString("drawsBackground"));
    }

    private void OnBlazorReady()
    {
        Dispatcher.Dispatch(() => HideSplash());
    }

    private void HideSplash()
    {
        if (_loadingOverlay == null) return;
        NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
        {
            // Animate opacity to 0 using CoreAnimation
            CoreAnimation.CATransaction.Begin();
            CoreAnimation.CATransaction.AnimationDuration = 0.3;
            CoreAnimation.CATransaction.CompletionBlock = () =>
            {
                _loadingOverlay?.RemoveFromSuperview();
                _loadingOverlay = null;
            };
            _loadingOverlay!.Layer!.Opacity = 0;
            CoreAnimation.CATransaction.Commit();
        });
    }

    /// <summary>
    /// Navigate the Blazor router to the specified route.
    /// Uses Blazor.navigateTo() JS interop.
    /// </summary>
    public void NavigateToRoute(string route)
    {
        _pendingRoute = route;

        // Use Dispatcher instead of MainThread (Essentials may not be ready during ConnectHandler)
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                var js = $"Blazor.navigateTo('{route}')";
                await EvaluateJavaScriptAsync(js);
            }
            catch
            {
                // Blazor may not be ready yet; store pending route
            }
        });
    }

    async Task EvaluateJavaScriptAsync(string js)
    {
        // The MacOSBlazorWebView exposes the underlying WKWebView.
        // We use the handler's platform view to evaluate JS.
        if (_blazorWebView.Handler?.PlatformView is WebKit.WKWebView webView)
        {
            await webView.EvaluateJavaScriptAsync(js);
        }
    }

    ToolbarItem CreateCopilotToolbarItem()
    {
        var copilotItem = new ToolbarItem
        {
            Text = "Copilot",
            IconImageSource = "sparkles",
            Command = new Command(() =>
            {
                Dispatcher.Dispatch(async () =>
                {
                    try { await EvaluateJavaScriptAsync("document.querySelector('.copilot-fab')?.click()"); }
                    catch { }
                });
            }),
        };
        MacOSToolbarItem.SetPlacement(copilotItem, MacOSToolbarItemPlacement.SidebarTrailing);
        return copilotItem;
    }

    void OnToolbarChanged()
    {
        Dispatcher.Dispatch(() =>
        {
            ToolbarItems.Clear();

            // Copilot button in sidebar trailing area (convenience mode handles it)
            var copilotItem = CreateCopilotToolbarItem();
            ToolbarItems.Add(copilotItem);

            // Build toolbar items from service, partitioned for layout
            ToolbarItem? refreshItem = null;
            var leadingItems = new List<ToolbarItem>();  // add/create buttons
            foreach (var action in _toolbarService.CurrentItems)
            {
                var item = new ToolbarItem
                {
                    Text = action.Label,
                    Order = action.IsPrimary ? ToolbarItemOrder.Primary : ToolbarItemOrder.Secondary,
                    Command = new Command(() => _toolbarService.InvokeToolbarItemClicked(action.Id)),
                };
                if (!string.IsNullOrEmpty(action.SfSymbol))
                    item.IconImageSource = action.SfSymbol;
                ToolbarItems.Add(item);

                if (action.Id == "refresh")
                    refreshItem = item;
                else
                    leadingItems.Add(item);
            }

            // Native search item
            MacOSSearchToolbarItem? searchItem = null;
            if (_toolbarService.SearchPlaceholder != null)
            {
                searchItem = new MacOSSearchToolbarItem
                {
                    Placeholder = _toolbarService.SearchPlaceholder,
                    Text = _toolbarService.SearchText,
                };
                searchItem.TextChanged += (s, e) => _toolbarService.NotifySearchTextChanged(e.NewTextValue ?? "");
                MacOSToolbar.SetSearchItem(this, searchItem);
            }
            else
            {
                MacOSToolbar.SetSearchItem(this, null);
            }

            // Apple identity menu for Apple pages
            MacOSMenuToolbarItem? identityMenu = null;
            if (AppleRoutes.Contains(_currentRoute))
                identityMenu = CreateAppleIdentityMenu();

            // Native filter menu — single menu button with submenus per filter category
            var filters = _toolbarService.CurrentFilters;
            MacOSMenuToolbarItem? filterMenu = null;
            if (filters.Count > 0)
            {
                filterMenu = new MacOSMenuToolbarItem
                {
                    Icon = "line.3.horizontal.decrease",
                    Text = "Filters",
                    ShowsIndicator = true,
                };
                for (int fi = 0; fi < filters.Count; fi++)
                {
                    var filter = filters[fi];
                    var filterId = filter.Id;
                    var submenuItem = new MacOSMenuItem { Text = filter.Label };
                    for (int oi = 0; oi < filter.Options.Length; oi++)
                    {
                        var optIndex = oi;
                        var option = new MacOSMenuItem
                        {
                            Text = filter.Options[oi],
                            IsChecked = oi == filter.SelectedIndex,
                        };
                        option.Clicked += (s, e) => _toolbarService.NotifyFilterChanged(filterId, optIndex);
                        submenuItem.SubItems.Add(option);
                    }
                    if (fi > 0)
                        filterMenu.Items.Add(new MacOSMenuItem { IsSeparator = true });
                    filterMenu.Items.Add(submenuItem);
                }
            }

            // Build explicit content layout:
            // [Identity] [Add/Create] ← FlexibleSpace → [Filter] [Search] [Refresh]
            var layout = new List<MacOSToolbarLayoutItem>();
            if (identityMenu != null)
                layout.Add(MacOSToolbarLayoutItem.Menu(identityMenu));
            foreach (var item in leadingItems)
                layout.Add(MacOSToolbarLayoutItem.Item(item));
            layout.Add(MacOSToolbarLayoutItem.FlexibleSpace);
            if (filterMenu != null)
                layout.Add(MacOSToolbarLayoutItem.Menu(filterMenu));
            if (searchItem != null)
                layout.Add(MacOSToolbarLayoutItem.Search(searchItem));
            if (refreshItem != null)
                layout.Add(MacOSToolbarLayoutItem.Item(refreshItem));

            MacOSToolbar.SetMenuItems(this, null);
            MacOSToolbar.SetPopUpItems(this, null);
            MacOSToolbar.SetSidebarLayout(this, null);
            MacOSToolbar.SetContentLayout(this, layout);
        });
    }

    private IReadOnlyList<AppleIdentity>? _cachedIdentities;

    MacOSMenuToolbarItem? CreateAppleIdentityMenu()
    {
        // Use cached identities — loaded async in background on first Apple page visit
        if (_cachedIdentities == null || _cachedIdentities.Count == 0)
        {
            // Kick off async load, toolbar will rebuild when it completes
            Task.Run(async () =>
            {
                _cachedIdentities = await _identityService.GetIdentitiesAsync();
                if (_cachedIdentities?.Count > 0)
                    Dispatcher.Dispatch(OnToolbarChanged);
            });
            return null;
        }

        var identities = _cachedIdentities;
        var selectedId = _identityState.SelectedIdentity?.Id;
        var selectedName = _identityState.SelectedIdentity?.Name ?? identities[0].Name;

        var menu = new MacOSMenuToolbarItem
        {
            Icon = "apple.logo",
            Text = selectedName,
            ShowsTitle = true,
            ShowsIndicator = true,
        };

        for (int i = 0; i < identities.Count; i++)
        {
            var identity = identities[i];
            var item = new MacOSMenuItem
            {
                Text = identity.Name,
                IsChecked = identity.Id == selectedId,
            };
            item.Clicked += (s, e) =>
            {
                _identityState.SetSelectedIdentity(identity);
                OnToolbarChanged();
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    void OnFilterSelectionChanged(string filterId, int selectedIndex)
    {
        // Update native NSMenu checkmarks directly without rebuilding the toolbar
        Dispatcher.Dispatch(() =>
        {
            var nsWindow = this.Window?.Handler?.PlatformView as NSWindow;
            var toolbar = nsWindow?.Toolbar;
            if (toolbar == null) return;

            foreach (var item in toolbar.Items)
            {
                if (item is NSMenuToolbarItem menuToolbarItem)
                {
                    var menu = menuToolbarItem.Menu;
                    if (menu == null) continue;

                    // Find the submenu matching this filter
                    var filters = _toolbarService.CurrentFilters;
                    int filterIndex = -1;
                    for (int i = 0; i < filters.Count; i++)
                    {
                        if (filters[i].Id == filterId) { filterIndex = i; break; }
                    }
                    if (filterIndex < 0) continue;

                    // Account for separator items between filter submenus
                    int menuItemIndex = 0;
                    for (int i = 0; i < filterIndex; i++)
                    {
                        menuItemIndex++; // the submenu item
                        if (i > 0) menuItemIndex++; // separator before it (skip first)
                    }
                    if (filterIndex > 0) menuItemIndex++; // separator before this filter

                    if (menuItemIndex >= menu.Count) return;
                    var submenu = menu.ItemAt(menuItemIndex)?.Submenu;
                    if (submenu == null) return;

                    for (int i = 0; i < submenu.Count; i++)
                    {
                        var mi = submenu.ItemAt(i);
                        if (mi != null)
                            mi.State = i == selectedIndex ? NSCellStateValue.On : NSCellStateValue.Off;
                    }
                    return;
                }
            }
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Disable right-click context menu in the webview
        Dispatcher.Dispatch(async () =>
        {
            try { await EvaluateJavaScriptAsync("document.addEventListener('contextmenu', e => e.preventDefault())"); }
            catch { }
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _toolbarService.ToolbarChanged -= OnToolbarChanged;
        _splashService.OnBlazorReady -= OnBlazorReady;
    }
}
