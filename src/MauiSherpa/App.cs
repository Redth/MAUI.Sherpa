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
            Page = new MainPage(splashService)
        };

        window.Created += (s, e) =>
        {
            Console.WriteLine("Window created");
        };
        window.Activated += (s, e) =>
        {
            Console.WriteLine("Window activated");
        };
        window.Deactivated += (s, e) =>
        {
            Console.WriteLine("Window deactivated");
        };
        window.Destroying += (s, e) =>
        {
            Console.WriteLine("Window destroying");
        };

        return window;
    }
}
