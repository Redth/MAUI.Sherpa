using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public sealed class ReleaseNotesPage : HybridViewPage
{
    protected override string FormTitle => "Release Notes";
    protected override string BlazorRoute => "/modal/release-notes";

    public ReleaseNotesPage()
    {
#if MACOSAPP
        MacOSPage.SetModalSheetWidth(this, 760);
        MacOSPage.SetModalSheetHeight(this, 680);
#elif LINUXGTK
        GtkPage.SetModalWidth(this, 760);
        GtkPage.SetModalHeight(this, 680);
#endif
    }
}
