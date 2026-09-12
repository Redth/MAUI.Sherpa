using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

/// <summary>
/// Publishes several publish profiles one after another, re-resolving each profile's
/// secrets first — used when a certificate or key changed outside of the profiles.
/// </summary>
public class BatchPublishPage : WizardFormPage<bool>
{
    protected override string FormTitle => "Publish Profiles";
    protected override string DefaultSubmitText => "Publish";
    protected override string BlazorRoute => "/modal/batch-publish";

    public BatchPublishPage(
        HybridFormBridgeHolder bridgeHolder,
        IReadOnlyList<PublishProfile> profiles)
        : base(bridgeHolder)
    {
        Bridge.Parameters["Profiles"] = profiles;
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 650);
        MacOSPage.SetModalSheetHeight(this, 550);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 600);
        GtkPage.SetModalHeight(this, 550);
#endif
    }
}
