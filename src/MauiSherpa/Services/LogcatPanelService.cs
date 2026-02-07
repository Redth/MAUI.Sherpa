namespace MauiSherpa.Services;

public enum InspectorTab { Logcat, Files, Shell, Capture }

public class DeviceInspectorService
{
    public bool IsOpen { get; private set; }
    public bool IsMinimized { get; private set; }
    public string? ActiveSerial { get; private set; }
    public string? ActiveDeviceName { get; private set; }
    public InspectorTab ActiveTab { get; private set; } = InspectorTab.Logcat;

    public event Action? StateChanged;
    public event Action<string>? DeviceChanged;

    public void Open(string serial, string? deviceName = null, InspectorTab tab = InspectorTab.Logcat)
    {
        var switchingDevice = IsOpen && ActiveSerial != serial;
        ActiveSerial = serial;
        ActiveDeviceName = deviceName ?? serial;
        ActiveTab = tab;
        IsOpen = true;
        IsMinimized = false;
        StateChanged?.Invoke();
        if (switchingDevice)
            DeviceChanged?.Invoke(serial);
    }

    public void SwitchDevice(string serial, string? deviceName = null)
    {
        if (ActiveSerial == serial) return;
        ActiveSerial = serial;
        ActiveDeviceName = deviceName ?? serial;
        StateChanged?.Invoke();
        DeviceChanged?.Invoke(serial);
    }

    public void SetTab(InspectorTab tab)
    {
        if (ActiveTab == tab) return;
        ActiveTab = tab;
        StateChanged?.Invoke();
    }

    public void Minimize()
    {
        IsMinimized = true;
        StateChanged?.Invoke();
    }

    public void Restore()
    {
        IsMinimized = false;
        StateChanged?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        IsMinimized = false;
        ActiveSerial = null;
        ActiveDeviceName = null;
        StateChanged?.Invoke();
    }
}
