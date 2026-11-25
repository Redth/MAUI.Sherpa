using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public record AndroidSdkSelection(string Path, bool Install, bool Activate);

public sealed class AndroidSdkLocationPage : HybridFormPage<AndroidSdkSelection>
{
    protected override string FormTitle => "Android SDK";
    protected override string SubmitButtonText => "Use This SDK";
    protected override string BlazorRoute => "/modal/android-sdk-location";

    public AndroidSdkLocationPage(HybridFormBridgeHolder holder, bool install) : base(holder)
    {
        Bridge.Parameters["Install"] = install;
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 700);
        MacOSPage.SetModalSheetHeight(this, 520);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 700);
        GtkPage.SetModalHeight(this, 520);
#endif
    }
}
