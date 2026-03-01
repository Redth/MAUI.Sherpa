using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif

namespace MauiSherpa.Pages.Modals;

public class PublisherPage : HybridFormPage<SecretsPublisherConfig>
{
    private readonly bool _isEditing;

    protected override string FormTitle => _isEditing ? "Edit CI/CD Publisher" : "Add CI/CD Publisher";
    protected override string SubmitButtonText => "Save";
    protected override string BlazorRoute => "/modal/publisher";

    public PublisherPage(
        HybridFormBridgeHolder bridgeHolder,
        SecretsPublisherConfig? publisher = null)
        : base(bridgeHolder)
    {
        _isEditing = publisher != null;
        if (publisher != null)
            Bridge.Parameters["Publisher"] = publisher;
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 550);
        MacOSPage.SetModalSheetHeight(this, 550);
#endif
    }
}
