using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform.MacOS;
using Microsoft.Maui.Platform.MacOS.Handlers;
using MauiSherpa.Core.Interfaces;
using AppKit;
using Foundation;

namespace MauiSherpa;

class MacOSApp : Application
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPreferences _preferences;
    private FlyoutPage? _flyoutPage;
    private BlazorContentPage? _blazorPage;
    private List<MacOSSidebarItem>? _sidebarItems;
    private bool _suppressSidebarSync;
    private readonly List<NSObject> _menuHandlers = new(); // prevent GC of menu action targets

    private const string PrefKeyWidth = "window_width";
    private const string PrefKeyHeight = "window_height";
    private const double DefaultWidth = 1280;
    private const double DefaultHeight = 800;
    private const double MinWidth = 600;
    private const double MinHeight = 400;

    public MacOSApp(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _preferences = serviceProvider.GetRequiredService<IPreferences>();

        var toolbarService = serviceProvider.GetRequiredService<IToolbarService>();
        toolbarService.RouteChanged += OnBlazorRouteChanged;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var blazorPage = new BlazorContentPage(_serviceProvider);
        _blazorPage = blazorPage;
        var flyoutPage = CreateFlyoutPage(blazorPage);

        var savedWidth = _preferences.Get(PrefKeyWidth, DefaultWidth);
        var savedHeight = _preferences.Get(PrefKeyHeight, DefaultHeight);
        savedWidth = Math.Max(MinWidth, savedWidth);
        savedHeight = Math.Max(MinHeight, savedHeight);

        var window = new Window(flyoutPage)
        {
            Width = savedWidth,
            Height = savedHeight,
        };

        window.SizeChanged += OnWindowSizeChanged;
        window.Destroying += OnMainWindowDestroying;

        // Add custom menu items after framework finishes menu bar setup
        NSApplication.SharedApplication.BeginInvokeOnMainThread(() => AddAppMenuItems(blazorPage));

        return window;
    }

    private void OnMainWindowDestroying(object? sender, EventArgs e)
    {
        NSApplication.SharedApplication.Terminate(NSApplication.SharedApplication);
    }

    void AddAppMenuItems(BlazorContentPage blazorPage)
    {
        var mainMenu = NSApplication.SharedApplication.MainMenu;
        if (mainMenu == null || mainMenu.Count == 0) return;

        var appMenu = mainMenu.ItemAt(0)?.Submenu;
        if (appMenu == null) return;

        // Insert after "About" (index 0) and the separator (index 1)
        var insertIndex = Math.Min(2, (int)appMenu.Count);

        var sep1 = NSMenuItem.SeparatorItem;
        appMenu.InsertItem(sep1, insertIndex++);

        var settingsHandler = new MenuActionHandler(() => blazorPage.NavigateToRoute("/settings"));
        _menuHandlers.Add(settingsHandler);
        var settingsItem = new NSMenuItem("Settings…", new ObjCRuntime.Selector("menuAction:"), ",");
        settingsItem.Target = settingsHandler;
        appMenu.InsertItem(settingsItem, insertIndex++);

        var doctorHandler = new MenuActionHandler(() => blazorPage.NavigateToRoute("/doctor"));
        _menuHandlers.Add(doctorHandler);
        var doctorItem = new NSMenuItem("Doctor", new ObjCRuntime.Selector("menuAction:"), "");
        doctorItem.Target = doctorHandler;
        appMenu.InsertItem(doctorItem, insertIndex++);

        var sep2 = NSMenuItem.SeparatorItem;
        appMenu.InsertItem(sep2, insertIndex);
    }

    FlyoutPage CreateFlyoutPage(BlazorContentPage blazorPage)
    {
        var flyoutPage = new FlyoutPage
        {
            Detail = new NavigationPage(blazorPage),
            Flyout = new ContentPage { Title = "MAUI Sherpa" },
            FlyoutLayoutBehavior = FlyoutLayoutBehavior.Split,
        };
        _flyoutPage = flyoutPage;

        var sidebarItems = new List<MacOSSidebarItem>
        {
            new MacOSSidebarItem
            {
                Title = "General",
                Children = new List<MacOSSidebarItem>
                {
                    new() { Title = "Dashboard", SystemImage = "house.fill", Tag = "/" },
                    new() { Title = "Doctor", SystemImage = "stethoscope", Tag = "/doctor" },
                    new() { Title = "Settings", SystemImage = "gear", Tag = "/settings" },
                }
            },
            new MacOSSidebarItem
            {
                Title = "Android",
                Children = new List<MacOSSidebarItem>
                {
                    new() { Title = "Devices", SystemImage = "iphone", Tag = "/devices" },
                    new() { Title = "Emulators", SystemImage = "desktopcomputer", Tag = "/emulators" },
                    new() { Title = "SDK Packages", SystemImage = "cube", Tag = "/android-sdk" },
                    new() { Title = "Keystores", SystemImage = "key", Tag = "/keystores" },
                    new() { Title = "Firebase Push", SystemImage = "paperplane", Tag = "/firebase-push" },
                }
            },
            new MacOSSidebarItem
            {
                Title = "Apple",
                Children = new List<MacOSSidebarItem>
                {
                    new() { Title = "Simulators", SystemImage = "ipad", Tag = "/apple-simulators" },
                    new() { Title = "Registered Devices", SystemImage = "iphone", Tag = "/apple-devices" },
                    new() { Title = "Bundle IDs", SystemImage = "touchid", Tag = "/bundle-ids" },
                    new() { Title = "Certificates", SystemImage = "checkmark.seal", Tag = "/certificates" },
                    new() { Title = "Provisioning Profiles", SystemImage = "person.text.rectangle", Tag = "/profiles" },
                    new() { Title = "Root Certificates", SystemImage = "shield", Tag = "/root-certificates" },
                    new() { Title = "Push Testing", SystemImage = "paperplane", Tag = "/push-testing" },
                }
            },
            new MacOSSidebarItem
            {
                Title = "Secrets",
                Children = new List<MacOSSidebarItem>
                {
                    new() { Title = "Secrets", SystemImage = "key.fill", Tag = "/secrets" },
                    new() { Title = "Publish", SystemImage = "square.and.arrow.up", Tag = "/secrets/publish" },
                }
            },
        };

#if DEBUG
        sidebarItems.Add(new MacOSSidebarItem
        {
            Title = "Development",
            Children = new List<MacOSSidebarItem>
            {
                new() { Title = "Debug UI", SystemImage = "ant", Tag = "/debug" },
            }
        });
#endif

        MacOSFlyoutPage.SetSidebarItems(flyoutPage, sidebarItems);
        _sidebarItems = sidebarItems;
        MacOSFlyoutPage.SetSidebarSelectionChanged(flyoutPage, item =>
        {
            if (_suppressSidebarSync) return;
            if (item.Tag is string route)
            {
                blazorPage.NavigateToRoute(route);
            }
        });

        // After handler connects, configure the split view for user-resizable sidebar
        flyoutPage.HandlerChanged += (s, e) =>
        {
            if (flyoutPage.Handler is not NativeSidebarFlyoutPageHandler handler) return;
            if (handler.PlatformView is not NSSplitView splitView) return;

            splitView.Delegate = new SidebarSplitViewDelegate();

            // Set initial sidebar width to 170px after layout settles
            NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                splitView.SetPositionOfDivider(200, 0);
            });
        };

        return flyoutPage;
    }

    void OnBlazorRouteChanged(string route)
    {
        if (_flyoutPage?.Handler is not NativeSidebarFlyoutPageHandler handler) return;
        if (_sidebarItems == null) return;

        // Find the matching sidebar item by tag
        MacOSSidebarItem? target = null;
        foreach (var group in _sidebarItems)
        {
            if (group.Children != null)
            {
                foreach (var child in group.Children)
                {
                    if (child.Tag is string tag && tag == route)
                    {
                        target = child;
                        break;
                    }
                }
            }
            if (target != null) break;
        }
        if (target == null) return;

        // Access the handler's private _outlineView and _dataSource via reflection
        var handlerType = handler.GetType();
        var outlineViewField = handlerType.GetField("_outlineView",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var dataSourceField = handlerType.GetField("_dataSource",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (outlineViewField?.GetValue(handler) is not NSOutlineView outlineView) return;
        var dataSource = dataSourceField?.GetValue(handler);
        if (dataSource == null) return;

        // Call dataSource.GetWrapper(target) to get the SidebarItemWrapper
        var getWrapperMethod = dataSource.GetType().GetMethod("GetWrapper");
        var wrapper = getWrapperMethod?.Invoke(dataSource, [target]) as NSObject;
        if (wrapper == null) return;

        NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            try
            {
                var row = outlineView.RowForItem(wrapper);
                if (row >= 0)
                {
                    _suppressSidebarSync = true;
                    outlineView.SelectRow(row, false);
                    _suppressSidebarSync = false;
                }
            }
            catch { }
        });
    }

    private CancellationTokenSource? _saveCts;

    private void OnWindowSizeChanged(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;

        var w = window.Width;
        var h = window.Height;
        if (w < MinWidth || h < MinHeight) return;

        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
                _preferences.Set(PrefKeyWidth, w);
                _preferences.Set(PrefKeyHeight, h);
            }
            catch (TaskCanceledException) { }
        }, token);
    }
}

/// <summary>
/// Split view delegate that constrains sidebar resize to min/max bounds.
/// </summary>
class SidebarSplitViewDelegate : NSSplitViewDelegate
{
    static readonly nfloat MinSidebarWidth = 150;
    static readonly nfloat MaxSidebarWidth = 400;

    public override nfloat ConstrainSplitPosition(NSSplitView splitView, nfloat proposedPosition, IntPtr subviewDividerIndex)
    {
        return (nfloat)Math.Clamp((double)proposedPosition, (double)MinSidebarWidth, (double)MaxSidebarWidth);
    }

    public override nfloat SetMinCoordinateOfSubview(NSSplitView splitView, nfloat proposedMinimumPosition, IntPtr dividerIndex)
    {
        return MinSidebarWidth;
    }

    public override nfloat SetMaxCoordinateOfSubview(NSSplitView splitView, nfloat proposedMaximumPosition, IntPtr dividerIndex)
    {
        return MaxSidebarWidth;
    }
}

/// <summary>
/// NSObject target for menu item actions that invokes a callback.
/// </summary>
class MenuActionHandler : NSObject
{
    readonly Action _action;

    public MenuActionHandler(Action action) => _action = action;

    [Export("menuAction:")]
    public void MenuAction(NSObject sender) => _action();
}
