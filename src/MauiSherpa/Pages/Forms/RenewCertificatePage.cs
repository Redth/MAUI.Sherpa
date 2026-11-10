using MauiSherpa.Core.Interfaces;
using Microsoft.Maui.Controls;
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
public class RenewCertificatePage : FormPage<RenewCertificateFormResult>
{
    private readonly AppleCertificate _certificate;
    private readonly IReadOnlyList<AppleCertificate> _candidates;
    private readonly int _affectedProfileCount;
    private readonly bool _requireDiskExport;
    private readonly string _typeLabel;

    private Picker _replacementPicker = null!;
    private CheckBox _saveToDiskCheckBox = null!;
    private Entry _passphraseEntry = null!;
    private View _storageGroup = null!;
    private View _passphraseGroup = null!;

    protected override string FormTitle => "Renew Certificate";
    protected override string SubmitButtonText => "Continue";
    protected override double FormBodyHeightRequest => 460;
    protected override bool CanSubmit => _replacementPicker?.SelectedIndex >= 0;

    public RenewCertificatePage(
        AppleCertificate certificate,
        string typeLabel,
        IReadOnlyList<AppleCertificate> candidates,
        int affectedProfileCount,
        bool requireDiskExport)
    {
        _certificate = certificate;
        _typeLabel = typeLabel;
        _candidates = candidates;
        _affectedProfileCount = affectedProfileCount;
        _requireDiskExport = requireDiskExport;

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

    private bool CreatesNewCertificate => _replacementPicker?.SelectedIndex <= 0;

    protected override View BuildFormContent()
    {
        var expiry = _certificate.ExpirationDate < DateTime.UtcNow
            ? $"expired {_certificate.ExpirationDate:MMM d, yyyy}"
            : $"expires {_certificate.ExpirationDate:MMM d, yyyy}";
        var summary = CreateBodyLabel(
            $"Replacing '{_certificate.Name}' ({_typeLabel}, {expiry}).");

        var profileText = _affectedProfileCount switch
        {
            0 => "No provisioning profiles use this certificate, so only the replacement is set up.",
            1 => "1 provisioning profile uses this certificate. You'll choose whether to move it to the replacement next.",
            _ => $"{_affectedProfileCount} provisioning profiles use this certificate. You'll choose which to move to the replacement next."
        };
        var profiles = CreateBodyLabel(profileText);
        profiles.SetDynamicResource(Label.TextColorProperty, FormTheme.TextSecondary);

        var options = new List<string> { $"Create a new {_typeLabel} certificate" };
        options.AddRange(_candidates.Select(candidate =>
            $"Use existing: {candidate.Name} (expires {candidate.ExpirationDate:MMM d, yyyy}, serial …{Tail(candidate.SerialNumber)})"));
        _replacementPicker = CreatePicker(null, options);
        _replacementPicker.SelectedIndex = 0;

        _saveToDiskCheckBox = new CheckBox
        {
            IsChecked = _requireDiskExport,
            IsEnabled = !_requireDiskExport,
            VerticalOptions = LayoutOptions.Center,
        };
        _saveToDiskCheckBox.SetDynamicResource(CheckBox.ColorProperty, FormTheme.AccentPrimary);
        var saveToDiskLabel = new Label
        {
            Text = "Save a copy to disk",
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center,
        };
        saveToDiskLabel.SetDynamicResource(Label.TextColorProperty, FormTheme.TextPrimary);
        var saveToDiskRow = new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { _saveToDiskCheckBox, saveToDiskLabel },
        };

        _storageGroup = CreateFormGroup(
            "Certificate Storage",
            saveToDiskRow,
            _requireDiskExport
                ? "Required because this platform does not provide a supported certificate store"
                : "The new certificate is installed in the platform certificate store and synced to default providers");

        _passphraseEntry = CreatePasswordEntry("Optional; leave empty for an unprotected P12");
        _passphraseGroup = CreateFormGroup("P12 Passphrase", _passphraseEntry, "Only used for the copy saved to disk");

        _saveToDiskCheckBox.CheckedChanged += (_, _) => UpdateVisibility();
        _replacementPicker.SelectedIndexChanged += (_, _) => UpdateVisibility();
        UpdateVisibility();

        var replacementHelp = _candidates.Count == 0
            ? "No newer certificate of this type exists, so a new one will be created."
            : "Pick an existing newer certificate of the same type, or create a new one.";

        return new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                new VerticalStackLayout { Spacing = 6, Children = { summary, profiles } },
                CreateFormGroup("Replacement Certificate", _replacementPicker, replacementHelp),
                _storageGroup,
                _passphraseGroup,
            }
        };
    }

    private void UpdateVisibility()
    {
        _storageGroup.IsVisible = CreatesNewCertificate;
        _passphraseGroup.IsVisible = CreatesNewCertificate && _saveToDiskCheckBox.IsChecked;
    }

    private static Label CreateBodyLabel(string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        label.SetDynamicResource(Label.TextColorProperty, FormTheme.TextPrimary);
        return label;
    }

    private static string Tail(string? serial) =>
        string.IsNullOrEmpty(serial) ? "" : serial.Length <= 6 ? serial : serial[^6..];

    protected override Task<RenewCertificateFormResult> OnSubmitAsync()
    {
        if (CreatesNewCertificate)
        {
            var saveToDisk = _saveToDiskCheckBox.IsChecked;
            return Task.FromResult(new RenewCertificateFormResult(
                null,
                saveToDisk,
                saveToDisk ? _passphraseEntry.Text ?? string.Empty : null));
        }

        var candidate = _candidates[_replacementPicker.SelectedIndex - 1];
        return Task.FromResult(new RenewCertificateFormResult(candidate.Id, false, null));
    }
}
