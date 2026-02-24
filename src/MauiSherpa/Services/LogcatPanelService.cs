namespace MauiSherpa.Services;

public enum InspectorTab { Logcat, Files, Shell, Capture, Tools, Apps }

public class DeviceInspectorService
{
    public bool IsOpen { get; set; }
    public string? ActiveSerial { get; set; }
    public string? ActiveDeviceName { get; set; }
    public InspectorTab ActiveTab { get; set; } = InspectorTab.Logcat;

    private Window? _window;

    public event Action? StateChanged;
    public event Action<string>? DeviceChanged;

    public void Open(string serial, string? deviceName = null, InspectorTab tab = InspectorTab.Logcat)
    {
        ActiveSerial = serial;
        ActiveDeviceName = deviceName ?? serial;
        ActiveTab = tab;
        IsOpen = true;

        if (_window != null)
        {
            // Window already open — switch device if needed
            if (ActiveSerial != serial)
                DeviceChanged?.Invoke(serial);
            StateChanged?.Invoke();
            _window.Activated += OnWindowActivated;
            Application.Current?.ActivateWindow(_window);
            return;
        }

        var tabName = tab.ToString().ToLowerInvariant();
        var title = $"Device Inspector — {deviceName ?? serial}";
        var page = new InspectorPage($"/inspector/android/{Uri.EscapeDataString(serial)}/{tabName}", title);
        _window = new Window(page)
        {
            Title = title,
            Width = 800,
            Height = 500,
        };
        _window.Destroying += OnWindowDestroying;
        Application.Current?.OpenWindow(_window);
        StateChanged?.Invoke();
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_window != null)
            _window.Activated -= OnWindowActivated;
    }

    public void SwitchDevice(string serial, string? deviceName = null)
    {
        if (ActiveSerial == serial) return;
        ActiveSerial = serial;
        ActiveDeviceName = deviceName ?? serial;
        if (_window != null)
            _window.Title = $"Device Inspector — {ActiveDeviceName}";
        StateChanged?.Invoke();
        DeviceChanged?.Invoke(serial);
    }

    public void SetTab(InspectorTab tab)
    {
        if (ActiveTab == tab) return;
        ActiveTab = tab;
        StateChanged?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        ActiveSerial = null;
        ActiveDeviceName = null;
        if (_window != null)
        {
            _window.Destroying -= OnWindowDestroying;
            Application.Current?.CloseWindow(_window);
            _window = null;
        }
        StateChanged?.Invoke();
    }

    private void OnWindowDestroying(object? sender, EventArgs e)
    {
        _window = null;
        IsOpen = false;
        ActiveSerial = null;
        ActiveDeviceName = null;
        StateChanged?.Invoke();
    }
}
