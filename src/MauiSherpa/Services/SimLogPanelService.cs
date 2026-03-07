namespace MauiSherpa.Services;

public enum SimInspectorTab { Logs, Apps, Capture, Tools, Permissions }

public class SimInspectorService
{
    public bool IsOpen { get; set; }
    public string? ActiveUdid { get; set; }
    public string? ActiveSimName { get; set; }
    public bool IsPhysicalDevice { get; set; }
    public SimInspectorTab ActiveTab { get; set; } = SimInspectorTab.Logs;

    private Window? _window;

    public event Action? StateChanged;
    public event Action<string>? DeviceChanged;

    public void Open(string udid, string? simName = null, SimInspectorTab tab = SimInspectorTab.Logs, bool isPhysicalDevice = false)
    {
        ActiveUdid = udid;
        ActiveSimName = simName ?? udid;
        ActiveTab = tab;
        IsOpen = true;
        IsPhysicalDevice = isPhysicalDevice;

        if (_window != null)
        {
            // Window already open — switch device if needed
            if (ActiveUdid != udid)
                DeviceChanged?.Invoke(udid);
            StateChanged?.Invoke();
            _window.Activated += OnWindowActivated;
            Application.Current?.ActivateWindow(_window);
            return;
        }

        var tabName = tab.ToString().ToLowerInvariant();
        var titlePrefix = isPhysicalDevice ? "Device Inspector" : "Simulator Inspector";
        var title = $"{titlePrefix} — {simName ?? udid}";
        var page = new InspectorPage($"/inspector/apple/{Uri.EscapeDataString(udid)}/{tabName}", title);
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

    public void SwitchDevice(string udid, string? simName = null)
    {
        if (ActiveUdid == udid) return;
        ActiveUdid = udid;
        ActiveSimName = simName ?? udid;
        if (_window != null)
            _window.Title = $"Simulator Inspector — {ActiveSimName}";
        StateChanged?.Invoke();
        DeviceChanged?.Invoke(udid);
    }

    public void SetTab(SimInspectorTab tab)
    {
        if (ActiveTab == tab) return;
        ActiveTab = tab;
        StateChanged?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        ActiveUdid = null;
        ActiveSimName = null;
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
        ActiveUdid = null;
        ActiveSimName = null;
        StateChanged?.Invoke();
    }
}
