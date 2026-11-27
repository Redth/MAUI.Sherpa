using MauiSherpa.Core.Services;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Forms;

public record CreateCertificateFormResult(
    string CertificateType,
    bool SaveToDisk,
    string? Passphrase);

public class CreateCertificatePage : HybridFormPage<CreateCertificateFormResult>
{
    protected override string FormTitle => "Create Certificate";
    protected override string SubmitButtonText => "Create";
    protected override string BlazorRoute => "/modal/create-certificate";

    public CreateCertificatePage(
        HybridFormBridgeHolder bridgeHolder,
        bool requireDiskExport)
        : base(bridgeHolder)
    {
        Bridge.Parameters["RequireDiskExport"] = requireDiskExport;
        Bridge.Parameters["CertificateTypes"] = AppleCertificateTypes.All;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 620);
        MacOSPage.SetModalSheetHeight(this, 640);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 620);
        GtkPage.SetModalHeight(this, 640);
#endif
    }
}
