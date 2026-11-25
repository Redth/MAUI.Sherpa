using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public sealed class AndroidSdkPathPage : HybridFormPage<bool>
{
    protected override string FormTitle => "Android SDK PATH";
    protected override string SubmitButtonText => "Apply PATH Update";
    protected override string BlazorRoute => "/modal/android-sdk-path";

    public AndroidSdkPathPage(HybridFormBridgeHolder holder) : base(holder)
    {
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 740);
        MacOSPage.SetModalSheetHeight(this, 680);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 740);
        GtkPage.SetModalHeight(this, 680);
#endif
    }
}
