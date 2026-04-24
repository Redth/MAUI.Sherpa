using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class AddPublisherPage : HybridFormPage<PublishProfilePublisher?>
{
    protected override string FormTitle => "Add Publisher";
    protected override string SubmitButtonText => "Add";
    protected override string BlazorRoute => "/modal/add-publisher";

    public AddPublisherPage(
        HybridFormBridgeHolder bridgeHolder,
        IReadOnlyList<SecretsPublisherConfig> publishers,
        IReadOnlyList<PublishProfilePublisher> existingPublishers)
        : base(bridgeHolder)
    {
        Bridge.Parameters["Publishers"] = publishers;
        Bridge.Parameters["ExistingPublishers"] = existingPublishers;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, true);
        MacOSPage.SetModalSheetMinWidth(this, 500);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, true);
        GtkPage.SetModalMinWidth(this, 500);
#endif
    }
}
