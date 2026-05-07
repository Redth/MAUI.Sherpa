using AppKit;
using Foundation;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace MauiSherpa;

[Register("MauiMacOSApp")]
public class MauiMacOSApp : MacOSMauiApplication
{
    protected override MauiApp CreateMauiApp() => MacOSMauiProgram.CreateMauiApp();

    [Export("applicationDidBecomeActive:")]
    public new void ApplicationDidBecomeActive(NSNotification notification)
    {
        try
        {
            base.ApplicationDidBecomeActive(notification);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already activated"))
        {
            // Upstream bug: Window.Activated() throws when called while already active
            // (e.g. when a modal sheet is presented and the app re-activates).
            // Swallow this to prevent a crash.
        }
    }
}
