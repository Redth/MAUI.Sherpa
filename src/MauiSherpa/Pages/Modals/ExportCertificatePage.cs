using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platform.MacOS;
#endif

namespace MauiSherpa.Pages.Modals;

public class ExportCertificatePage : HybridFormPage<bool>
{
    protected override string FormTitle => "Export Certificate";
    protected override string SubmitButtonText => "Export";
    protected override string BlazorRoute => "/modal/export-certificate";

    public ExportCertificatePage(
        HybridFormBridgeHolder bridgeHolder,
        AppleCertificate certificate,
        IReadOnlyList<LocalSigningIdentity>? localIdentities = null)
        : base(bridgeHolder)
    {
        Bridge.Parameters["Certificate"] = certificate;
        if (localIdentities != null)
            Bridge.Parameters["LocalIdentities"] = localIdentities;
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 550);
        MacOSPage.SetModalSheetHeight(this, 500);
#endif
    }
}
