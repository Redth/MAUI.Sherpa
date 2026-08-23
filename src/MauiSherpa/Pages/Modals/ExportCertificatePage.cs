using MauiSherpa.Core.Interfaces;
using MauiSherpa.Pages.Forms;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Modals;

public class ExportCertificatePage : HybridFormPage<bool>
{
    private readonly int _certificateCount;

    protected override string FormTitle =>
        _certificateCount > 1 ? "Export Certificate Bundle" : "Export Certificate";
    protected override string SubmitButtonText => "Export";
    protected override string BlazorRoute => "/modal/export-certificate";

    public ExportCertificatePage(
        HybridFormBridgeHolder bridgeHolder,
        IReadOnlyList<AppleCertificate> certificates,
        IReadOnlyList<LocalSigningIdentity>? localIdentities = null)
        : base(bridgeHolder)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        if (certificates.Count == 0)
            throw new ArgumentException("At least one certificate must be selected.", nameof(certificates));

        _certificateCount = certificates.Count;
        Bridge.Parameters["Certificates"] = certificates;
        if (localIdentities != null)
            Bridge.Parameters["LocalIdentities"] = localIdentities;
#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 550);
        MacOSPage.SetModalSheetHeight(this, 620);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 550);
        GtkPage.SetModalHeight(this, 620);
#endif
    }
}
