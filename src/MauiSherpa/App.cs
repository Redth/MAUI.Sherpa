using MauiSherpa.Core.Interfaces;

namespace MauiSherpa;

public class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    
    private const string PrefKeyWidth = "window_width";
    private const string PrefKeyHeight = "window_height";
    private const double DefaultWidth = 1280;
    private const double DefaultHeight = 800;
    private const double MinWidth = 600;
    private const double MinHeight = 400;
    
    public App(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var splashService = _serviceProvider.GetRequiredService<ISplashService>();
        
        var savedWidth = Preferences.Default.Get(PrefKeyWidth, DefaultWidth);
        var savedHeight = Preferences.Default.Get(PrefKeyHeight, DefaultHeight);
        
        // Clamp to reasonable bounds
        savedWidth = Math.Max(MinWidth, savedWidth);
        savedHeight = Math.Max(MinHeight, savedHeight);
        
        var window = new Window
        {
            Page = new MainPage(splashService),
            Width = savedWidth,
            Height = savedHeight,
        };

#if WINDOWS
        var toolbarService = _serviceProvider.GetRequiredService<IToolbarService>();
        var titleBarManager = new MauiSherpa.Services.WindowsTitleBarManager(
            toolbarService,
            _serviceProvider.GetRequiredService<IAppleIdentityService>(),
            _serviceProvider.GetRequiredService<IAppleIdentityStateService>(),
            _serviceProvider.GetRequiredService<IGoogleIdentityService>(),
            _serviceProvider.GetRequiredService<IGoogleIdentityStateService>(),
            _serviceProvider);
        var titleBar = titleBarManager.CreateTitleBar();
        window.TitleBar = titleBar;

        window.HandlerChanged += (s, e) =>
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            {
                var appWindow = nativeWindow.AppWindow;
                if (appWindow.TitleBar is { } tb)
                {
                    // Make caption buttons (min/max/close) white on dark background
                    tb.ButtonForegroundColor = Microsoft.UI.Colors.White;
                    tb.ButtonInactiveForegroundColor = Microsoft.UI.Colors.White;
                    tb.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
                    tb.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;
                    tb.ButtonHoverBackgroundColor = new Windows.UI.Color { A = 40, R = 255, G = 255, B = 255 };
                    tb.ButtonPressedBackgroundColor = new Windows.UI.Color { A = 60, R = 255, G = 255, B = 255 };
                }
            }
        };
#endif

        window.SizeChanged += OnWindowSizeChanged;

        window.Created += (s, e) =>
        {
#if MACCATALYST
            // Hide the titlebar title and toolbar.
            var uiApp = UIKit.UIApplication.SharedApplication;
            foreach (var scene in uiApp.ConnectedScenes)
            {
                if (scene is UIKit.UIWindowScene ws)
                {
                    if (ws.Titlebar is { } tb)
                    {
                        tb.TitleVisibility = UIKit.UITitlebarTitleVisibility.Hidden;
                        tb.Toolbar = null;
                    }
                }
            }
#endif
        };

        return window;
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
