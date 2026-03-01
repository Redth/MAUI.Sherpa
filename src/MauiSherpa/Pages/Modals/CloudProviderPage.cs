using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif

namespace MauiSherpa.Pages.Modals;

public class CloudProviderPage : HybridFormPage<CloudSecretsProviderConfig>
{
    private readonly bool _isEditing;

    protected override string FormTitle => _isEditing ? "Edit Cloud Provider" : "Add Cloud Provider";
    protected override string SubmitButtonText => "Save";
    protected override string BlazorRoute => "/modal/cloud-provider";

    public CloudProviderPage(
        HybridFormBridgeHolder bridgeHolder,
        CloudSecretsProviderConfig? provider = null)
        : base(bridgeHolder)
    {
        _isEditing = provider != null;
        if (provider != null)
            Bridge.Parameters["Provider"] = provider;
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 550);
        MacOSPage.SetModalSheetHeight(this, 550);
#endif
    }
}
