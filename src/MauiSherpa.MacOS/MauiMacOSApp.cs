using AppKit;
using Foundation;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform.MacOS.Hosting;

namespace MauiSherpa;

[Register("MauiMacOSApp")]
public class MauiMacOSApp : MacOSMauiApplication
{
    protected override MauiApp CreateMauiApp() => MacOSMauiProgram.CreateMauiApp();

    // Work around Platform.Maui.MacOS bug: when multiple windows exist,
    // ApplicationDidBecomeActive calls Window.Activated() on a window that is
    // already activated, throwing InvalidOperationException.
    [Export("applicationDidBecomeActive:")]
    public new void ApplicationDidBecomeActive(NSNotification notification)
    {
        try
        {
            base.ApplicationDidBecomeActive(notification);
        }
        catch (InvalidOperationException)
        {
            // Swallow "Window was already activated" — harmless in multi-window scenarios
        }
    }
}
