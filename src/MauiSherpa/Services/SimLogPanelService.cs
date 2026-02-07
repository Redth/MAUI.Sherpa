namespace MauiSherpa.Services;

public class SimLogPanelService
{
    public bool IsOpen { get; private set; }
    public bool IsMinimized { get; private set; }
    public string? ActiveUdid { get; private set; }
    public string? ActiveSimName { get; private set; }

    public event Action? StateChanged;
    public event Action<string>? DeviceChanged;

    public void Open(string udid, string? simName = null)
    {
        var switchingDevice = IsOpen && ActiveUdid != udid;
        ActiveUdid = udid;
        ActiveSimName = simName ?? udid;
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
