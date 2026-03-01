namespace MauiSherpa.Services;

public enum SimInspectorTab { Logs, Apps, Capture, Tools }

public class SimInspectorService
{
    public bool IsOpen { get; set; }
    public string? ActiveUdid { get; set; }
    public string? ActiveSimName { get; set; }
    public bool IsSimulator { get; set; } = true;
    public SimInspectorTab ActiveTab { get; set; } = SimInspectorTab.Logs;

    private Window? _window;

    public event Action? StateChanged;
    public event Action<string>? DeviceChanged;

    public void Open(string udid, string? simName = null, SimInspectorTab tab = SimInspectorTab.Logs)
        => Open(udid, simName, isSimulator: true, tab);

    public void Open(string udid, string? deviceName, bool isSimulator, SimInspectorTab tab = SimInspectorTab.Logs)
    {
        ActiveUdid = udid;
        ActiveSimName = deviceName ?? udid;
        IsSimulator = isSimulator;
        ActiveTab = tab;
        IsOpen = true;

        if (_window != null)
        {
            // Window already open — switch device if needed
            if (ActiveUdid != udid)
                DeviceChanged?.Invoke(udid);
            _window.Title = BuildTitle(deviceName ?? udid, isSimulator);
            StateChanged?.Invoke();
            _window.Activated += OnWindowActivated;
            Application.Current?.ActivateWindow(_window);
            return;
        }

        var tabName = tab.ToString().ToLowerInvariant();
        var title = BuildTitle(deviceName ?? udid, isSimulator);
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

    public void SwitchDevice(string udid, string? deviceName = null, bool? isSimulator = null)
    {
        if (ActiveUdid == udid) return;
        ActiveUdid = udid;
        ActiveSimName = deviceName ?? udid;
        if (isSimulator.HasValue)
            IsSimulator = isSimulator.Value;
        if (_window != null)
            _window.Title = BuildTitle(ActiveSimName, IsSimulator);
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

    private static string BuildTitle(string deviceName, bool isSimulator)
        => isSimulator
            ? $"Simulator Inspector — {deviceName}"
            : $"Apple Inspector — {deviceName}";
}
