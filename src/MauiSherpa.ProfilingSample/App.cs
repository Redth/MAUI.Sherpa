namespace MauiSherpa.ProfilingSample;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window
        {
            Title = "Sherpa Profiling Sample",
            Width = 1440,
            Height = 960,
            MinimumWidth = 960,
            MinimumHeight = 720,
            Page = new NavigationPage(new NativeMainPage())
            {
                BarBackgroundColor = Color.FromArgb("#0f172a"),
                BarTextColor = Colors.White
            }
        };
    }
}
