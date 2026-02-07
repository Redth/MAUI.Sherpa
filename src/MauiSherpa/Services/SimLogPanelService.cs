namespace MauiSherpa.Services;

public enum SimInspectorTab { Logs, Apps, Capture, Tools }

public class SimInspectorService
{
    public bool IsOpen { get; private set; }
    public bool IsMinimized { get; private set; }
    public string? ActiveUdid { get; private set; }
    public string? ActiveSimName { get; private set; }
    public SimInspectorTab ActiveTab { get; private set; } = SimInspectorTab.Logs;

    public event Action? StateChanged;
    public event Action<string>? DeviceChanged;

    public void Open(string udid, string? simName = null, SimInspectorTab tab = SimInspectorTab.Logs)
    {
        var switchingDevice = IsOpen && ActiveUdid != udid;
        ActiveUdid = udid;
        ActiveSimName = simName ?? udid;
        ActiveTab = tab;
        IsOpen = true;
        IsMinimized = false;
        StateChanged?.Invoke();
        if (switchingDevice)
            DeviceChanged?.Invoke(udid);
    }

    public void SwitchDevice(string udid, string? simName = null)
    {
        if (ActiveUdid == udid) return;
        ActiveUdid = udid;
        ActiveSimName = simName ?? udid;
        StateChanged?.Invoke();
        DeviceChanged?.Invoke(udid);
    }

    public void SetTab(SimInspectorTab tab)
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
        ActiveUdid = null;
        ActiveSimName = null;
        StateChanged?.Invoke();
    }
}
