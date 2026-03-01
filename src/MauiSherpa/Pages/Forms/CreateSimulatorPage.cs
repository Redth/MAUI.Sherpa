using Microsoft.Maui.Controls;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Pages.Forms;

public class CreateSimulatorPage : FormPage<bool>
{
    private readonly ISimulatorService _simulatorService;
    private readonly IReadOnlyList<SimulatorRuntime> _runtimes;
    private readonly IReadOnlyList<SimulatorDeviceType> _allDeviceTypes;

    private Entry _nameEntry = null!;
    private Picker _runtimePicker = null!;
    private Picker _deviceTypePicker = null!;

    private IReadOnlyList<SimulatorRuntime> _availableRuntimes = [];
    private IReadOnlyList<SimulatorDeviceType> _filteredDeviceTypes = [];

    protected override string FormTitle => "Create Simulator";
    protected override string SubmitButtonText => "Create";

    protected override bool CanSubmit =>
        !string.IsNullOrWhiteSpace(_nameEntry?.Text) &&
        _runtimePicker?.SelectedIndex >= 0 &&
        _deviceTypePicker?.SelectedIndex >= 0;

    public CreateSimulatorPage(
        ISimulatorService simulatorService,
        IReadOnlyList<SimulatorRuntime> runtimes,
        IReadOnlyList<SimulatorDeviceType> allDeviceTypes)
    {
        _simulatorService = simulatorService;
        _runtimes = runtimes;
        _allDeviceTypes = allDeviceTypes;
        _availableRuntimes = runtimes.Where(r => r.IsAvailable).ToList();
    }

    protected override View BuildFormContent()
    {
        _nameEntry = CreateEntry("My iPhone Simulator");

        _runtimePicker = CreatePicker(null, _availableRuntimes.Select(r => r.Name).ToList());
        _runtimePicker.IsEnabled = _availableRuntimes.Count > 0;
        _runtimePicker.SelectedIndexChanged += OnRuntimeChanged;

        _deviceTypePicker = CreatePicker(null, []);
        _deviceTypePicker.IsEnabled = false;

        // Auto-select newest iOS runtime
        var newestIos = _availableRuntimes
            .Select((r, i) => (Runtime: r, Index: i))
            .Where(x => x.Runtime.Platform?.Contains("iOS", StringComparison.OrdinalIgnoreCase) == true
                     || x.Runtime.Name.Contains("iOS", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Runtime.Version)
            .FirstOrDefault();

        if (newestIos.Runtime != null)
            _runtimePicker.SelectedIndex = newestIos.Index;
        else if (_availableRuntimes.Count > 0)
            _runtimePicker.SelectedIndex = 0;

        var runtimeGroup = _availableRuntimes.Count > 0
            ? CreateFormGroup("Runtime", _runtimePicker)
            : CreateFormGroup("Runtime", _runtimePicker, "No runtimes found. Install Xcode to get simulator runtimes.");

        return new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                CreateFormGroup("Simulator Name", _nameEntry),
                runtimeGroup,
                CreateFormGroup("Device Type", _deviceTypePicker),
            }
        };
    }

    private void OnRuntimeChanged(object? sender, EventArgs e)
    {
        _deviceTypePicker.Items.Clear();
        _deviceTypePicker.SelectedIndex = -1;

        if (_runtimePicker.SelectedIndex < 0 || _runtimePicker.SelectedIndex >= _availableRuntimes.Count)
        {
            _deviceTypePicker.IsEnabled = false;
            _filteredDeviceTypes = [];
            UpdateSubmitEnabled();
            return;
        }

        var runtime = _availableRuntimes[_runtimePicker.SelectedIndex];
        _filteredDeviceTypes = runtime.SupportedDeviceTypes?.ToList()
            ?? _allDeviceTypes.ToList();

        foreach (var dt in _filteredDeviceTypes)
            _deviceTypePicker.Items.Add(dt.Name);

        _deviceTypePicker.IsEnabled = _filteredDeviceTypes.Count > 0;

        // Auto-select first iPhone device type
        var firstIphone = _filteredDeviceTypes
            .Select((dt, i) => (DeviceType: dt, Index: i))
            .Where(x => x.DeviceType.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (firstIphone.DeviceType != null)
        {
            _deviceTypePicker.SelectedIndex = firstIphone.Index;
            _nameEntry.Text = $"My {firstIphone.DeviceType.Name}";
        }
        else if (_filteredDeviceTypes.Count > 0)
            _deviceTypePicker.SelectedIndex = 0;

        UpdateSubmitEnabled();
    }

    protected override async Task<bool> OnSubmitAsync()
    {
        var name = _nameEntry.Text.Trim();
        var runtime = _availableRuntimes[_runtimePicker.SelectedIndex];
        var deviceType = _filteredDeviceTypes[_deviceTypePicker.SelectedIndex];

        return await _simulatorService.CreateSimulatorAsync(
            name,
            deviceType.Identifier,
            runtime.Identifier);
    }
}
