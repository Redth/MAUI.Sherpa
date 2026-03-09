namespace MauiSherpa.Services;

public enum DevFlowInspectorTab { Tree, Network, Profiling, WebView, Logs }

public class DevFlowInspectorService
{
    public bool IsOpen { get; set; }
    public string? ActiveAgentId { get; set; }
    public string? ActiveAppName { get; set; }
    public string? ActiveHost { get; set; }
    public int ActivePort { get; set; }
    public DevFlowInspectorTab ActiveTab { get; set; } = DevFlowInspectorTab.Tree;

    private Window? _window;

    public event Action? StateChanged;

    public void Open(string agentId, string host, int port, string? appName = null, DevFlowInspectorTab tab = DevFlowInspectorTab.Tree)
    {
        ActiveAgentId = agentId;
        ActiveHost = host;
        ActivePort = port;
        ActiveAppName = appName ?? $"{host}:{port}";
        ActiveTab = tab;
        IsOpen = true;

        if (_window != null)
        {
            StateChanged?.Invoke();
            Application.Current?.ActivateWindow(_window);
            return;
        }

        var tabName = tab.ToString().ToLowerInvariant();
        var title = $"DevFlow — {ActiveAppName}";
        var page = new InspectorPage(
            $"/inspector/devflow/{Uri.EscapeDataString(agentId)}/{tabName}?host={Uri.EscapeDataString(host)}&port={port}",
            title);
        _window = new Window(page)
        {
            Title = title,
            Width = 1200,
            Height = 700,
        };
        _window.Destroying += OnWindowDestroying;
        Application.Current?.OpenWindow(_window);
        StateChanged?.Invoke();
    }

    public void SetTab(DevFlowInspectorTab tab)
    {
        if (ActiveTab == tab) return;
        ActiveTab = tab;
        StateChanged?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        ActiveAgentId = null;
        ActiveAppName = null;
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
        ActiveAgentId = null;
        ActiveAppName = null;
        StateChanged?.Invoke();
    }
}
