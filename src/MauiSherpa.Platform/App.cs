namespace MauiSherpa.Platform;

public class App : Application
{
    public App()
    {
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window
        {
            Page = new MainPage()
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
