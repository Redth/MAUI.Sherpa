using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform.MacOS;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa;

class MacOSApp : Application
{
    private readonly IServiceProvider _serviceProvider;

    private const string PrefKeyWidth = "window_width";
    private const string PrefKeyHeight = "window_height";
    private const double DefaultWidth = 1280;
    private const double DefaultHeight = 800;
    private const double MinWidth = 600;
    private const double MinHeight = 400;

    public MacOSApp(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var blazorPage = new BlazorContentPage(_serviceProvider);
        var flyoutPage = CreateFlyoutPage(blazorPage);

        var savedWidth = Preferences.Default.Get(PrefKeyWidth, DefaultWidth);
        var savedHeight = Preferences.Default.Get(PrefKeyHeight, DefaultHeight);
        savedWidth = Math.Max(MinWidth, savedWidth);
        savedHeight = Math.Max(MinHeight, savedHeight);

        var window = new Window(flyoutPage)
        {
            Width = savedWidth,
            Height = savedHeight,
        };

        window.SizeChanged += OnWindowSizeChanged;
        return window;
    }

    FlyoutPage CreateFlyoutPage(BlazorContentPage blazorPage)
    {
        var flyoutPage = new FlyoutPage
        {
            Detail = new NavigationPage(blazorPage),
            Flyout = new ContentPage { Title = "MAUI Sherpa" },
            FlyoutLayoutBehavior = FlyoutLayoutBehavior.Split,
        };

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
        MacOSFlyoutPage.SetSidebarSelectionChanged(flyoutPage, item =>
        {
            if (item.Tag is string route)
            {
                blazorPage.NavigateToRoute(route);
            }
        });

        return flyoutPage;
    }

    private static CancellationTokenSource? _saveCts;

    private static void OnWindowSizeChanged(object? sender, EventArgs e)
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
                Preferences.Default.Set(PrefKeyWidth, w);
                Preferences.Default.Set(PrefKeyHeight, h);
            }
            catch (TaskCanceledException) { }
        }, token);
    }
}
