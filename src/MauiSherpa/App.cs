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
}
