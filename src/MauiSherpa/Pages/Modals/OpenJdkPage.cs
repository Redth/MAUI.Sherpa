using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public record OpenJdkSelection(string? Path, int? MajorVersion);

public sealed class OpenJdkPage : HybridFormPage<OpenJdkSelection>
{
    protected override string FormTitle => "Microsoft OpenJDK";
    protected override string SubmitButtonText => "Use This JDK";
    protected override string BlazorRoute => "/modal/openjdk";

    public OpenJdkPage(HybridFormBridgeHolder holder, bool install, int? majorVersion = null) : base(holder)
    {
        Bridge.Parameters["Install"] = install;
        Bridge.Parameters["MajorVersion"] = majorVersion;
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 700);
        MacOSPage.SetModalSheetHeight(this, 620);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 700);
        GtkPage.SetModalHeight(this, 620);
#endif
    }
}
