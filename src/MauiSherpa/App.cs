using MauiSherpa.Core.Interfaces;

namespace MauiSherpa;

public class App : Application
{
    private readonly IServiceProvider _serviceProvider;
    
    public App(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var splashService = _serviceProvider.GetRequiredService<ISplashService>();
        
        var window = new Window
        {
            Page = new MainPage(splashService),
        };

        window.Created += (s, e) =>
        {
#if MACCATALYST
            // Negate the titlebar safe area so content extends under the traffic lights.
            // Safe area isn't available immediately — poll until it's set (typically ~500ms).
            int checks = 0;
            window.Dispatcher.StartTimer(TimeSpan.FromMilliseconds(500), () =>
            {
                checks++;
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
                        foreach (var w in ws.Windows)
                        {
                            var rvc = w.RootViewController;
                            if (rvc != null && rvc.AdditionalSafeAreaInsets.Top == 0 && w.SafeAreaInsets.Top > 0)
                            {
                                rvc.AdditionalSafeAreaInsets = new UIKit.UIEdgeInsets(-w.SafeAreaInsets.Top, 0, 0, 0);
                                rvc.View?.SetNeedsLayout();
                            }
                        }
                    }
                }
                return checks < 5;
            });
#endif
        };

        return window;
    }
}
