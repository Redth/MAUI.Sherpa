using Microsoft.UI.Xaml;

namespace MauiSherpa.Platform.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MauiSherpa.Platforms.Windows.WindowsAccentTheme.Apply(Resources);
        base.OnLaunched(args);
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
