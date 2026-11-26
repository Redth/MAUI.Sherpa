using MauiSherpa.Core.Interfaces;
#if MACOSAPP
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif
#if LINUXGTK
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
#endif

namespace MauiSherpa.Pages.Forms;

/// <summary>
/// The replacement the user picked: a new certificate (<see cref="ExistingCertificateId"/> null)
/// or an existing newer one.
/// </summary>
public record RenewCertificateFormResult(
    string? ExistingCertificateId,
    bool SaveToDisk,
    string? Passphrase);

/// <summary>
/// Step 1 of certificate renewal: choose the replacement certificate.
/// </summary>
public class RenewCertificatePage : HybridFormPage<RenewCertificateFormResult>
{
    protected override string FormTitle => "Renew Certificate";
    protected override string SubmitButtonText => "Continue";
    protected override string BlazorRoute => "/modal/renew-certificate";

    public RenewCertificatePage(
        HybridFormBridgeHolder bridgeHolder,
        AppleCertificate certificate,
        string typeLabel,
        IReadOnlyList<AppleCertificate> candidates,
        int affectedProfileCount,
        bool requireDiskExport)
        : base(bridgeHolder)
    {
        Bridge.Parameters["Certificate"] = certificate;
        Bridge.Parameters["TypeLabel"] = typeLabel;
        Bridge.Parameters["Candidates"] = candidates;
        Bridge.Parameters["AffectedProfileCount"] = affectedProfileCount;
        Bridge.Parameters["RequireDiskExport"] = requireDiskExport;

#if MACOSAPP
        MacOSPage.SetModalSheetSizesToContent(this, false);
        MacOSPage.SetModalSheetWidth(this, 620);
        MacOSPage.SetModalSheetHeight(this, 660);
#elif LINUXGTK
        GtkPage.SetModalSizesToContent(this, false);
        GtkPage.SetModalWidth(this, 620);
        GtkPage.SetModalHeight(this, 660);
#endif
    }
}
