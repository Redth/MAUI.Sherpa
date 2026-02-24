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
    private readonly IGoogleIdentityService _googleIdentityService;
    private readonly IGoogleIdentityStateService _googleIdentityState;
    private AppKit.NSView? _loadingOverlay;
    private NSImage? _copilotIcon;
    private string _pendingRoute = "/";
    private string _currentRoute = "/";

    // Routes that show the Apple identity picker
    static readonly HashSet<string> AppleRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/certificates", "/profiles", "/apple-devices", "/bundle-ids", "/apple-simulators"
    };

    // Routes that show the Google identity picker
    static readonly HashSet<string> GoogleRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/firebase-push"
    };

    public BlazorContentPage(IServiceProvider serviceProvider)
    {
        Title = "";

        _toolbarService = serviceProvider.GetRequiredService<IToolbarService>();
        _toolbarService.ToolbarChanged += OnToolbarChanged;
        _toolbarService.FilterChanged += OnFilterSelectionChanged;

        _identityService = serviceProvider.GetRequiredService<IAppleIdentityService>();
        _identityState = serviceProvider.GetRequiredService<IAppleIdentityStateService>();
        _googleIdentityService = serviceProvider.GetRequiredService<IGoogleIdentityService>();
        _googleIdentityState = serviceProvider.GetRequiredService<IGoogleIdentityStateService>();
        _toolbarService.RouteChanged += route => _currentRoute = route;

        _splashService = serviceProvider.GetRequiredService<ISplashService>();
        _splashService.OnBlazorReady += OnBlazorReady;

        _blazorWebView = new MacOSBlazorWebView
        {
            HostPage = "wwwroot/index.html",
            ContentInsets = new Thickness(0, 52, 0, 0),
            HideScrollPocketOverlay = true,
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

    // Toolbar caching: create items once, then show/hide natively
    bool _toolbarInitialized;
    Dictionary<string, ToolbarItem> _actionItemMap = new();
    List<NSObject> _nativeMenuTargets = new();

    // Superset signature for the initial build — includes all possible items
    static readonly string SupersetSignature = "refresh,create,import,|S|F|I";

    void OnToolbarChanged()
    {
        Dispatcher.Dispatch(() =>
        {
            if (!_toolbarInitialized)
            {
                // First time: build with all possible items (superset)
                _toolbarInitialized = true;
                FullRebuildToolbar();
            }

            // After initial build: show/hide native items + update commands
            UpdateToolbarVisibility();
        });
    }

    /// <summary>
    /// Toggle native NSToolbarItem visibility and update action commands,
    /// search, and filter menus without triggering a full toolbar rebuild.
    /// </summary>
    void UpdateToolbarVisibility()
    {
        var nsWindow = this.Window?.Handler?.PlatformView as NSWindow;
        var toolbar = nsWindow?.Toolbar;
        if (toolbar == null) return;

        var activeIds = new HashSet<string>();
        foreach (var action in _toolbarService.CurrentItems)
            activeIds.Add(action.Id);

        bool hasSearch = _toolbarService.SearchPlaceholder != null;
        bool hasFilter = _toolbarService.CurrentFilters.Count > 0;
        bool hasIdentity = AppleRoutes.Contains(_currentRoute) || GoogleRoutes.Contains(_currentRoute);
        bool hasPublishWizard = activeIds.Contains("publish-wizard");

        // 1. Update action item visibility via MacOSToolbarItem.IsVisible API and commands
        foreach (var (actionId, toolbarItem) in _actionItemMap)
        {
            bool shouldShow = activeIds.Contains(actionId);
            MacOSToolbarItem.SetIsVisible(toolbarItem, shouldShow);
            toolbarItem.Command = shouldShow
                ? new Command(() => _toolbarService.InvokeToolbarItemClicked(actionId))
                : null;
        }

        // 2. Toggle menu/search visibility and update enabled state via native APIs
        //    (MacOSToolbarItem.IsVisible only works for ToolbarItems, not menus/search)
        var hiddenSelector = new ObjCRuntime.Selector("setHidden:");
        var enabledSelector = new ObjCRuntime.Selector("setEnabled:");
        foreach (var nsItem in toolbar.Items)
        {
            var id = nsItem.Identifier;

            if (id.StartsWith("MauiToolbarItem_"))
            {
                // Update enabled state for action items
                if (int.TryParse(id.Replace("MauiToolbarItem_", ""), out int idx) && idx < _actionItemMap.Count)
                {
                    var actionId = _actionItemMap.Keys.ElementAt(idx);
                    if (nsItem.RespondsToSelector(enabledSelector))
                        _objc_msgSend_bool(nsItem.Handle, enabledSelector.Handle, _toolbarService.IsItemEnabled(actionId));
                }
            }
            else if (id.StartsWith("MauiMenu_"))
            {
                if (!nsItem.RespondsToSelector(hiddenSelector)) continue;
                bool shouldHide;
                if (id == "MauiMenu_0")
                    shouldHide = !hasIdentity;
                else if (id == "MauiMenu_1")
                    shouldHide = !hasPublishWizard;
                else if (id == "MauiMenu_2")
                    shouldHide = !hasFilter;
                else
                    shouldHide = false;
                _objc_msgSend_bool(nsItem.Handle, hiddenSelector.Handle, shouldHide);
            }
            else if (id == "MauiSearchItem")
            {
                if (!nsItem.RespondsToSelector(hiddenSelector)) continue;
                _objc_msgSend_bool(nsItem.Handle, hiddenSelector.Handle, !hasSearch);
                if (hasSearch && nsItem is NSSearchToolbarItem searchNative)
                {
                    searchNative.SearchField.PlaceholderString = _toolbarService.SearchPlaceholder ?? "";
                    searchNative.SearchField.StringValue = "";
                }
            }
        }

        // 3. Rebuild filter submenus natively if filters are active
        if (hasFilter)
            RebuildFilterMenuNatively();

        // 4. Rebuild identity menu natively if on an Apple/Google page
        if (hasIdentity)
            RebuildIdentityMenuNatively();

        // 5. Update publish menu's "Publish Selected…" enabled state
        if (hasPublishWizard)
            UpdatePublishMenuState(activeIds.Contains("publish-selected"));

        // 6. Set custom Copilot icon on sidebar item (replaces SF Symbol)
        SetCopilotIcon(toolbar);
    }

    [System.Runtime.InteropServices.DllImport(ObjCRuntime.Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern void _objc_msgSend_bool(IntPtr receiver, IntPtr selector, bool arg1);

    void SetCopilotIcon(NSToolbar toolbar)
    {
        foreach (var nsItem in toolbar.Items)
        {
            if (nsItem.Identifier != "MauiSidebarItem_0") continue;

            if (_copilotIcon == null)
            {
                var path = NSBundle.MainBundle.PathForResource("copilot-iconTemplate", "png");
                if (path == null) return;
                _copilotIcon = new NSImage(path);
                _copilotIcon.Size = new CGSize(18, 18);
                _copilotIcon.Template = true;
            }

            nsItem.Image = _copilotIcon;
            break;
        }
    }

    /// <summary>
    /// Update the "Publish Selected…" menu item's enabled state in the publish menu.
    /// </summary>
    void UpdatePublishMenuState(bool hasSelection)
    {
        var nsWindow = this.Window?.Handler?.PlatformView as NSWindow;
        var toolbar = nsWindow?.Toolbar;
        if (toolbar == null) return;

        // Publish menu is MauiMenu_1
        foreach (var nsItem in toolbar.Items)
        {
            if (nsItem.Identifier == "MauiMenu_1" && nsItem is NSMenuToolbarItem menuItem && menuItem.Menu != null)
            {
                // "Publish Selected…" is the second item (index 1)
                if (menuItem.Menu.Count >= 2)
                    menuItem.Menu.ItemAt(1)!.Enabled = hasSelection;
                break;
            }
        }
    }

    /// <summary>
    /// Find the filter NSMenuToolbarItem on the native toolbar and rebuild
    /// its menu items without triggering a full toolbar refresh.
    /// </summary>
    void RebuildFilterMenuNatively()
    {
        var nsWindow = this.Window?.Handler?.PlatformView as NSWindow;
        var toolbar = nsWindow?.Toolbar;
        if (toolbar == null) return;

        var filters = _toolbarService.CurrentFilters;
        if (filters.Count == 0) return;

        _nativeMenuTargets.Clear();

        // Find filter menu by identifier (MauiMenu_2)
        NSMenuToolbarItem? filterNative = null;
        foreach (var nsItem in toolbar.Items)
        {
            if (nsItem.Identifier == "MauiMenu_2" && nsItem is NSMenuToolbarItem m)
            {
                filterNative = m;
                break;
            }
        }
        if (filterNative?.Menu == null) return;

        var menu = filterNative.Menu;
        menu.RemoveAllItems();

        for (int fi = 0; fi < filters.Count; fi++)
        {
            var filter = filters[fi];
            var filterId = filter.Id;

            if (fi > 0)
                menu.AddItem(NSMenuItem.SeparatorItem);

            var submenuItem = new NSMenuItem(filter.Label);
            var submenu = new NSMenu(filter.Label);

            for (int oi = 0; oi < filter.Options.Length; oi++)
            {
                var optIndex = oi;
                var optionItem = new NSMenuItem(filter.Options[oi]);
                optionItem.State = oi == filter.SelectedIndex ? NSCellStateValue.On : NSCellStateValue.Off;

                var target = new MenuActionTarget(filterId, optIndex,
                    (fid, idx) => _toolbarService.NotifyFilterChanged(fid, idx));
                optionItem.Target = target;
                optionItem.Action = new ObjCRuntime.Selector("menuItemClicked:");
                _nativeMenuTargets.Add(target);

                submenu.AddItem(optionItem);
            }
            submenuItem.Submenu = submenu;
            menu.AddItem(submenuItem);
        }
    }

    /// <summary>
    /// Rebuild the identity NSMenuToolbarItem's menu natively.
    /// Shows Apple identities on Apple routes, Google identities on Firebase routes.
    /// </summary>
    void RebuildIdentityMenuNatively()
    {
        bool isApple = AppleRoutes.Contains(_currentRoute);
        bool isGoogle = GoogleRoutes.Contains(_currentRoute);

        if (isApple)
            RebuildAppleIdentityMenu();
        else if (isGoogle)
            RebuildGoogleIdentityMenu();
    }

    private IReadOnlyList<GoogleIdentity>? _cachedGoogleIdentities;

    void RebuildGoogleIdentityMenu()
    {
        if (_cachedGoogleIdentities == null || _cachedGoogleIdentities.Count == 0)
        {
            Task.Run(async () =>
            {
                _cachedGoogleIdentities = await _googleIdentityService.GetIdentitiesAsync();
                if (_cachedGoogleIdentities?.Count > 0)
                    Dispatcher.Dispatch(() => UpdateToolbarVisibility());
            });
            return;
        }

        var nsWindow = this.Window?.Handler?.PlatformView as NSWindow;
        var toolbar = nsWindow?.Toolbar;
        if (toolbar == null) return;

        NSMenuToolbarItem? identityNative = null;
        foreach (var nsItem in toolbar.Items)
        {
            if (nsItem.Identifier == "MauiMenu_0" && nsItem is NSMenuToolbarItem m)
            {
                identityNative = m;
                break;
            }
        }
        if (identityNative?.Menu == null) return;

        var identities = _cachedGoogleIdentities;
        var selectedId = _googleIdentityState.SelectedIdentity?.Id;

        if (selectedId == null && identities.Count > 0)
        {
            _googleIdentityState.SetSelectedIdentity(identities[0]);
            selectedId = identities[0].Id;
        }

        var selectedName = _googleIdentityState.SelectedIdentity?.Name ?? identities[0].Name;
        identityNative.Image = NSImage.GetSystemSymbol("flame", null);
        identityNative.Title = selectedName;
        identityNative.Label = selectedName;

        var newMenu = new NSMenu();
        for (int i = 0; i < identities.Count; i++)
        {
            var identity = identities[i];
            var menuItem = new NSMenuItem(identity.Name);
            menuItem.State = identity.Id == selectedId ? NSCellStateValue.On : NSCellStateValue.Off;

            var target = new MenuActionTarget(identity.Id, i, (id, _) =>
            {
                var selected = identities.FirstOrDefault(x => x.Id == id);
                if (selected != null)
                {
                    _googleIdentityState.SetSelectedIdentity(selected);
                    Dispatcher.Dispatch(() => UpdateToolbarVisibility());
                }
            });
            menuItem.Target = target;
            menuItem.Action = new ObjCRuntime.Selector("menuItemClicked:");
            _nativeMenuTargets.Add(target);

            newMenu.AddItem(menuItem);
        }

        // Separator + Settings…
        newMenu.AddItem(NSMenuItem.SeparatorItem);
        var settingsItem = new NSMenuItem("Settings…");
        var settingsTarget = new MenuActionTarget("__settings__", 0, (_, _) =>
        {
            Dispatcher.Dispatch(() => NavigateToRoute("/settings"));
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), async () =>
            {
                try { await EvaluateJavaScriptAsync("document.getElementById('settings-google-identities')?.scrollIntoView({behavior:'smooth'})"); }
                catch { }
            });
        });
        settingsItem.Target = settingsTarget;
        settingsItem.Action = new ObjCRuntime.Selector("menuItemClicked:");
        _nativeMenuTargets.Add(settingsTarget);
        newMenu.AddItem(settingsItem);

        identityNative.Menu = newMenu;
    }

    void RebuildAppleIdentityMenu()
    {
        if (_cachedIdentities == null || _cachedIdentities.Count == 0)
        {
            Task.Run(async () =>
            {
                _cachedIdentities = await _identityService.GetIdentitiesAsync();
                if (_cachedIdentities?.Count > 0)
                    Dispatcher.Dispatch(() => UpdateToolbarVisibility());
            });
            return;
        }

        var nsWindow = this.Window?.Handler?.PlatformView as NSWindow;
        var toolbar = nsWindow?.Toolbar;
        if (toolbar == null) return;

        NSMenuToolbarItem? identityNative = null;
        foreach (var nsItem in toolbar.Items)
        {
            if (nsItem.Identifier == "MauiMenu_0" && nsItem is NSMenuToolbarItem m)
            {
                identityNative = m;
                break;
            }
        }
        if (identityNative?.Menu == null) return;

        var identities = _cachedIdentities;
        var selectedId = _identityState.SelectedIdentity?.Id;

        if (selectedId == null && identities.Count > 0)
        {
            _identityState.SetSelectedIdentity(identities[0]);
            selectedId = identities[0].Id;
        }

        var selectedName = _identityState.SelectedIdentity?.Name ?? identities[0].Name;
        identityNative.Image = NSImage.GetSystemSymbol("apple.logo", null);
        identityNative.Title = selectedName;
        identityNative.Label = selectedName;

        var newMenu = new NSMenu();
        for (int i = 0; i < identities.Count; i++)
        {
            var identity = identities[i];
            var menuItem = new NSMenuItem(identity.Name);
            menuItem.State = identity.Id == selectedId ? NSCellStateValue.On : NSCellStateValue.Off;

            var target = new MenuActionTarget(identity.Id, i, (id, _) =>
            {
                var selected = identities.FirstOrDefault(x => x.Id == id);
                if (selected != null)
                {
                    _identityState.SetSelectedIdentity(selected);
                    Dispatcher.Dispatch(() => UpdateToolbarVisibility());
                }
            });
            menuItem.Target = target;
            menuItem.Action = new ObjCRuntime.Selector("menuItemClicked:");
            _nativeMenuTargets.Add(target);

            newMenu.AddItem(menuItem);
        }

        // Separator + Settings…
        newMenu.AddItem(NSMenuItem.SeparatorItem);
        var settingsItem = new NSMenuItem("Settings…");
        var settingsTarget = new MenuActionTarget("__settings__", 0, (_, _) =>
        {
            Dispatcher.Dispatch(() => NavigateToRoute("/settings"));
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), async () =>
            {
                try { await EvaluateJavaScriptAsync("document.getElementById('settings-apple-identities')?.scrollIntoView({behavior:'smooth'})"); }
                catch { }
            });
        });
        settingsItem.Target = settingsTarget;
        settingsItem.Action = new ObjCRuntime.Selector("menuItemClicked:");
        _nativeMenuTargets.Add(settingsTarget);
        newMenu.AddItem(settingsItem);

        identityNative.Menu = newMenu;
    }

    void FullRebuildToolbar()
    {
            ToolbarItems.Clear();
            _actionItemMap.Clear();

            // Copilot button in sidebar trailing area (convenience mode handles it)
            var copilotItem = CreateCopilotToolbarItem();
            ToolbarItems.Add(copilotItem);

            // Create SUPERSET of all possible action items (hidden ones toggled later)
            // Order matters for layout: [create/import] ← flex → [refresh]
            var supersetActions = new[]
            {
                ("create", "Create", "plus"),
                ("import", "Import", "square.and.arrow.down"),
                ("refresh", "Refresh", "arrow.clockwise"),
            };

            ToolbarItem? refreshItem = null;
            var leadingItems = new List<ToolbarItem>();
            foreach (var (id, label, icon) in supersetActions)
            {
                var actionId = id;
                var item = new ToolbarItem
                {
                    Text = label,
                    IconImageSource = icon,
                    Command = new Command(() => _toolbarService.InvokeToolbarItemClicked(actionId)),
                };
                ToolbarItems.Add(item);
                _actionItemMap[actionId] = item;

                if (actionId == "refresh")
                    refreshItem = item;
                else
                    leadingItems.Add(item);
            }

            // Always create search item
            var searchItem = new MacOSSearchToolbarItem
            {
                Placeholder = _toolbarService.SearchPlaceholder ?? "Search...",
                Text = _toolbarService.SearchText,
            };
            searchItem.TextChanged += (s, e) => _toolbarService.NotifySearchTextChanged(e.NewTextValue ?? "");
            MacOSToolbar.SetSearchItem(this, searchItem);

            // Always create identity menu (hidden on non-Apple pages)
            var identityMenu = CreateAppleIdentityMenu() ?? new MacOSMenuToolbarItem
            {
                Icon = "apple.logo",
                Text = "Identity",
                ShowsTitle = true,
                ShowsIndicator = true,
            };

            // Always create filter menu (hidden when no filters)
            var filterMenu = new MacOSMenuToolbarItem
            {
                Icon = "line.3.horizontal.decrease",
                Text = "Filters",
                ShowsIndicator = true,
            };
            var filters = _toolbarService.CurrentFilters;
            if (filters.Count > 0)
            {
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

            // Publish menu (icon+text, with sub-items for wizard and publish selected)
            var publishMenu = new MacOSMenuToolbarItem
            {
                Icon = "square.and.arrow.up",
                Text = "Publish",
                ShowsTitle = true,
                ShowsIndicator = true,
            };
            var wizardAction = new MacOSMenuItem
            {
                Text = "Publish to CI/CD",
                Icon = "wand.and.stars",
            };
            wizardAction.Clicked += (s, e) => _toolbarService.InvokeToolbarItemClicked("publish-wizard");
            publishMenu.Items.Add(wizardAction);
            var publishSelectedAction = new MacOSMenuItem
            {
                Text = "Publish Selected…",
                Icon = "square.and.arrow.up",
                IsEnabled = false,
            };
            publishSelectedAction.Clicked += (s, e) => _toolbarService.InvokeToolbarItemClicked("publish-selected");
            publishMenu.Items.Add(publishSelectedAction);

            // Build explicit content layout with ALL items:
            // [Identity] [Publish] [Create] [Import] ← FlexibleSpace → [Filter] [Search] [Refresh]
            var layout = new List<MacOSToolbarLayoutItem>();
            layout.Add(MacOSToolbarLayoutItem.Menu(identityMenu));
            layout.Add(MacOSToolbarLayoutItem.Menu(publishMenu));
            foreach (var item in leadingItems)
                layout.Add(MacOSToolbarLayoutItem.Item(item));
            layout.Add(MacOSToolbarLayoutItem.FlexibleSpace);
            layout.Add(MacOSToolbarLayoutItem.Menu(filterMenu));
            layout.Add(MacOSToolbarLayoutItem.Search(searchItem));
            if (refreshItem != null)
                layout.Add(MacOSToolbarLayoutItem.Item(refreshItem));

            MacOSToolbar.SetMenuItems(this, null);
            MacOSToolbar.SetPopUpItems(this, null);
            MacOSToolbar.SetSidebarLayout(this, null);
            MacOSToolbar.SetContentLayout(this, layout);
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

            // Filter menu is MauiMenu_2 (identity=0, publish=1, filter=2)
            NSMenuToolbarItem? filterNative = null;
            foreach (var item in toolbar.Items)
            {
                if (item.Identifier == "MauiMenu_2" && item is NSMenuToolbarItem m)
                {
                    filterNative = m;
                    break;
                }
            }
            if (filterNative?.Menu == null) return;

            var filters = _toolbarService.CurrentFilters;
            int filterIndex = -1;
            for (int i = 0; i < filters.Count; i++)
            {
                if (filters[i].Id == filterId) { filterIndex = i; break; }
            }
            if (filterIndex < 0) return;

            // Account for separator items between filter submenus
            int menuItemIndex = 0;
            for (int i = 0; i < filterIndex; i++)
            {
                menuItemIndex++; // the submenu item
                if (i > 0) menuItemIndex++; // separator before it (skip first)
            }
            if (filterIndex > 0) menuItemIndex++; // separator before this filter

            var menu = filterNative.Menu;
            if (menuItemIndex >= menu.Count) return;
            var submenu = menu.ItemAt(menuItemIndex)?.Submenu;
            if (submenu == null) return;

            for (int i = 0; i < submenu.Count; i++)
            {
                var mi = submenu.ItemAt(i);
                if (mi != null)
                    mi.State = i == selectedIndex ? NSCellStateValue.On : NSCellStateValue.Off;
            }
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

#if !DEBUG
        // Disable right-click context menu in the webview
        Dispatcher.Dispatch(async () =>
        {
            try { await EvaluateJavaScriptAsync("document.addEventListener('contextmenu', e => e.preventDefault())"); }
            catch { }
        });
#endif
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _toolbarService.ToolbarChanged -= OnToolbarChanged;
        _splashService.OnBlazorReady -= OnBlazorReady;
    }
}

/// <summary>
/// NSObject target for native NSMenuItem click actions.
/// Bridges Obj-C target/action to a C# callback for filter menu items
/// that are rebuilt natively without going through the MAUI toolbar manager.
/// </summary>
sealed class MenuActionTarget : NSObject
{
    readonly string _filterId;
    readonly int _optionIndex;
    readonly Action<string, int> _callback;

    public MenuActionTarget(string filterId, int optionIndex, Action<string, int> callback)
    {
        _filterId = filterId;
        _optionIndex = optionIndex;
        _callback = callback;
    }

    [Export("menuItemClicked:")]
    void MenuItemClicked(NSObject sender) => _callback(_filterId, _optionIndex);
}
