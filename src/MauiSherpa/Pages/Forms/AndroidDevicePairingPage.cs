using MauiSherpa.Core.Services;
using Microsoft.Maui.Controls;

namespace MauiSherpa.Pages.Forms;

public sealed class AndroidWirelessConnectionRequest(
    bool pairFirst, string? pairingAddress, string? pairingCode, string? connectionAddress)
{
    public bool PairFirst { get; } = pairFirst;
    public string? PairingAddress { get; } = pairingAddress;
    public string? PairingCode { get; } = pairingCode;
    public string? ConnectionAddress { get; } = connectionAddress;
}

public sealed class AndroidDevicePairingPage : FormPage<AndroidWirelessConnectionRequest>
{
    private Picker _mode = null!;
    private Entry _pairingAddress = null!;
    private Entry _pairingCode = null!;
    private Entry _connectionAddress = null!;
    private View _pairingFields = null!;
    private Label _instructions = null!;
    private Label _connectionHint = null!;

    protected override string FormTitle => "Connect Android Device";
    protected override string SubmitButtonText => "Continue";

    protected override bool CanSubmit
    {
        get
        {
            if (_mode?.SelectedIndex == 1)
                return AndroidAdbService.IsValidEndpoint(_connectionAddress?.Text);

            return _mode?.SelectedIndex == 0 &&
                AndroidAdbService.IsValidEndpoint(_pairingAddress?.Text) &&
                AndroidAdbService.IsValidPairingCode(_pairingCode?.Text) &&
                (string.IsNullOrWhiteSpace(_connectionAddress?.Text) ||
                 AndroidAdbService.IsValidEndpoint(_connectionAddress.Text));
        }
    }

    protected override View BuildFormContent()
    {
        _pairingAddress = CreateEntry("192.168.1.10:12345");
        _pairingCode = CreatePasswordEntry("Six-digit code");
        _pairingCode.Keyboard = Keyboard.Numeric;
        _pairingCode.MaxLength = 6;
        _connectionAddress = CreateEntry("192.168.1.10:45678");
        _mode = CreatePicker(null, ["Pair a new device", "Connect an already paired device"]);
        _instructions = new Label { FontSize = 12 };
        _instructions.SetDynamicResource(Label.TextColorProperty, FormTheme.TextSecondary);
        _connectionHint = new Label { FontSize = 11 };
        _connectionHint.SetDynamicResource(Label.TextColorProperty, FormTheme.TextMuted);

        _pairingFields = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                CreateFormGroup("Pairing IP address and port", _pairingAddress),
                CreateFormGroup("Pairing code", _pairingCode)
            }
        };
        _mode.SelectedIndexChanged += (_, _) =>
        {
            var pairing = _mode.SelectedIndex == 0;
            _pairingFields.IsVisible = pairing;
            _instructions.Text = pairing
                ? "On Android 11 or later, open Developer options > Wireless debugging. Choose Pair device with pairing code and keep that screen open."
                : "On your paired device, open Developer options > Wireless debugging and enter the IP address and port shown on the main screen.";
            _connectionHint.Text = pairing
                ? "Optional when pairing. Find this on the main Wireless debugging screen; its port is different from the pairing port."
                : "Use the address on the main Wireless debugging screen, not the pairing-code screen.";
            UpdateSubmitEnabled();
        };
        _mode.SelectedIndex = 0;

        var usbHint = new Label
        {
            Text = "For USB, enable USB debugging and accept the authorization prompt on your device.",
            FontSize = 11
        };
        usbHint.SetDynamicResource(Label.TextColorProperty, FormTheme.TextMuted);

        return new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                _instructions,
                CreateFormGroup("Action", _mode),
                _pairingFields,
                CreateFormGroup("Wireless debugging IP address and port", _connectionAddress),
                _connectionHint,
                usbHint
            }
        };
    }

    protected override Task<AndroidWirelessConnectionRequest> OnSubmitAsync()
    {
        var request = new AndroidWirelessConnectionRequest(
            _mode.SelectedIndex == 0,
            _pairingAddress.Text?.Trim(),
            _pairingCode.Text,
            _connectionAddress.Text?.Trim());
        _pairingCode.Text = "";
        return Task.FromResult(request);
    }
}
