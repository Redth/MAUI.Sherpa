using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform.MacOS;
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
    private NSSplitView? _cachedSplitView;
    private List<MacOSSidebarItem>? _sidebarItems;
    private bool _suppressSidebarSync;
    private readonly List<NSObject> _menuHandlers = new(); // prevent GC of menu action targets

    private const string PrefKeySidebarWidth = "sidebar_width";

    public MacOSApp(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _preferences = serviceProvider.GetRequiredService<IPreferences>();

        var toolbarService = serviceProvider.GetRequiredService<IToolbarService>();
        toolbarService.RouteChanged += OnBlazorRouteChanged;

        // Save state before the process terminates
        NSNotificationCenter.DefaultCenter.AddObserver(
            NSApplication.WillTerminateNotification, _ => SaveState());
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var blazorPage = new BlazorContentPage(_serviceProvider);
        _blazorPage = blazorPage;
        var flyoutPage = CreateFlyoutPage(blazorPage);

        var window = new Window(flyoutPage);

        window.Destroying += OnMainWindowDestroying;

        // Add custom menu items after framework finishes menu bar setup
        NSApplication.SharedApplication.BeginInvokeOnMainThread(() => AddAppMenuItems(blazorPage));

        // Start centralized device monitoring
        _ = Task.Run(async () =>
        {
            try
            {
                var monitor = _serviceProvider.GetRequiredService<IDeviceMonitorService>();
                await monitor.StartAsync();
            }
            catch { }
        });

        return window;
    }

    private void OnMainWindowDestroying(object? sender, EventArgs e)
    {
        try { _serviceProvider.GetRequiredService<IDeviceMonitorService>().Stop(); } catch { }
        NSApplication.SharedApplication.Terminate(NSApplication.SharedApplication);
    }

    private void SaveState()
    {
        try
        {
            // Save sidebar width
            var splitView = _cachedSplitView;
            if (splitView == null)
            {
                var handler = _flyoutPage?.Handler as Microsoft.Maui.Platform.MacOS.Handlers.NativeSidebarFlyoutPageHandler;
                splitView = handler?.SplitViewController?.SplitView;
            }
            if (splitView != null)
            {
                var sidebarView = splitView.ArrangedSubviews.Length > 0
                    ? splitView.ArrangedSubviews[0]
                    : (splitView.Subviews.Length > 0 ? splitView.Subviews[0] : null);
                if (sidebarView != null)
                {
                    var width = (double)sidebarView.Frame.Width;
                    if (width > 0)
                        _preferences.Set(PrefKeySidebarWidth, width);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Caches the split view reference for use during app termination.
    /// </summary>
    internal void CacheSplitView()
    {
        if (_cachedSplitView != null) return;
        var handler = _flyoutPage?.Handler as Microsoft.Maui.Platform.MacOS.Handlers.NativeSidebarFlyoutPageHandler;
        _cachedSplitView = handler?.SplitViewController?.SplitView;
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
        MacOSFlyoutPage.SetUseNativeSidebar(flyoutPage, true);
        _flyoutPage = flyoutPage;

        var sidebarItems = new List<MacOSSidebarItem>
        {
            new() { Title = "Dashboard", SystemImage = "house.fill", Tag = "/" },
            new() { Title = "Doctor", SystemImage = "stethoscope", Tag = "/doctor" },
            new() { Title = "Settings", SystemImage = "gear", Tag = "/settings" },
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

        return flyoutPage;
    }

    void OnBlazorRouteChanged(string route)
    {
        if (_flyoutPage == null) return;

        _suppressSidebarSync = true;
        MacOSFlyoutPage.SelectSidebarItem(_flyoutPage, item => item.Tag is string tag && tag == route);
        _suppressSidebarSync = false;
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
