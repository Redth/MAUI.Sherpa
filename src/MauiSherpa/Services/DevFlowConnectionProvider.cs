using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Services;

public class DevFlowConnectionProvider : IDevFlowConnectionProvider
{
    private readonly DevFlowInspectorService _inspector;

    public DevFlowConnectionProvider(DevFlowInspectorService inspector)
        => _inspector = inspector;

    public bool IsConnected => _inspector.IsOpen && !string.IsNullOrEmpty(_inspector.ActiveHost);
    public string? Host => _inspector.ActiveHost;
    public int Port => _inspector.ActivePort;
    public string? AppName => _inspector.ActiveAppName;
}
